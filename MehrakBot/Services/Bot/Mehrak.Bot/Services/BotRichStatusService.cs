using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace Mehrak.Bot.Services;

internal class BotRichStatusService : BackgroundService
{
    private const int RefreshMinutes = 60;

    private readonly GatewayClient m_Client;
    private readonly ClickhouseClientService m_ClickhouseClient;
    private readonly ILogger<BotRichStatusService> m_Logger;

    public BotRichStatusService(GatewayClient gatewayClient, ClickhouseClientService clickhouseClient, ILogger<BotRichStatusService> logger)
    {
        m_Client = gatewayClient;
        m_ClickhouseClient = clickhouseClient;
        m_Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = UpdateStatusTask(stoppingToken);
    }

    private async Task UpdateStatusTask(CancellationToken token)
    {
        while (m_Client.Status != WebSocketStatus.Ready)
        {
            await Task.Delay(50, token);
        }

        while (!token.IsCancellationRequested)
        {
            var userCount = await m_ClickhouseClient.GetUniqueUserCountAsync();
            m_Logger.LogDebug("Updating presence with {UserCount} users", userCount);
            if (userCount > 0)
            {
                await m_Client.UpdatePresenceAsync(new PresenceProperties(NetCord.UserStatusType.Online)
                {
                    Activities =
                    [
                        new UserActivityProperties($"with {userCount} users", UserActivityType.Playing)
                    ]
                }, cancellationToken: token);
            }

            await Task.Delay(TimeSpan.FromMinutes(RefreshMinutes), token);
        }
    }
}
