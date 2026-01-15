#region

using NetCord;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules.Common;

public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!",
        DefaultGuildUserPermissions = Permissions.ManageGuild | Permissions.Administrator,
        Contexts = [InteractionContextType.Guild])]
    public string Ping()
    {
        return "Pong";
    }
}