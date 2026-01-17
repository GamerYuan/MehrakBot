namespace Mehrak.Bot.Services.Abstractions;

public interface IBotMetrics
{
    void TrackCommand(string commandName, ulong userId, bool isSuccess);

    void TrackCharacterSelection(string game, string character);

    void TrackDiscordLatency(double latencyMs);

    IDisposable ObserveCommandDuration(string commandName);

    void RecordCommandDuration(string commandName, TimeSpan duration);
}
