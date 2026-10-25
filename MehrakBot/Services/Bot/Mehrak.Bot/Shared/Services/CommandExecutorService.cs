#region

using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Extensions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Protobuf;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Shared.Services;

public interface ICommandExecutorService
{
    IInteractionContext Context { get; set; }
    bool ValidateServer { get; set; }
    Dictionary<string, object> Parameters { get; set; }

    Task ExecuteAsync(int profile);

    void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null);
}

internal class CommandExecutorService : CommandExecutorServiceBase
{
    private readonly IAttachmentStorageService m_AttachmentService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICommandUiLocalizer m_Localizer;

    public CommandExecutorService(
        UserDbContext userContext,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IBotMetrics metricsService,
        ApplicationService.ApplicationServiceClient applicationClient,
        IAttachmentStorageService attachmentService,
        IImageRepository imageRepository,
        ILogger<CommandExecutorService> logger,
        ICommandUiLocalizer? localizer = null
    ) : base(userContext, authenticationMiddleware, metricsService, applicationClient, logger)
    {
        m_AttachmentService = attachmentService;
        m_ImageRepository = imageRepository;
        m_Localizer = localizer ?? CommandUiLocalizer.Default;
    }

    public override async Task ExecuteAsync(int profile)
    {
        var culture = BotUiCulture.Resolve(Context);

        Logger.LogInformation(
            "User {User} used command {Command}",
            Context.Interaction.User.Id, CommandName);

        if (profile <= 0 || profile > 10)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties(
                    m_Localizer.Get(culture, CommandUiKeys.ExecutorProfileOutOfRange)))));
            return;
        }

        var invalid = Validators.Where(x => !x.IsValid(this))
            .Select(x => m_Localizer.Get(culture, x.ErrorMessage)).ToArray();
        if (invalid.Length > 0)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties(
                    m_Localizer.Get(culture, CommandUiKeys.ExecutorValidationFailed,
                        string.Join("\n", invalid))))));
            return;
        }

        var authResult =
            await AuthenticationMiddleware.GetAuthenticationAsync(new AuthenticationRequest(Context, profile));

        if (authResult.IsSuccess)
        {
            using var observer = MetricsService.ObserveCommandDuration(CommandName);

            if (ValidateServer)
            {
                var server = GetParam<string?>("server");
                var game = GetParam<Game>("game");

                if (server == null)
                {
                    server = await GetLastUsedServerAsync(authResult.User, game, profile);
                    if (server == null)
                    {
                        await authResult.Context.Interaction.SendFollowupMessageAsync(
                            new InteractionMessageProperties().WithContent(
                                    m_Localizer.Get(culture, CommandUiKeys.ExecutorServerRequired))
                                .WithFlags(MessageFlags.Ephemeral));
                        return;
                    }

                    Parameters.Add("server", server);
                }

                await UpdateLastUsedServerAsync(authResult.User, profile, game, server);
            }

            List<IDisposable> disposable = [];

            try
            {
                var commandResult = await DispatchCommand(CommandName, Context.Interaction.User.Id, authResult.LtUid, authResult.LToken,
                Parameters.Select(x => (x.Key, x.Value)));

                if (commandResult.IsSuccess)
                {
                    var presentation = CommandOutcomePresenter.Present(commandResult.Outcome, Context.Interaction.User.Id,
                        m_Localizer, culture);
                    var message = presentation.ToMessage();

                    var attachments = presentation.Attachments;

                    foreach (var attachment in attachments)
                    {
                        Stream? stream = null;
                        if (attachment.SourceType == Domain.Command.Models.AttachmentSourceType.ImageStorage)
                        {
                            try
                            {
                                stream = await m_ImageRepository.DownloadFileToStreamAsync(attachment.StorageFileName ?? attachment.FileName);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, "Failed to download image {FileName} from ImageRepository",
                                    attachment.StorageFileName ?? attachment.FileName);
                            }
                        }
                        else
                        {
                            var downloadResult = await m_AttachmentService.DownloadAsync(attachment.StorageFileName ?? attachment.FileName);
                            stream = downloadResult?.Content;
                        }

                        if (stream != null)
                        {
                            disposable.Add(stream);
                            message.AddAttachments(new AttachmentProperties(attachment.FileName, stream));
                        }
                        else
                        {
                            Logger.LogWarning("Attachment {Attachment} not found for command {Command}",
                                attachment.FileName, CommandName);
                        }
                    }

                    MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, true);

                    if (IsResponseEphemeral || presentation.IsEphemeral)
                    {
                        await authResult.Context!.Interaction.SendFollowupMessageAsync(message
                            .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
                    }
                    else
                    {
                        await authResult.Context!.Interaction.SendFollowupMessageAsync(
                            new InteractionMessageProperties().WithContent(presentation.EphemeralMessage ??
                                m_Localizer.Get(culture, CommandUiKeys.ExecutorCompleted)).WithFlags(MessageFlags.Ephemeral));
                        await authResult.Context.Interaction.SendFollowupMessageAsync(message
                            .AddComponents(new ActionRowProperties([
                                new ButtonProperties("remove_card",
                                    m_Localizer.Get(culture, CommandUiKeys.ExecutorRemoveButton), ButtonStyle.Danger)
                            ])));
                    }
                }
                else
                {
                    MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
                    await authResult.Context!.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithContent(
                                CommandOutcomePresenter.ErrorText(commandResult.Error, m_Localizer, culture))
                            .WithFlags(MessageFlags.Ephemeral));
                }
            }
            catch (Exception e)
            {
                MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
                Logger.LogError(e, "Error executing command {Command} for user {User}", CommandName,
                    Context.Interaction.User.Id);
                await authResult.Context!.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithContent(
                            m_Localizer.Get(culture, CommandUiKeys.ExecutorUnexpectedError))
                        .WithFlags(MessageFlags.Ephemeral));
            }
            finally
            {
                disposable.ForEach(x => x.Dispose());
            }
        }
        else if (authResult.Status == AuthStatus.Failure)
        {
            MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
            await authResult.Context!.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithContent(authResult.ErrorMessage)
                    .WithFlags(MessageFlags.Ephemeral));
        }
        else if (authResult.Status == AuthStatus.NotFound)
        {
            MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
            await authResult.Context!.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent(authResult.ErrorMessage)
                    .WithFlags(MessageFlags.Ephemeral)));
        }
    }
}
