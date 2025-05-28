#region

using MehrakCore.Modules;
using MehrakCore.Services.Commands;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules.Common;

public class DailyCheckInCommandModule : ApplicationCommandModule<ApplicationCommandContext>, ICommandModule
{
    private readonly IDailyCheckInCommandService<DailyCheckInCommandModule> m_Executor;
    private readonly ILogger<DailyCheckInCommandModule> m_Logger;

    public DailyCheckInCommandModule(
        IDailyCheckInCommandService<DailyCheckInCommandModule> executor,
        ILogger<DailyCheckInCommandModule> logger)
    {
        m_Executor = executor;
        m_Logger = logger;
    }

    [SlashCommand("checkin", "Perform HoYoLAB Daily Check-In")]
    public async Task DailyCheckInCommand(
        [SlashCommandParameter(Name = "profile", Description = "Profile ID (Defaults to 1)")]
        uint profile = 1)
    {
        try
        {
            // Set the context for the executor
            m_Executor.Context = Context;

            // Delegate to the executor with the profile parameter
            await m_Executor.ExecuteAsync(profile);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error in daily check-in command module for user {UserId}", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties()
                    .WithContent("An error occurred while processing your request. Please try again later.")
                    .WithFlags(MessageFlags.Ephemeral)));
        }
    }

    public static string GetHelpString()
    {
        return "## Daily Check-In\n" +
               "Perform HoYoLAB Daily Check-In to collect daily rewards for Genshin Impact\n" +
               "### Usage\n" +
               "```/checkin [profile]```\n" +
               "### Parameters\n" +
               "- `profile`: Profile ID (Defaults to 1) [Optional]\n" +
               "### Examples\n" +
               "```/checkin\n/checkin 2```\n";
    }
}
