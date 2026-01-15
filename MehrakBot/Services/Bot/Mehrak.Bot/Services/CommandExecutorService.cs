#region

using Mehrak.Bot.Authentication;
using Mehrak.Bot.Extensions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Services;

public interface ICommandExecutorService<TContext> where TContext : IApplicationContext
{
    IInteractionContext Context { get; set; }
    TContext ApplicationContext { get; set; }
    bool ValidateServer { get; set; }

    Task ExecuteAsync(int profile);

    void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null);
}

internal class CommandExecutorService<TContext> : CommandExecutorServiceBase<TContext>
    where TContext : IApplicationContext
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly IAttachmentStorageService m_AttachmentService;
    private readonly IImageRepository m_ImageRepository;

    public CommandExecutorService(
        IServiceProvider serviceProvider,
        UserDbContext userContext,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IMetricsService metricsService,
        IAttachmentStorageService attachmentService,
        IImageRepository imageRepository,
        ILogger<CommandExecutorService<TContext>> logger
    ) : base(userContext, commandRateLimitService, authenticationMiddleware, metricsService, logger)
    {
        m_ServiceProvider = serviceProvider;
        m_AttachmentService = attachmentService;
        m_ImageRepository = imageRepository;
    }

    public override async Task ExecuteAsync(int profile)
    {
        Logger.LogInformation(
            "User {User} used command {Command}",
            Context.Interaction.User.Id, CommandName);

        if (profile <= 0 || profile > 10)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Profile specified is outside of allowed range (1 - 10)"))));
            return;
        }

        var invalid = Validators.Where(x => !x.IsValid(ApplicationContext)).Select(x => x.ErrorMessage);
        if (invalid.Any())
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties(
                    $"Error when validating input:\n{string.Join("\n", invalid)}"))));
            return;
        }

        if (!await ValidateRateLimitAsync()) return;

        var authResult =
            await AuthenticationMiddleware.GetAuthenticationAsync(new AuthenticationRequest(Context, profile));

        if (authResult.IsSuccess)
        {
            using var observer = MetricsService.ObserveCommandDuration(CommandName);

            if (ValidateServer)
            {
                var server = ApplicationContext.GetParameter<string?>("server");
                var game = ApplicationContext.GetParameter<Game>("game");

                if (server == null)
                {
                    server = await GetLastUsedServerAsync(authResult.User, game, profile);
                    if (server == null)
                    {
                        await authResult.Context.Interaction.SendFollowupMessageAsync(
                            new InteractionMessageProperties().WithContent(
                                    "Server is required for first time use. Please specify the server parameter.")
                                .WithFlags(MessageFlags.Ephemeral));
                        return;
                    }

                    ApplicationContext.SetParameter("server", server);
                }

                await UpdateLastUsedServerAsync(authResult.User, profile, game, server);
            }

            ApplicationContext.LToken = authResult.LToken;
            ApplicationContext.LtUid = authResult.LtUid;

            var service =
                m_ServiceProvider.GetRequiredService<IApplicationService<TContext>>();
            var commandResult = await service
                .ExecuteAsync(ApplicationContext)
                .ConfigureAwait(false);


            if (commandResult.IsSuccess)
            {
                var message = commandResult.Data.ToMessage();

                var attachments = commandResult.Data.Components
                    .OfType<ICommandResultAttachment>()
                    .Concat(commandResult.Data.Components
                        .OfType<CommandSection>()
                        .Select(s => s.Attachment)
                        .Where(a => a != null));

                foreach (var attachment in attachments)
                {
                    Stream? stream = null;
                    if (attachment.SourceType == AttachmentSourceType.ImageStorage)
                    {
                        try
                        {
                            stream = await m_ImageRepository.DownloadFileToStreamAsync(attachment.FileName);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Failed to download image {FileName} from ImageRepository", attachment.FileName);
                        }
                    }
                    else
                    {
                        var downloadResult = await m_AttachmentService.DownloadAsync(attachment.FileName);
                        stream = downloadResult?.Content;
                    }

                    if (stream != null)
                    {
                        message.AddAttachments(new AttachmentProperties(attachment.FileName, stream));
                    }
                    else
                    {
                        Logger.LogWarning("Attachment {Attachment} not found for command {Command}",
                            attachment.FileName, CommandName);
                    }
                }

                MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, true);

                if (IsResponseEphemeral || commandResult.Data.IsEphemeral)
                {
                    await authResult.Context!.Interaction.SendFollowupMessageAsync(message
                        .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
                }
                else
                {
                    await authResult.Context!.Interaction.SendFollowupMessageAsync("Command Execution Completed");
                    await authResult.Context.Interaction.SendFollowupMessageAsync(message
                        .AddComponents(new ActionRowProperties([
                            new ButtonProperties("remove_card", "Remove", ButtonStyle.Danger)
                        ])));
                }
            }
            else
            {
                MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
                await authResult.Context!.Interaction.SendFollowupMessageAsync(commandResult.ErrorMessage);
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
