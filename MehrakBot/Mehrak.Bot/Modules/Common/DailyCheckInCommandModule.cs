#region

#endregion

using Mehrak.Application.Models.Context;
using Mehrak.Bot.Builders;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Modules.Common;

public class DailyCheckInCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorBuilder m_Builder;
    private readonly ILogger<DailyCheckInCommandModule> m_Logger;

    public DailyCheckInCommandModule(
        ICommandExecutorBuilder builder,
        ILogger<DailyCheckInCommandModule> logger)
    {
        m_Builder = builder;
        m_Logger = logger;
    }

    [SlashCommand("checkin", "Perform HoYoLAB Daily Check-In")]
    public async Task DailyCheckInCommand(
        [SlashCommandParameter(Name = "profile", Description = "Profile ID (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation("Executing Daily Check-In command for user {UserId} with profile {ProfileId}",
            Context.User.Id, profile);

        var executor = m_Builder.For<CheckInApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id))
            .WithEphemeralResponse(true)
            .WithCommandName("checkin")
            .Build();

        await executor.ExecuteAsync(Domain.Enums.Game.Unsupported, null, profile).ConfigureAwait(false);
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
