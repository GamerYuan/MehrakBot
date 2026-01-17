#region

using Mehrak.Bot.Services.Abstractions;
using Mehrak.Infrastructure.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;

#endregion

namespace Mehrak.Infrastructure.Metrics;

public class BotMetricsService : IBotMetrics, IHostedService
{
    private readonly Counter m_CommandsTotal = Prometheus.Metrics.CreateCounter(
        "discord_commands_total",
        "Total number of commands executed"
    );

    private readonly Counter m_CommandsByName = Prometheus.Metrics.CreateCounter(
        "discord_commands_by_name",
        "Commands executed by name",
        new CounterConfiguration { LabelNames = ["command_name"] }
    );

    private readonly Counter m_CommandResults = Prometheus.Metrics.CreateCounter(
        "discord_command_results",
        "Results of command execution",
        new CounterConfiguration { LabelNames = ["command_name", "result"] }
    );

    private readonly Histogram m_CommandExecutionTime = Prometheus.Metrics.CreateHistogram(
        "discord_command_exec_time",
        "Execution time of commands",
        new HistogramConfiguration { LabelNames = ["command_name"] }
    );

    private readonly Counter m_CommandsByUser = Prometheus.Metrics.CreateCounter(
        "discord_commands_by_user",
        "Commands executed by user",
        new CounterConfiguration { LabelNames = ["user_id"] }
    );

    private readonly Counter m_CharacterSelection = Prometheus.Metrics.CreateCounter(
        "discord_character_selections_total",
        "Total number of character selections by game",
        new CounterConfiguration { LabelNames = ["game", "character"] }
    );

    private readonly Gauge m_MemoryUsage = Prometheus.Metrics.CreateGauge(
        "discord_memory_bytes",
        "Current memory usage of the bot"
    );

    private readonly Gauge m_BotLatency = Prometheus.Metrics.CreateGauge(
        "discord_bot_latency_ms",
        "Current Discord gateway latency in milliseconds"
    );

    private readonly IOptions<MetricsConfig> m_MetricsOptions;
    private readonly ILogger<BotMetricsService> m_Logger;
    private IHost? m_MetricsServer;
    private readonly CancellationTokenSource m_Cts = new();

    public BotMetricsService(IOptions<MetricsConfig> metricsOptions, ILogger<BotMetricsService> logger)
    {
        m_MetricsOptions = metricsOptions;
        m_Logger = logger;
    }

    public void TrackCommand(string commandName, ulong userId, bool isSuccess)
    {
        m_CommandsTotal.Inc();
        m_CommandsByName.WithLabels(commandName).Inc();
        m_CommandResults.WithLabels(commandName, isSuccess ? "success" : "failure").Inc();
        m_CommandsByUser.WithLabels(userId.ToString()).Inc();
    }

    public void TrackCharacterSelection(string game, string character)
    {
        m_CharacterSelection.WithLabels(game, character.ToLowerInvariant()).Inc();
    }

    public void TrackDiscordLatency(double latencyMs)
    {
        m_BotLatency.Set(latencyMs);
    }

    public IDisposable ObserveCommandDuration(string commandName)
    {
        return m_CommandExecutionTime.WithLabels(commandName).NewTimer();
    }

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
        m_CommandExecutionTime.WithLabels(commandName).Observe(duration.TotalMilliseconds);
    }

    private async Task TrackGCMemoryTask(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            m_MemoryUsage.Set(GC.GetTotalMemory(false));
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var metricsConfig = m_MetricsOptions.Value;
        if (!metricsConfig.Enabled)
        {
            m_Logger.LogInformation("Metrics service disabled");
            return;
        }

        m_MetricsServer = Host.CreateDefaultBuilder()
            .ConfigureWebHost(x => x.UseKestrel().UseUrls($"http://{metricsConfig.Host}:{metricsConfig.Port}")
                .Configure(app =>
                {
                    app.UseMetricServer(metricsConfig.Endpoint);
                    app.Map("/health", builder => builder.Run(async context =>
                    {
                        context.Response.StatusCode = 200;
                        await context.Response.WriteAsync("Healthy", context.RequestAborted);
                    }));
                })).Build();

        await m_MetricsServer.StartAsync(cancellationToken).ConfigureAwait(false);

        _ = Task.Run(() => TrackGCMemoryTask(m_Cts.Token), CancellationToken.None);

        m_Logger.LogInformation("Metrics service started on {Host}:{Port} with endpoint {Endpoint}",
            metricsConfig.Host, metricsConfig.Port, metricsConfig.Endpoint);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await m_Cts.CancelAsync();
        m_Cts.Dispose();
        if (m_MetricsServer != null)
        {
            await m_MetricsServer.StopAsync(cancellationToken);
            m_MetricsServer.Dispose();
        }
    }
}
