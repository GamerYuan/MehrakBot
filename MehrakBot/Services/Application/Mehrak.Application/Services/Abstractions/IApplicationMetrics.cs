namespace Mehrak.Application.Services.Abstractions;

public interface IApplicationMetrics
{
    void TrackCommand(string commandName, ulong userId, bool isSuccess);

    void TrackCharacterSelection(string game, string character);

    void TrackDiscordLatency(double latencyMs);

    IDisposable ObserveCommandDuration(string commandName);

    void RecordCommandDuration(string commandName, TimeSpan duration);
}

