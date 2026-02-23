using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace Mehrak.Bot.Services;

internal class BotRichStatusService : BackgroundService
{
    private const int RefreshMinutes = 60;

    private readonly GatewayClient m_Client;
    private readonly UserCountTrackerService m_UserTracker;
    private readonly ILogger<BotRichStatusService> m_Logger;

    public BotRichStatusService(GatewayClient gatewayClient, UserCountTrackerService userTracker, ILogger<BotRichStatusService> logger)
    {
        m_Client = gatewayClient;
        m_UserTracker = userTracker;
        m_Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (m_Client.Status != WebSocketStatus.Ready)
        {
            await Task.Delay(50, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var userCount = await m_UserTracker.GetUserCountAsync();
            m_Logger.LogDebug("Updating presence with {UserCount} users", userCount);
            if (userCount > 0)
            {
                await m_Client.UpdatePresenceAsync(new PresenceProperties(NetCord.UserStatusType.Online)
                {
                    Activities =
                    [
                        new UserActivityProperties($"with {userCount} users", UserActivityType.Playing)
                    ]
                }, cancellationToken: stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(RefreshMinutes), stoppingToken);
        }
    }
}
