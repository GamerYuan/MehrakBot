#region

using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!")]
    public string Ping()
    {
        return "Pong";
    }
}
