#region

using MehrakCore.Services.Metrics;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules.Common;

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
            "genshin" => GenshinCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "profile" => ProfileCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "hsr" => HsrCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "checkin" => DailyCheckInCommandModule.GetHelpString(),
            _ => "Available commands: \n" +
                 "- `/profile [add|delete|list]`\n" +
                 "- `/checkin`\n" +
                 "- `/genshin [character]`\n" +
                 "- `/hsr [character]`\n" +
                 "Use `/help <command>` to get help about a specific command or subcommand.\n" +
                 "For example: `/help genshin` or `/help genshin character`"
        };

        BotMetrics.TrackCommand(Context.Interaction.User, "help", true);

        return new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents([
                new ComponentContainerProperties().AddComponents(new TextDisplayProperties(helpMessage))
            ]);
    }
}
