#region

using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Services.Genshin;

public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!")]
    public string Ping()
    {
        return "Pong";
    }
}
