#region

using Mehrak.Bot.Shared.Attributes;
using NetCord;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Shared.Modules;

[HelpIgnore]
public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Ping!",
        DefaultGuildPermissions = Permissions.ManageGuild | Permissions.Administrator,
        Contexts = [InteractionContextType.Guild])]
    public string Ping()
    {
        return "Pong";
    }
}
