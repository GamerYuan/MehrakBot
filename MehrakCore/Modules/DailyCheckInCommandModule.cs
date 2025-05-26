#region

using MehrakCore.Repositories;
using MehrakCore.Services;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

public class DailyCheckInCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly IDailyCheckInService m_Service;
    private readonly UserRepository m_UserRepository;
    private readonly CommandRateLimitService m_RateLimitService;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly ILogger<AuthModalModule> m_Logger;

    public DailyCheckInCommandModule(IDailyCheckInService service, UserRepository userRepository,
        CommandRateLimitService rateLimitService,
        TokenCacheService tokenCacheService, ILogger<AuthModalModule> logger)
    {
        m_Service = service;
        m_UserRepository = userRepository;
        m_RateLimitService = rateLimitService;
        m_TokenCacheService = tokenCacheService;
        m_Logger = logger;
    }

    [SlashCommand("checkin", "Perform HoYoLAB Daily Check-In")]
    public async Task DailyCheckInCommand(
        [SlashCommandParameter(Name = "profile", Description = "Profile ID (Defaults to 1)")]
        uint profile = 1)
    {
        try
        {
            m_Logger.LogInformation("User {UserId} used the character command", Context.User.Id);
            if (await m_RateLimitService.IsRateLimitedAsync(Context.User.Id))
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("Used command too frequent! Please try again later")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await m_RateLimitService.SetRateLimitAsync(Context.User.Id);
            var user = await m_UserRepository.GetUserAsync(Context.User.Id);
            if (user?.Profiles == null || user.Profiles.All(x => x.ProfileId != profile))
            {
                m_Logger.LogInformation("User {UserId} does not have a profile with ID {ProfileId}",
                    Context.User.Id, profile);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("You do not have a profile with this ID")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var selectedProfile = user.Profiles.First(x => x.ProfileId == profile);

            var ltoken = await m_TokenCacheService.GetCacheEntry(Context.User.Id, selectedProfile.LtUid);
            if (ltoken == null)
            {
                m_Logger.LogInformation("User {UserId} is not authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.Modal(AuthModalModule.AuthModal("check_in_auth_modal", profile)));
            }
            else
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await m_Service.CheckInAsync(Context, selectedProfile.LtUid, ltoken);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing character command for user {UserId}", Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
        }
    }
}
