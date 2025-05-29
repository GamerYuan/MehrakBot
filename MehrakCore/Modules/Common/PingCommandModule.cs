#region

using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules.Common;

public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!")]
    public string Ping()
    {
        return "Pong";
    }
}