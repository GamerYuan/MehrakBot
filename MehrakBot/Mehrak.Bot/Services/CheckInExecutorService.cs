using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Extensions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace Mehrak.Bot.Services;

internal class CheckInExecutorService : CommandExecutorServiceBase<CheckInApplicationContext>
{
    private readonly IApplicationService<CheckInApplicationContext> m_Service;
    internal override string CommandName { get; set; } = "checkin";

    public CheckInExecutorService(
        IApplicationService<CheckInApplicationContext> service,
        IUserRepository userRepository,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IMetricsService metricsService,
        ILogger<CheckInExecutorService> logger)
        : base(userRepository, commandRateLimitService, authenticationMiddleware, metricsService, logger)
    {
        m_Service = service;
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
                    $"Error when validating input:\n{string.Join('\n', invalid)}"))));
            return;
        }

        if (!await ValidateRateLimitAsync()) return;

        var authResult = await AuthenticationMiddleware.GetAuthenticationAsync(new(Context, profile));

        if (authResult.IsSuccess)
        {
            using var observer = MetricsService.ObserveCommandDuration(CommandName);

            await authResult.Context!.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            ApplicationContext.LToken = authResult.LToken;
            ApplicationContext.LtUid = authResult.LtUid;

            var commandResult = await m_Service
                .ExecuteAsync(ApplicationContext)
                .ConfigureAwait(false);

            MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, commandResult.IsSuccess);

            if (commandResult.IsSuccess)
            {
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
