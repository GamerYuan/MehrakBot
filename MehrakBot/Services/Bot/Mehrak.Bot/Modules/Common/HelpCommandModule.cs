#region

#endregion

#region

using Mehrak.Bot.Config;
using Mehrak.Bot.Generated;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules.Common;

public class HelpCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("help", "Get help about the bot",
        Contexts =
        [
            InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
        ])]
    public static InteractionMessageProperties HelpCommand(string commandName = "")
    {
        var commands = commandName.ToLowerInvariant().Split(' ');
        var helpMessage = GeneratedHelpRegistry.GetHelpString(
            commands.Length > 0 ? commands[0] : "",
            commands.Length > 1 ? commands[1] : "");

        return new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents(
                new ComponentContainerProperties().AddComponents(
                    new TextDisplayProperties(helpMessage),
                    new ComponentSeparatorProperties(),
                    new TextDisplayProperties(
                        $"-# v{AppInfo.Version}  |  [Click](https://mehrak.yuan-dev.com/docs) for documentation"))
            );
    }
}
