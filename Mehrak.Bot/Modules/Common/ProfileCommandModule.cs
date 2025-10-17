#region

using Mehrak.Domain.Repositories;
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
    private readonly IUserRepository m_UserRepository;
    private readonly ILogger<ProfileCommandModule> m_Logger;

    public ProfileCommandModule(IUserRepository userRepository, ILogger<ProfileCommandModule> logger)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
    }

    [SubSlashCommand("add", "Add a profile")]
    public async Task AddProfileCommand()
    {
        m_Logger.LogInformation("User {UserId} is adding HoYoLAB profile", Context.User.Id);
        var user = await m_UserRepository.GetUserAsync(Context.User.Id);

        if (user?.Profiles != null && user.Profiles.Count() >= 10)
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
        var user = await m_UserRepository.GetUserAsync(Context.User.Id);
        if (user?.Profiles == null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("No profile found!"))));
            return;
        }

        if (profileId == 0)
        {
            await m_UserRepository.DeleteUserAsync(Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("All profiles deleted!"))));
            return;
        }

        var profiles = user.Profiles.ToList();

        if (profiles.All(x => x.ProfileId != profileId))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties($"No profile with ID {profileId} found!"))));
            return;
        }

        for (int i = profiles.Count - 1; i >= 0; i--)
            if (profiles[i].ProfileId == profileId)
                profiles.RemoveAt(i);
            else if (profiles[i].ProfileId > profileId) profiles[i].ProfileId--;

        user.Profiles = profiles;
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
            new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(
                    new TextDisplayProperties($"Profile {profileId} deleted!"))));
    }

    [SubSlashCommand("list", "List your profiles")]
    public async Task ListProfileCommand()
    {
        var user = await m_UserRepository.GetUserAsync(Context.User.Id);
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
