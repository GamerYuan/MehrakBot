using Mehrak.Application.Services.Abstractions;

namespace Mehrak.Application.Services.Common;

// TODO: To replace with proper impl after migration to ClickHouse
public class ApplicationMetricsService : IApplicationMetrics
{
    public IDisposable ObserveCommandDuration(string commandName)
    {
        return new MemoryStream();
    }

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
        // no-op
    }

    public void TrackCharacterSelection(string game, string character)
    {
        // no-op
    }

    public void TrackCommand(string commandName, ulong userId, bool isSuccess)
    {
        // no-op
    }

    public void TrackDiscordLatency(double latencyMs)
    {
        // no-op
    }
}
