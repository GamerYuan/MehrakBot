#region

using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Attributes;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Services;
using NetCord;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Shared.Modules;

[HelpIgnore]
public class PingCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandUiLocalizer m_Localizer;

    public PingCommandModule(ICommandUiLocalizer? localizer = null)
    {
        m_Localizer = localizer ?? CommandUiLocalizer.Default;
    }

    [SlashCommand("ping", "Ping!",
        DefaultGuildPermissions = Permissions.ManageGuild | Permissions.Administrator,
        Contexts = [InteractionContextType.Guild])]
    public string Ping()
    {
        return m_Localizer.Get(BotUiCulture.Resolve(Context), CommandUiKeys.PingPong);
    }
}
