#region

#endregion

#region

using Mehrak.Application.Models.Context;
using Mehrak.Bot.Services;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules.Common;

public class DailyCheckInCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorService<CheckInApplicationContext> m_Executor;
    private readonly ILogger<DailyCheckInCommandModule> m_Logger;

    public DailyCheckInCommandModule(
        ICommandExecutorService<CheckInApplicationContext> executor,
        ILogger<DailyCheckInCommandModule> logger)
    {
        m_Executor = executor;
        m_Logger = logger;
    }

    [SlashCommand("checkin", "Perform HoYoLAB Daily Check-In")]
    public async Task DailyCheckInCommand(
        [SlashCommandParameter(Name = "profile", Description = "Profile ID (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation("Executing Daily Check-In command for user {UserId} with profile {ProfileId}",
            Context.User.Id, profile);

        m_Executor.ApplicationContext = new CheckInApplicationContext(Context.User.Id);
        m_Executor.Context = Context;

        await m_Executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    public static string GetHelpString()
    {
        return "## Daily Check-In\n" +
               "Perform HoYoLAB Daily Check-In to collect daily rewards for multiple HoYoverse games\n" +
               "Supports: Genshin Impact, Honkai: Star Rail, Zenless Zone Zero, and Honkai Impact 3rd\n" +
               "### Usage\n" +
               "```/checkin [profile]```\n" +
               "### Parameters\n" +
               "- `profile`: Profile ID (Defaults to 1) [Optional]\n" +
               "### Examples\n" +
               "```/checkin\n/checkin 2```\n";
    }
}
