#region

using Mehrak.Infrastructure.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using Prometheus;

#endregion

namespace Mehrak.Infrastructure.Metrics;

public class MetricsService : BackgroundService
{
    private readonly GatewayClient m_Client;
    private readonly IOptions<MetricsConfig> m_MetricsOptions;
    private readonly ILogger<MetricsService> m_Logger;

    public MetricsService(GatewayClient client, IOptions<MetricsConfig> metricsOptions, ILogger<MetricsService> logger)
    {
        m_Client = client;
        m_MetricsOptions = metricsOptions;
        m_Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MetricsConfig metricsConfig = m_MetricsOptions.Value;
        if (!metricsConfig.Enabled)
        {
            m_Logger.LogInformation("Metrics service disabled");
            return;
        }

        var metricServer = new WebHostBuilder().UseKestrel().UseUrls($"http://{metricsConfig.Host}:{metricsConfig.Port}")
            .Configure(app =>
            {
                app.UseMetricServer(metricsConfig.Endpoint);
                app.UseHttpMetrics();

                app.Map("/health", builder => builder.Run(async context =>
                {
                    context.Response.StatusCode = 200;
                    await context.Response.WriteAsync("Healthy", stoppingToken);
                }));
            }).Build();

        await metricServer.StartAsync(stoppingToken).ConfigureAwait(false);
        m_Logger.LogInformation("Metrics service started on {Host}:{Port} with endpoint {Endpoint}",
            metricsConfig.Host, metricsConfig.Port, metricsConfig.Endpoint);

        BotMetrics.Initialize(m_Client);
        m_Logger.LogInformation("Bot metrics initialized");
    }
}
