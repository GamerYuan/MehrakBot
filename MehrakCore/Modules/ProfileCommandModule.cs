#region

using System.Security.Cryptography;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Modules;

[SlashCommand("profile", "Manage your profile",
    Contexts = [InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel])]
public class ProfileCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly ILogger<ProfileCommandModule> m_Logger;

    public ProfileCommandModule(UserRepository userRepository, ILogger<ProfileCommandModule> logger)
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

        await RespondAsync(InteractionCallback.Modal(AuthModalModule.AddAuthModal));
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
                                     $"{y.Key.ToString()}\n{string.Join(", ", y.Value.Select(z =>
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

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    public static ModalProperties AddAuthModal => new ModalProperties("add_auth_modal", "Authenticate")
        .WithComponents([
            new TextInputProperties("ltuid", TextInputStyle.Short, "HoYoLAB UID"),
            new TextInputProperties("ltoken", TextInputStyle.Paragraph, "HoYoLAB Cookies"),
            new TextInputProperties("passphrase", TextInputStyle.Paragraph, "Passphrase")
                .WithPlaceholder("Do not use the same password as your Discord or HoYoLAB account!").WithMaxLength(64)
        ]);

    public static ModalProperties AuthModal(string character, Regions server, uint profile)
    {
        return new ModalProperties($"character_auth_modal:{character}:{server}:{profile}", "Authenticate")
            .AddComponents([
                new TextInputProperties("passphrase", TextInputStyle.Paragraph, "Passphrase")
                    .WithPlaceholder("Your Passphrase").WithMaxLength(64)
            ]);
    }

    private readonly UserRepository m_UserRepository;
    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly CookieService m_CookieService;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterCommandService<ModalInteractionContext> m_Service;

    public AuthModalModule(UserRepository userRepository, ILogger<AuthModalModule> logger, CookieService cookieService,
        TokenCacheService tokenCacheService, GenshinCharacterCommandService<ModalInteractionContext> service)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_CookieService = cookieService;
        m_TokenCacheService = tokenCacheService;
        m_Service = service;
    }

    [ComponentInteraction("add_auth_modal")]
    public async Task AddAuth()
    {
        try
        {
            m_Logger.LogInformation("Processing add auth modal submission from user {UserId}", Context.User.Id);

            var user = await m_UserRepository.GetUserAsync(Context.User.Id);
            user ??= new UserModel
            {
                Id = Context.User.Id
            };

            var inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            if (!ulong.TryParse(inputs["ltuid"], out var ltuid))
            {
                m_Logger.LogWarning("User {UserId} provided invalid UID format", Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Invalid UID!"))));
                return;
            }

            m_Logger.LogDebug("Encrypting cookie for user {UserId}", Context.User.Id);
            user.Profiles ??= new List<UserProfile>();
            if (user.Profiles.Any(x => x.LtUid == ltuid))
            {
                m_Logger.LogWarning("User {UserId} already has a profile with UID {LtUid}", Context.User.Id, ltuid);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Profile already exists!"))));
                return;
            }

            UserProfile profile = new()
            {
                ProfileId = (uint)user.Profiles.Count() + 1,
                LtUid = ltuid,
                LToken = await Task.Run(() =>
                    m_CookieService.EncryptCookie(inputs["ltoken"], inputs["passphrase"])),
                GameUids = new Dictionary<GameName, Dictionary<string, string>>()
            };

            user.Profiles = user.Profiles.Append(profile);

            await m_UserRepository.CreateOrUpdateUserAsync(user);
            m_Logger.LogInformation("User {UserId} added new profile", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("Added profile successfully!"))));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            InteractionMessageProperties responseMessage = new()
            {
                Content = "An error occurred",
                Flags = MessageFlags.Ephemeral
            };
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
        }
    }

    [ComponentInteraction("character_auth_modal")]
    public async Task CharacterAuth(string characterName,
        [ComponentInteractionParameter(TypeReaderType = typeof(RegionsEnumTypeReader<ModalInteractionContext>))]
        Regions server, uint profile = 1)
    {
        var (success, ltuid, ltoken) = await AuthUser(profile);
        if (success)
        {
            m_Logger.LogInformation("User {UserId} successfully authenticated", Context.User.Id);
            m_Service.Context = Context;
            await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);
        }
    }

    private async Task<(bool, ulong, string)> AuthUser(uint profile)
    {
        try
        {
            m_Logger.LogInformation("Processing auth modal submission from user {UserId}", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var user = await m_UserRepository.GetUserAsync(Context.User.Id);
            if (user?.Profiles == null || user.Profiles.All(x => x.ProfileId != profile))
            {
                m_Logger.LogWarning("User {UserId} not found in database", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .WithComponents([new TextDisplayProperties("No profile found! Please add a profile first.")]));
                return (false, 0, string.Empty);
            }

            var selectedProfile = user.Profiles.First(x => x.ProfileId == profile);

            var inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            var ltoken = m_CookieService.DecryptCookie(selectedProfile.LToken, inputs["passphrase"]);

            m_TokenCacheService.AddCacheEntry(selectedProfile.LtUid, ltoken);
            return (true, selectedProfile.LtUid, ltoken);
        }
        catch (AuthenticationTagMismatchException)
        {
            m_Logger.LogWarning("User {UserId} provided wrong passphrase", Context.User.Id);
            if (Context.Interaction.Message?.InteractionMetadata?.OriginalResponseMessageId != null)
                await Context.Interaction.DeleteFollowupMessageAsync(
                    Context.Interaction.Message.InteractionMetadata
                        .OriginalResponseMessageId!.Value);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([new TextDisplayProperties("Invalid passphrase. Please try again.")]));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            InteractionMessageProperties responseMessage = new()
            {
                Content = "An error occurred",
                Flags = MessageFlags.Ephemeral
            };
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
        }

        return (false, 0, string.Empty);
    }
}
