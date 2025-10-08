#region

using NetCord;
using NetCord.Gateway;
using Prometheus;

#endregion

namespace Mehrak.Infrastructure.Metrics;

public static class BotMetrics
{
    private static readonly Counter CommandsTotal = Prometheus.Metrics.CreateCounter(
        "discord_commands_total",
        "Total number of commands executed"
    );

    private static readonly Counter CommandsByName = Prometheus.Metrics.CreateCounter(
        "discord_commands_by_name",
        "Commands executed by name",
        new CounterConfiguration { LabelNames = ["command_name"] }
    );

    private static readonly Counter CommandResults = Prometheus.Metrics.CreateCounter(
        "discord_command_results",
        "Results of command execution",
        new CounterConfiguration { LabelNames = ["command_name", "result"] }
    );

    private static readonly Counter CommandsByUser = Prometheus.Metrics.CreateCounter(
        "discord_commands_by_user",
        "Commands executed by user",
        new CounterConfiguration { LabelNames = ["user_id"] }
    );

    private static readonly Counter CharacterSelection = Prometheus.Metrics.CreateCounter(
        "discord_character_selections_total",
        "Total number of character selections by game",
        new CounterConfiguration { LabelNames = ["game", "character"] }
    );

    private static readonly Gauge MemoryUsage = Prometheus.Metrics.CreateGauge(
        "discord_memory_bytes",
        "Current memory usage of the bot"
    );

    private static readonly Gauge BotLatency = Prometheus.Metrics.CreateGauge(
        "discord_bot_latency_ms",
        "Current Discord gateway latency in milliseconds"
    );

    public static void Initialize(GatewayClient client)
    {
        Task.Run(() => UpdateMetrics(client));
    }

    public static void TrackCommand(User user, string command, bool success)
    {
        CommandsTotal.Inc();
        CommandsByName.WithLabels(command).Inc(0);
        CommandsByName.WithLabels(command).Inc();
        CommandResults.WithLabels(command, success ? "success" : "failure").Inc();
        CommandsByUser.WithLabels(user.Id.ToString()).Inc(0);
        CommandsByUser.WithLabels(user.Id.ToString()).Inc();
    }

    public static void TrackCharacterSelection(string game, string character)
    {
        CharacterSelection.WithLabels(game, character.ToLowerInvariant()).Inc();
    }

    public static void CreateCharacterSelection(string game, List<string> characters)
    {
        foreach (string character in characters)
            CharacterSelection.WithLabels(game, character.ToLowerInvariant()).Inc(0);
    }

    private static async Task UpdateMetrics(GatewayClient client, CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            BotLatency.Set(client.Latency.TotalMilliseconds);
            MemoryUsage.Set(GC.GetTotalMemory(false));
            await Task.Delay(TimeSpan.FromMinutes(1), token);
        }
    }
}
