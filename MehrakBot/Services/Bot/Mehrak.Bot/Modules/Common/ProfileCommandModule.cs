#region

using System.Text;
using Mehrak.Bot.Services;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules.Common;

[SlashCommand("profile", "Manage your profile",
    Contexts = [InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel])]
public class ProfileCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserDbContext m_UserContext;
    private readonly UserCountTrackerService m_UserTracker;
    private readonly ILogger<ProfileCommandModule> m_Logger;

    public ProfileCommandModule(UserDbContext userContext, UserCountTrackerService userTracker, ILogger<ProfileCommandModule> logger)
    {
        m_UserContext = userContext;
        m_UserTracker = userTracker;
        m_Logger = logger;
    }

    [SubSlashCommand("add", "Add a profile")]
    public async Task AddProfileCommand()
    {
        m_Logger.LogInformation("User {UserId} is adding HoYoLAB profile", Context.User.Id);

        if (await m_UserContext.UserProfiles.Where(x => x.UserId == (long)Context.User.Id).CountAsync() >= 10)
        {
            m_Logger.LogInformation("User {UserId} has reached the maximum number of profiles", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("You can only have 10 profiles!"))));
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.Modal(AuthModalModule.AddAuthModal));
    }

    [SubSlashCommand("delete", "Delete a profile")]
    public async Task DeleteProfileCommand(
        [SlashCommandParameter(Name = "profile",
            Description = "The Profile ID/HoYoLAB UID of the profile to delete. Leave blank to delete all profiles.")]
        ulong profileId = 0)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

        var profiles = await m_UserContext.UserProfiles.Where(x => x.UserId == (long)Context.User.Id).ToListAsync();

        if (profiles.Count == 0)
        {
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("No profiles found!")));
            return;
        }

        if (profileId == 0)
        {
            try
            {
                var deleted = await m_UserContext.Users.Where(x => x.Id == (long)Context.User.Id).ExecuteDeleteAsync();

                if (deleted > 0) await m_UserTracker.AdjustUserCountAsync(-1);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties($"All profiles deleted!")));
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to delete all profiles for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to delete profiles! Please try again later")));
            }
            return;
        }

        var profile = FindProfileForDelete(profiles, profileId);
        if (profile == null)
        {
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties($"No profile with ID or UID {profileId} found!")));
            return;
        }

        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            if (profiles[i].ProfileId == profile.ProfileId)
            {
                m_UserContext.UserProfiles.Remove(profiles[i]);
                profiles.RemoveAt(i);
            }
            else if (profiles[i].ProfileId > profile.ProfileId) profiles[i].ProfileId--;
        }

        try
        {
            m_UserContext.UserProfiles.UpdateRange(profiles);

            await m_UserContext.SaveChangesAsync();

            if (profiles.Count == 0)
            {
                var deleted = await m_UserContext.Users.Where(x => x.Id == (long)Context.User.Id).ExecuteDeleteAsync();
                if (deleted > 0) await m_UserTracker.AdjustUserCountAsync(-1);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("All profiles deleted!")));
                return;
            }

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(
                        new TextDisplayProperties($"Profile {profile.ProfileId} deleted!")));
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete profile {ProfileId} for user {UserId}", profile.ProfileId, Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("Failed to delete profile! Please try again later")));
        }
    }

    private static UserProfileModel? FindProfileForDelete(IEnumerable<UserProfileModel> profiles, ulong lookupValue)
    {
        return profiles.FirstOrDefault(p => p.ProfileId == (int)lookupValue)
               ?? profiles.FirstOrDefault(p => p.LtUid == (long)lookupValue);
    }

    [SubSlashCommand("list", "List your profiles")]
    public async Task ListProfileCommand()
    {
        var user = await m_UserContext.Users
                .AsNoTracking()
                .Include(u => u.Profiles)
                    .ThenInclude(p => p.GameUids)
                .Include(u => u.Profiles)
                    .ThenInclude(p => p.LastUsedRegions)
                .SingleOrDefaultAsync(u => u.Id == (long)Context.User.Id);


        if (user?.Profiles == null || user.Profiles.Count == 0)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("No profile found!"))));
            return;
        }

        ComponentContainerProperties container = [];

        var sb = new StringBuilder();
        for (var i = 0; i < user.Profiles.Count; i++)
        {
            var profile = user.Profiles[i];
            sb.Append($"## Profile {profile.ProfileId}\n**HoYoLAB UID:** {profile.LtUid}\n### Games: \n");
            foreach (var gameUid in profile.GameUids.GroupBy(x => x.Game, (key, g) => new { Key = key, Grouping = g }))
            {
                sb.AppendLine($"**{gameUid.Key.ToFriendlyString()}**");
                foreach (var g in gameUid.Grouping)
                {
                    sb.AppendLine($"- {g.Region}: {g.GameUid}");
                }
            }
            container.AddComponents([new TextDisplayProperties(sb.ToString())]);
            sb.Clear();

            if (i + 1 < user.Profiles.Count) container.AddComponents([new ComponentSeparatorProperties()]);
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
            new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents([container])));
    }

    [SubSlashCommand("update", "Update the HoYoLAB Cookies and Passphrase for a selected profile")]
    public async Task UpdateProfile([SlashCommandParameter(Name = "profile", Description = "The Profile ID/HoYoLAB UID of the profile to update")] ulong profileId)
    {
        var user = await m_UserContext.Users
            .AsNoTracking()
            .Where(u => u.Id == (long)Context.User.Id)
            .Include(u => u.Profiles)
                .ThenInclude(p => p.GameUids)
            .FirstOrDefaultAsync();

        var profiles = user?.Profiles
            .OrderBy(p => p.ProfileId)
            .Select(p => new UserProfileDto
            {
                Id = p.Id,
                ProfileId = p.ProfileId,
                LtUid = (ulong)p.LtUid,
                GameUids = p.GameUids
                    .GroupBy(g => g.Game)
                    .ToDictionary(
                        group => group.Key,
                        group => group.ToDictionary(entry => entry.Region, entry => entry.GameUid))
            })
            .ToList();

        var profile = FindProfile(profiles, profileId);

        if (profile == null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties($"No profile with ID {profileId} found!"))));
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.Modal(AuthModalModule.UpdateAuthModal(profile)));
    }

    private static UserProfileDto? FindProfile(IEnumerable<UserProfileDto>? profiles, ulong lookupValue)
    {
        var orderedProfiles = profiles?.OrderBy(p => p.ProfileId).ToList();
        if (orderedProfiles == null || orderedProfiles.Count == 0)
            return null;

        var lookupText = lookupValue.ToString();

        return orderedProfiles.FirstOrDefault(p => p.ProfileId == (int)lookupValue)
               ?? orderedProfiles.FirstOrDefault(p => p.LtUid == lookupValue);
    }

    public static string GetHelpString(string subcommand)
    {
        return subcommand switch
        {
            "add" => "## Profile Add\n" +
                     "Adds a new HoYoLAB profile to your account.\n" +
                     "You can add up to 10 profiles.\n" +
                     "### Usage\n" +
                     "```/profile add```" +
                     "You will be prompted with a authentication modal to provide your HoYoLAB details\n" +
                     "### Parameters\n" +
                     "HoYoLAB UID: Your HoYoLAB UID\n" +
                     "HoYoLAB Cookies: Your HoYoLAB Cookies. Retrieve only the `ltoken_v2` value from the cookies that starts with `v2_...`\n" +
                     "-# [Ctrl] + [Shift] + [I] to open the developer tools in your browser. For Chromium Browser, go to Application Tab; " +
                     "For Firefox, go to Storage Tab. You may find the `ltoken_v2` cookie entry there\n",
            "delete" => "## Profile Delete\n" +
                        "Deletes a HoYoLAB profile from your account.\n" +
                        "### Usage\n" +
                        "```/profile delete [profile]```\n" +
                        "### Parameters\n" +
                        "[profile]: The ID of the profile you want to delete. Leave blank if you wish to delete all profiles.\n" +
                        "### Examples\n" +
                        "```/profile delete\n/profile delete 1```",
            "list" => "## Profile List\n" +
                      "Lists all your HoYoLAB profiles.\n" +
                      "### Usage\n" +
                      "```/profile list```",
            "update" => "## Profile Update\n" +
                        "Update the HoYoLAB Cookies and Passphrase for a selected HoYoLAB profile.\n" +
                        "### Usage\n" +
                        "```/profile update 1```",
            _ => "## Profile\n" +
                 "Manage your HoYoLAB profiles.\n" +
                 "### Usage\n" +
                 "```/profile [add|delete|list|update]```\n" +
                 "### Parameters\n" +
                 "[add]: Adds a new HoYoLAB profile to your account.\n" +
                 "[delete]: Deletes a HoYoLAB profile from your account.\n" +
                 "[list]: Lists all your HoYoLAB profiles.\n" +
                 "[update]: Update the HoYoLAB Cookies and Passphrase for a selected HoYoLAB profile."
        };
    }
}
