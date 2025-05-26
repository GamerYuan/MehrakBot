#region

using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

public class HelpCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("help", "Get help about the bot",
        Contexts =
        [
            InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
        ])]
    public InteractionMessageProperties HelpCommand(string commandName = "")
    {
        var commands = commandName.ToLowerInvariant().Split(' ');
        var helpMessage = commands[0].TrimStart('/') switch
        {
            "character" => CharacterCommandModule.GetHelpString(),
            "profile" => ProfileCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "checkin" => DailyCheckInCommandModule.GetHelpString(),
            _ => "Available commands: " +
                 "- `/profile [add|delete|list]`" +
                 "- `/character`" +
                 "- `/checkin`.\n" +
                 "Use `/help <command>` to get help about a specific command or subcommand.\n" +
                 "For example: `/help character`"
        };

        return new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents([
                new ComponentContainerProperties().AddComponents(new TextDisplayProperties(helpMessage))
            ]);
    }
}
