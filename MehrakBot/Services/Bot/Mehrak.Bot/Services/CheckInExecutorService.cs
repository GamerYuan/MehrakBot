#region

using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Extensions;
using Mehrak.Domain.Protobuf;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace Mehrak.Bot.Services;

internal class CheckInExecutorService : CommandExecutorServiceBase<CheckInApplicationContext>
{
    private readonly IApplicationService<CheckInApplicationContext> m_Service;
    internal override string CommandName { get; set; } = Domain.Common.CommandName.Common.CheckIn;

    public CheckInExecutorService(
        IApplicationService<CheckInApplicationContext> service,
        UserDbContext userContext,
        ApplicationService.ApplicationServiceClient applicationClient,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IMetricsService metricsService,
        ILogger<CheckInExecutorService> logger)
        : base(userContext, commandRateLimitService, authenticationMiddleware, metricsService, applicationClient, logger)
    {
        m_Service = service;
    }

    public override async Task ExecuteAsync(int profile)
    {
        Logger.LogInformation(
            "User {User} used command {Command}",
            Context.Interaction.User.Id, CommandName);

        if (!await ValidateRateLimitAsync()) return;

        var authResult =
            await AuthenticationMiddleware.GetAuthenticationAsync(new AuthenticationRequest(Context, profile));

        if (authResult.IsSuccess)
        {
            using var observer = MetricsService.ObserveCommandDuration(CommandName);

            ApplicationContext.LToken = authResult.LToken;
            ApplicationContext.LtUid = authResult.LtUid;

            var commandResult = await m_Service
                .ExecuteAsync(ApplicationContext)
                .ConfigureAwait(false);

            MetricsService.TrackCommand(CommandName, Context.Interaction.User.Id, commandResult.IsSuccess);

            if (commandResult.IsSuccess)
                await authResult.Context!.Interaction.SendFollowupMessageAsync(commandResult.Data.ToMessage());
            else
                await authResult.Context!.Interaction.SendFollowupMessageAsync(commandResult.ErrorMessage);
        }
        else if (authResult.Status == AuthStatus.Failure)
        {
            await authResult.Context!.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithContent(authResult.ErrorMessage!)
                    .WithFlags(MessageFlags.Ephemeral));
        }
    }
}
