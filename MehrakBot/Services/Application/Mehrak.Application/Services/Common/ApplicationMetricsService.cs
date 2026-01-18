using System.Diagnostics.Metrics;
using Mehrak.Application.Services.Abstractions;

namespace Mehrak.Application.Services.Common;

public sealed class ApplicationMetricsService : IApplicationMetrics, IDisposable
{
    private readonly Meter m_Meter;
    private readonly Histogram<double> m_CommandExecutionTime;
    private readonly Histogram<double> m_CardGenerationTime;
    private readonly Counter<long> m_CharacterSelections;

    public ApplicationMetricsService()
    {
        m_Meter = new Meter("MehrakApplication", "1.0.0");

        m_CommandExecutionTime = m_Meter.CreateHistogram<double>(
            "application_command_exec_time",
            unit: "ms",
            description: "Execution time of application commands"
        );

        m_CardGenerationTime = m_Meter.CreateHistogram<double>(
            "application_card_generation_time",
            unit: "ms",
            description: "Execution time of card generation"
        );

        m_CharacterSelections = m_Meter.CreateCounter<long>(
            "application_character_selections_total",
            description: "Total number of character selections by game"
        );
    }

    public IDisposable ObserveCommandDuration(string commandName)
    {
        return new Timer(this, commandName, (s, n, d) => s.RecordCommandDuration(n, d));
    }

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
        m_CommandExecutionTime.Record(duration.TotalMilliseconds,
             new KeyValuePair<string, object?>("command_name", commandName));
    }

    public IDisposable ObserveCardGenerationDuration(string cardType)
    {
        return new Timer(this, cardType, (s, n, d) => s.RecordCardGenerationDuration(n, d));
    }

    public void RecordCardGenerationDuration(string cardType, TimeSpan duration)
    {
        m_CardGenerationTime.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("card_type", cardType));
    }

    public void TrackCharacterSelection(string game, string character)
    {
        m_CharacterSelections.Add(1,
            new KeyValuePair<string, object?>("game", game),
            new KeyValuePair<string, object?>("character", character.ToLowerInvariant()));
    }

    public void Dispose()
    {
        m_Meter.Dispose();
    }

    private sealed class Timer : IDisposable
    {
        private readonly ApplicationMetricsService m_Service;
        private readonly string m_Name;
        private readonly Action<ApplicationMetricsService, string, TimeSpan> m_Callback;
        private readonly long m_StartTime;

        public Timer(ApplicationMetricsService service, string name, Action<ApplicationMetricsService, string, TimeSpan> callback)
        {
            m_Service = service;
            m_Name = name;
            m_Callback = callback;
            m_StartTime = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(m_StartTime);
            m_Callback(m_Service, m_Name, elapsed);
        }
    }
}
