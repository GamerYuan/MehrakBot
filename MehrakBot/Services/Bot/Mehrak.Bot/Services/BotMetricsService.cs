#region

using System.Diagnostics.Metrics;
using Mehrak.Bot.Services.Abstractions;

#endregion

namespace Mehrak.Bot.Services;

public sealed class BotMetricsService : IBotMetrics, IDisposable
{
    private readonly Meter m_Meter;

    private readonly Counter<long> m_CommandsTotal;
    private readonly Counter<long> m_CommandResults;
    private readonly Histogram<double> m_CommandExecutionTime;
    private readonly Counter<long> m_CommandsByUser;

    private double m_CurrentLatency;

    public BotMetricsService()
    {
        // TODO: Add env var for bot version
        m_Meter = new Meter("MehrakBot", "1.0.0");

        m_CommandsTotal = m_Meter.CreateCounter<long>(
            "bot_command_total",
            description: "Total number of commands executed"
        );

        m_CommandResults = m_Meter.CreateCounter<long>(
            "bot_command_results",
            description: "Results of command execution"
        );

        m_CommandExecutionTime = m_Meter.CreateHistogram<double>(
            "bot_command_exec_time",
            unit: "ms",
            description: "Execution time of commands"
        );

        m_CommandsByUser = m_Meter.CreateCounter<long>(
            "bot_command_by_user",
            description: "Commands executed by user"
        );

        m_Meter.CreateObservableGauge(
            "bot_latency_ms",
            () => m_CurrentLatency,
            unit: "ms",
            description: "Current Discord gateway latency in milliseconds"
        );
    }

    public void TrackCommand(string commandName, ulong userId, bool isSuccess)
    {
        m_CommandsTotal.Add(1);
        m_CommandResults.Add(1,
            new KeyValuePair<string, object?>("command_name", commandName),
            new KeyValuePair<string, object?>("result", isSuccess ? "success" : "failure"));
        m_CommandsByUser.Add(1, new KeyValuePair<string, object?>("user_id", userId));
    }

    public void TrackDiscordLatency(double latencyMs)
    {
        m_CurrentLatency = latencyMs;
    }

    public IDisposable ObserveCommandDuration(string commandName)
    {
        return new CommandTimer(this, commandName);
    }

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
        m_CommandExecutionTime.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("command_name", commandName));
    }

    public void Dispose()
    {
        m_Meter.Dispose();
    }

    private sealed class CommandTimer : IDisposable
    {
        private readonly BotMetricsService m_Service;
        private readonly string m_CommandName;
        private readonly long m_StartTime;

        public CommandTimer(BotMetricsService service, string commandName)
        {
            m_Service = service;
            m_CommandName = commandName;
            m_StartTime = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(m_StartTime);
            m_Service.RecordCommandDuration(m_CommandName, elapsed);
        }
    }
}
