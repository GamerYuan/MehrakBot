using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace Mehrak.Bot.Services;

internal class BotLatencyService : IHostedService
{
    private readonly CancellationTokenSource m_Cts;
    private readonly IMetricsService m_MetricsService;
    private readonly GatewayClient m_Client;
    private readonly ILogger<BotLatencyService> m_Logger;

    public BotLatencyService(IMetricsService metricsService, GatewayClient client, ILogger<BotLatencyService> logger)
    {
        m_Cts = new CancellationTokenSource();
        m_MetricsService = metricsService;
        m_Client = client;
        m_Logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!m_Cts.Token.IsCancellationRequested)
                {
                    m_MetricsService.TrackDiscordLatency(m_Client.Latency.TotalMilliseconds);
                    await Task.Delay(TimeSpan.FromSeconds(30), m_Cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                m_Logger.LogInformation("BotLatencyService background task cancelled.");
            }
        }, m_Cts.Token);
        m_Logger.LogInformation("Service started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_Cts.Cancel();
        m_Cts.Dispose();
        return Task.CompletedTask;
    }
}
