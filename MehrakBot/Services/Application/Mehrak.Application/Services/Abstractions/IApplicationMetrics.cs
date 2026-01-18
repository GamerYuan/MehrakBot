namespace Mehrak.Application.Services.Abstractions;

public interface IApplicationMetrics
{
    void TrackCharacterSelection(string game, string character);

    IDisposable ObserveCommandDuration(string commandName);

    void RecordCommandDuration(string commandName, TimeSpan duration);

    IDisposable ObserveCardGenerationDuration(string cardType);

    void RecordCardGenerationDuration(string cardType, TimeSpan duration);
}

