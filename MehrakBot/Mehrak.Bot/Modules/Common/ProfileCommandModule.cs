#region

using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Context;
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
    private readonly ILogger<ProfileCommandModule> m_Logger;

    public ProfileCommandModule(UserDbContext userContext, ILogger<ProfileCommandModule> logger)
    {
        m_UserContext = userContext;
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
            Description = "The ID of the profile you want to delete. Leave blank if you wish to delete all profiles.")]
        uint profileId = 0)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

        if (profileId == 0)
        {
            await m_UserContext.Users.Where(x => x.Id == (long)Context.Interaction.Id).ExecuteDeleteAsync();

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties($"All profiles deleted!")));
            return;
        }

        var profiles = await m_UserContext.UserProfiles.Where(x => x.UserId == (long)Context.User.Id).ToListAsync();

        if (profiles.All(x => x.ProfileId != profileId))
        {
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties($"No profile with ID {profileId} found!")));
            return;
        }

        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            if (profiles[i].ProfileId == profileId)
            {
                profiles.RemoveAt(i);
                m_UserContext.UserProfiles.Remove(profiles[i]);
            }
            else if (profiles[i].ProfileId > profileId) profiles[i].ProfileId--;
        }

        try
        {
            m_UserContext.UserProfiles.UpdateRange(profiles);

            await m_UserContext.SaveChangesAsync();

            if (profiles.Count == 0)
            {
                await m_UserContext.Users.Where(x => x.Id == (long)Context.User.Id).ExecuteDeleteAsync();
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("All profiles deleted!")));
                return;
            }

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(
                        new TextDisplayProperties($"Profile {profileId} deleted!")));
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete profile {ProfileId} for user {UserId}", profileId, Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("Failed to delete profile! Please try again later")));
        }
    }

    [SubSlashCommand("list", "List your profiles")]
    public async Task ListProfileCommand()
    {
        var user = await m_UserContext.Users.AsNoTracking()
            .Where(x => x.Id == (long)Context.User.Id)
            .Select(u => new UserDto()
            {
                Id = (ulong)u.Id,
                Profiles = u.Profiles.Select(p => new UserProfileDto()
                {
                    ProfileId = p.ProfileId,
                    LtUid = (ulong)p.LtUid,
                    GameUids = p.GameUids
                        .GroupBy(g => g.Game)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToDictionary(
                                x => x.Region,
                                x => x.GameUid))
                }).ToList()
            }).FirstOrDefaultAsync();

        if (user?.Profiles == null || !user.Profiles.Any())
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("No profile found!"))));
            return;
        }

        var profileList = user.Profiles.Select(x => new TextDisplayProperties(
            $"## Profile {x.ProfileId}:\n**HoYoLAB UID:** {x.LtUid}\n### Games:\n" +
            $"{string.Join('\n', x.GameUids?.Select(y =>
                                     $"{y.Key}\n{string.Join(", ", y.Value.Select(z =>
                                         $"{z.Key}: {z.Value}"))}")
                                 ?? [])}"));
        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
            new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(profileList)));
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
            _ => "## Profile\n" +
                 "Manage your HoYoLAB profiles.\n" +
                 "### Usage\n" +
                 "```/profile [add|delete|list]```\n" +
                 "### Parameters\n" +
                 "[add]: Adds a new HoYoLAB profile to your account.\n" +
                 "[delete]: Deletes a HoYoLAB profile from your account.\n" +
                 "[list]: Lists all your HoYoLAB profiles."
        };
    }
}
