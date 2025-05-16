#region

using NetCord;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!", DefaultGuildUserPermissions = Permissions.ManageGuild)]
    public string Ping()
    {
        return "Pong";
    }
}
