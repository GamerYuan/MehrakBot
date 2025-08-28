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
        string[] commands = commandName.ToLowerInvariant().Split(' ');
        string helpMessage = commands[0].TrimStart('/') switch
        {
            "genshin" => GenshinCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "profile" => ProfileCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "hsr" => HsrCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "zzz" => ZzzCommandModule.GetHelpString(commands.Length > 1 ? commands[1] : ""),
            "health" => HealthCommandModule.GetHelpString(),
            "checkin" => DailyCheckInCommandModule.GetHelpString(),
            _ => "Available commands: \n" +
                 "- `/profile [add|delete|list]`\n" +
                 "- `/checkin`\n" +
                 "- `/genshin [abyss|character|charlist|codes|notes|stygian|theater]`\n" +
                 "- `/hsr [as|character|charlist|codes|moc|notes|pf]`\n" +
                 "- `/zzz [character|codes]`\n" +
                 "Use `/help <command>` to get help about a specific command or subcommand.\n" +
                 "For example: `/help genshin` or `/help genshin character`"
        };

        BotMetrics.TrackCommand(Context.Interaction.User, "help", true);

        return new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents([
                new ComponentContainerProperties().AddComponents(new TextDisplayProperties(helpMessage))
                    .AddComponents(new TextDisplayProperties(
                        "-# Check out the bot's documentation at https://gameryuan.gitbook.io/mehrak for more information!"))
            ]);
    }
}
