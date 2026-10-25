#region

using Mehrak.Bot.Config;
using Mehrak.Bot.Generated;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Attributes;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Shared.Modules;

public class HelpCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandUiLocalizer m_Localizer;

    public HelpCommandModule(ICommandUiLocalizer localizer)
    {
        m_Localizer = localizer;
    }

    [SlashCommand("help", "Get help about the bot",
        Contexts =
        [
            InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
        ])]
    public InteractionMessageProperties HelpCommand(
        [SlashCommandParameter(Name = "command_name", Description = "The command to get help for")]
        [HelpExample("genshin", "genshin character")]
        string commandName = "")
    {
        var commands = commandName.ToLowerInvariant().Split(' ');
        var culture = BotUiCulture.Resolve(Context);
        var helpMessage = HelpTextRenderer.Render(
            GeneratedHelpRegistry.Commands,
            commands.Length > 0 ? commands[0] : "",
            commands.Length > 1 ? commands[1] : "",
            m_Localizer,
            culture);

        return new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents(
                new ComponentContainerProperties().AddComponents(
                    new TextDisplayProperties(helpMessage),
                    new ComponentSeparatorProperties(),
                    new TextDisplayProperties(HelpTextRenderer.RenderFooter(AppInfo.Version, m_Localizer, culture))
                )
            );
    }
}
