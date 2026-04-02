using NetCord.Services;

namespace Mehrak.Bot.Services.Abstractions;

public interface IBotService
{
    IBotContext? Context { get; set; }

    Task ExecuteAsync();
}

public interface IBotContext
{
    IInteractionContext DiscordContext { get; }
    TParam? GetParameter<TParam>(string key);
}

public class BotContext : IBotContext
{
    public IInteractionContext DiscordContext { get; }
    private Dictionary<string, object> Parameters { get; } = [];
    public BotContext(IInteractionContext discordContext, params IEnumerable<(string, string)> parameters)
    {
        DiscordContext = discordContext;
        foreach (var (key, value) in parameters) Parameters[key] = value;
    }

    public TParam? GetParameter<TParam>(string key)
    {
        return Parameters.TryGetValue(key, out var value) && value is TParam tValue ? tValue : default;
    }
}
