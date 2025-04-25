#region

using NetCord.Services.ApplicationCommands;

#endregion

namespace G_BuddyCore.Modules;

public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!")]
    public string Ping()
    {
        return "Pong";
    }
}
