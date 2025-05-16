#region

using System.Security.Cryptography;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Modules;

[SlashCommand("profile", "Manage your profile")]
public class ProfileCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly ILogger<ProfileCommandModule> m_Logger;

    public ProfileCommandModule(UserRepository userRepository, TokenCacheService tokenCacheService,
        ILogger<ProfileCommandModule> logger)
    {
        m_UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
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
        ushort profileId = 0)
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
            $"{string.Join('\n', x.GameUids?.Select(y => $"{y.Key.ToString()}, {string.Join(", ", y.Value.Keys, y.Value.Values)}") ?? [])}"));
        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
            new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(profileList)));
    }
}

public class OldAuthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly ILogger<OldAuthCommandModule> m_Logger;

    public OldAuthCommandModule(UserRepository userRepository, TokenCacheService tokenCacheService,
        ILogger<OldAuthCommandModule> logger)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_TokenCacheService = tokenCacheService;
    }

    [UserCommand("Authenticate HoYoLAB Profile")]
    public async Task AuthCommand()
    {
        m_Logger.LogInformation("User {UserId} is authenticating HoYoLAB profile", Context.User.Id);

        await RespondAsync(InteractionCallback.Modal(AuthModalModule.AddAuthModal));
    }

    [UserCommand("Delete Profile")]
    public async Task DeleteProfileCommand()
    {
        m_Logger.LogInformation("User {UserId} requested profile deletion", Context.User.Id);

        var result = await m_UserRepository.DeleteUserAsync(Context.User.Id);

        m_TokenCacheService.RemoveEntry(Context.User.Id);

        m_Logger.LogInformation("Profile deletion for user {UserId}: {Result}",
            Context.User.Id, result ? "Success" : "Not Found");

        InteractionMessageProperties responseMessage = new()
        {
            Content = result ? "Profile deleted!" : "No profile found!",
            Flags = MessageFlags.Ephemeral
        };

        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(responseMessage));
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

    public static ModalProperties AuthModal(string server, string character)
    {
        return new ModalProperties($"character_auth_modal:{server}:{character}", "Authenticate")
            .AddComponents([
                new TextInputProperties("passphrase", TextInputStyle.Paragraph, "Passphrase")
                    .WithPlaceholder("Your Passphrase").WithMaxLength(64)
            ]);
    }

    private readonly UserRepository m_UserRespository;
    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly CookieService m_CookieService;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterCommandService<ModalInteractionContext> m_Service;

    public AuthModalModule(UserRepository userRespository, ILogger<AuthModalModule> logger, CookieService cookieService,
        TokenCacheService tokenCacheService, GenshinCharacterCommandService<ModalInteractionContext> service)
    {
        m_UserRespository = userRespository;
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

            var user = await m_UserRespository.GetUserAsync(Context.User.Id);
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
                ProfileId = (ushort)(user.Profiles.Count() + 1),
                Guid = Guid.NewGuid(),
                LtUid = ltuid,
                LToken = await Task.Run(() =>
                    m_CookieService.EncryptCookie(inputs["ltoken"], inputs["passphrase"])),
                GameUids = new Dictionary<GameName, Dictionary<string, string>>()
            };

            user.Profiles = user.Profiles.Append(profile);

            await m_UserRespository.CreateOrUpdateUserAsync(user);
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
    public async Task CharacterAuth(string server, string characterName)
    {
        if (await AuthUser())
        {
            m_Logger.LogInformation("User {UserId} successfully authenticated", Context.User.Id);
            m_Service.Context = Context;
            await m_Service.SendCharacterCardResponseAsync(server, characterName);
        }
    }

    private async Task<bool> AuthUser()
    {
        try
        {
            m_Logger.LogInformation("Processing auth modal submission from user {UserId}", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var user = await m_UserRespository.GetUserAsync(Context.User.Id);
            if (user == null)
            {
                m_Logger.LogWarning("User {UserId} not found in database", Context.User.Id);
                if (Context.Interaction.Message?.InteractionMetadata?.OriginalResponseMessageId != null)
                    await Context.Interaction.DeleteFollowupMessageAsync(
                        Context.Interaction.Message.InteractionMetadata
                            .OriginalResponseMessageId!.Value);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .WithComponents([new TextDisplayProperties("No profile found! Please add a profile first.")]));
                return false;
            }

            var inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            var ltoken = m_CookieService.DecryptCookie(user.LToken, inputs["passphrase"]);

            m_TokenCacheService.AddCacheEntry(Context.User.Id, user.LtUid, ltoken);
            return true;
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

        return false;
    }
}
