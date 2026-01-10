namespace Mehrak.Domain.Services.Abstractions;

public interface IMetricsService
{
    void TrackCommand(string commandName, ulong userId, bool isSuccess);

    void TrackCharacterSelection(string game, string character);

    void TrackDiscordLatency(double latencyMs);

    IDisposable ObserveCommandDuration(string commandName);

    void RecordCommandDuration(string commandName, TimeSpan duration);
}
