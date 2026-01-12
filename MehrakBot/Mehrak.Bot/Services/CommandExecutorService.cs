#region

using Mehrak.Bot.Authentication;
using Mehrak.Bot.Extensions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
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

    Task ExecuteAsync(uint profile);

    void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null);
}

internal class CommandExecutorService<TContext> : CommandExecutorServiceBase<TContext>
    where TContext : IApplicationContext
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly IAttachmentStorageService m_AttachmentService;

    public CommandExecutorService(
        IServiceProvider serviceProvider,
        UserDbContext userContext,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IMetricsService metricsService,
        IAttachmentStorageService attachmentService,
        ILogger<CommandExecutorService<TContext>> logger
    ) : base(userContext, commandRateLimitService, authenticationMiddleware, metricsService, logger)
    {
        m_ServiceProvider = serviceProvider;
        m_AttachmentService = attachmentService;
    }

    public override async Task ExecuteAsync(uint profile)
    {
        Logger.LogInformation(
            "User {User} used command {Command}",
            Context.Interaction.User.Id, CommandName);

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
                if (service is IAttachmentApplicationService<TContext>)
                {
                    if (commandResult.Data.Components.FirstOrDefault(x => x is ICommandResultAttachment) is ICommandResultAttachment attachment)
                    {
                        var stream = await m_AttachmentService.DownloadAsync(attachment.FileName)!;
                        if (stream != null)
                            message.AddAttachments(new AttachmentProperties(attachment!.FileName, stream.Content));
                        else
                        {
                            Logger.LogWarning("Attachment {Attachment} not found for command {Command}",
                                attachment.FileName, CommandName);
                            MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
                            await authResult.Context!.Interaction.SendFollowupMessageAsync(
                                new InteractionMessageProperties().WithContent(
                                        "Attachment not found. Please try again later.")
                                    .WithFlags(MessageFlags.Ephemeral));
                            return;
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Attachment not found in command result for command {Command}",
                            CommandName);
                        MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, false);
                        await authResult.Context!.Interaction.SendFollowupMessageAsync(
                            new InteractionMessageProperties().WithContent(
                                    "Attachment not found. Please try again later.")
                                .WithFlags(MessageFlags.Ephemeral));
                        return;
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
