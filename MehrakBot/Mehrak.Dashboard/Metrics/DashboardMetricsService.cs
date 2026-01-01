using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Options;
using Prometheus;

namespace Mehrak.Dashboard.Metrics;

public class DashboardMetricsService : IMetricsService, IHostedService
{
    private readonly Counter m_CommandsTotal = Prometheus.Metrics.CreateCounter(
        "dashboard_commands_total",
        "Total number of commands executed"
    );

    private readonly Counter m_CommandsByName = Prometheus.Metrics.CreateCounter(
        "dashboard_commands_by_name",
        "Commands executed by name",
        new CounterConfiguration { LabelNames = ["command_name"] }
    );

    private readonly Counter m_CommandResults = Prometheus.Metrics.CreateCounter(
        "dashboard_command_results",
        "Results of command execution",
        new CounterConfiguration { LabelNames = ["command_name", "result"] }
    );

    private readonly Histogram m_CommandExecutionTime = Prometheus.Metrics.CreateHistogram(
        "dashboard_command_exec_time",
        "Execution time of commands",
        new HistogramConfiguration { LabelNames = ["command_name"] }
    );

    private readonly Counter m_CommandsByUser = Prometheus.Metrics.CreateCounter(
        "dashboard_commands_by_user",
        "Commands executed by user",
        new CounterConfiguration { LabelNames = ["user_id"] }
    );

    private readonly Counter m_CharacterSelection = Prometheus.Metrics.CreateCounter(
        "dashboard_character_selections_total",
        "Total number of character selections by game",
        new CounterConfiguration { LabelNames = ["game", "character"] }
    );

    private readonly IOptions<MetricsConfig> m_Options;
    private IHost? m_MetricsServer;

    public DashboardMetricsService(IOptions<MetricsConfig> options)
    {
        m_Options = options;
    }


    public IDisposable ObserveCommandDuration(string commandName)
    {
        return m_CommandExecutionTime.WithLabels(commandName).NewTimer();
    }

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
        m_CommandExecutionTime.WithLabels(commandName).Observe(duration.TotalMilliseconds);
    }

    public void TrackCharacterSelection(string game, string character)
    {
        m_CharacterSelection.WithLabels(game, character.ToLowerInvariant()).Inc();
    }

    public void TrackCommand(string commandName, ulong userId, bool isSuccess)
    {
        m_CommandsTotal.Inc();
        m_CommandsByName.WithLabels(commandName).Inc();
        m_CommandResults.WithLabels(commandName, isSuccess ? "success" : "failure").Inc();
        m_CommandsByUser.WithLabels(userId.ToString()).Inc();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var metricsConfig = m_Options.Value;
        if (!metricsConfig.Enabled)
        {
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
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (m_MetricsServer != null)
        {
            await m_MetricsServer.StopAsync(cancellationToken);
            m_MetricsServer.Dispose();
        }
    }


    public void TrackDiscordLatency(double latencyMs)
    {
        throw new NotSupportedException();
    }
}
