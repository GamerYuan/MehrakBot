#region

#endregion

#region

using Mehrak.Bot.Attributes;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Generated;
using Mehrak.Bot.Services.RateLimit;
using Mehrak.Domain.Common;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules.Common;

[RateLimit<ApplicationCommandContext>]
[HelpExampleFallback("profile", "2")]
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
        int profile = 1)
    {
        m_Logger.LogInformation("Executing Daily Check-In command for user {UserId} with profile {ProfileId}",
            Context.User.Id, profile);

        var executor = m_Builder
            .WithInteractionContext(Context)
            .ValidateServer(false)
            .WithEphemeralResponse(true)
            .WithCommandName(CommandName.Common.CheckIn)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    public static string GetHelpString()
    {
        return GeneratedHelpRegistry.GetHelpString("checkin");
    }
}
