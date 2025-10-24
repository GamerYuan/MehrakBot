using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Extensions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

namespace Mehrak.Bot.Services;

public interface ICommandExecutorService<TContext> where TContext : IApplicationContext
{
    IInteractionContext Context { get; set; }
    TContext ApplicationContext { get; set; }

    Task ExecuteAsync(uint profile);

    void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null);
}

internal class CommandExecutorService<TContext> : CommandExecutorServiceBase<TContext> where TContext : ApplicationContextBase
{
    private readonly IServiceProvider m_ServiceProvider;

    public CommandExecutorService(
        IServiceProvider serviceProvider,
        IUserRepository userRepository,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IMetricsService metricsService,
        ILogger<CommandExecutorService<TContext>> logger
    ) : base(userRepository, commandRateLimitService, authenticationMiddleware, metricsService, logger)
    {
        m_ServiceProvider = serviceProvider;
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

        var authResult = await AuthenticationMiddleware.GetAuthenticationAsync(new(Context, profile));

        if (authResult.IsSuccess)
        {
            using var observer = MetricsService.ObserveCommandDuration(CommandName);

            await authResult.Context!.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var server = ApplicationContext.GetParameter<Server?>("server");
            var game = ApplicationContext.GetParameter<Game>("game");

            server ??= GetLastUsedServerAsync(authResult.User, game, profile);
            if (server == null)
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent(
                            "Server is required for first time use. Please specify the server parameter.")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await UpdateLastUsedServerAsync(authResult.User, profile, game, server.Value);

            ApplicationContext.LToken = authResult.LToken;
            ApplicationContext.LtUid = authResult.LtUid;
            ApplicationContext.Server = server.Value;

            var service =
                m_ServiceProvider.GetRequiredService<IApplicationService<TContext>>();
            var commandResult = await service
                .ExecuteAsync(ApplicationContext)
                .ConfigureAwait(false);

            MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, commandResult.IsSuccess);

            if (commandResult.IsSuccess)
            {
                if (!IsResponseEphemeral || !commandResult.Data.IsEphemeral)
                    await authResult.Context!.Interaction.SendFollowupMessageAsync("Command Execution Completed");
                await authResult.Context.Interaction.SendFollowupMessageAsync(commandResult.Data.ToMessage());
            }
            else
            {
                await authResult.Context!.Interaction.SendFollowupMessageAsync(commandResult.ErrorMessage);
            }
        }
        else if (authResult.Status == AuthStatus.Failure)
        {
            await authResult.Context!.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent(authResult.ErrorMessage!)
                    .WithFlags(MessageFlags.Ephemeral)));
        }
    }
}
