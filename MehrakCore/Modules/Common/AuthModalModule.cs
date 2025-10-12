#region

using Mehrak.Domain.Interfaces;
using MehrakCore.Models;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using System.Security.Cryptography;

#endregion

namespace MehrakCore.Modules.Common;

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    public static ModalProperties AddAuthModal => new ModalProperties("add_auth_modal", "Authenticate")
        .WithComponents([
            new TextInputProperties("ltuid", TextInputStyle.Short, "HoYoLAB UID"),
            new TextInputProperties("ltoken", TextInputStyle.Paragraph, "HoYoLAB Cookies"),
            new TextInputProperties("passphrase", TextInputStyle.Paragraph, "Passphrase")
                .WithPlaceholder("Do not use the same password as your Discord or HoYoLAB account!").WithMaxLength(64)
        ]);

    public static ModalProperties AuthModal(string guid, uint profile)
    {
        return new ModalProperties($"auth_modal:{guid}:{profile}", "Authenticate")
            .AddComponents([
                new TextInputProperties("passphrase", TextInputStyle.Paragraph, "Passphrase")
                    .WithPlaceholder("Your Passphrase").WithMaxLength(64)
            ]);
    }

    private readonly UserRepository m_UserRepository;
    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly CookieEncryptionService m_CookieService;
    private readonly RedisCacheService m_TokenCacheService;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;

    public AuthModalModule(UserRepository userRepository, ILogger<AuthModalModule> logger, CookieEncryptionService cookieService,
        RedisCacheService tokenCacheService, IAuthenticationMiddlewareService authenticationMiddleware)
    {
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_CookieService = cookieService;
        m_TokenCacheService = tokenCacheService;
        m_AuthenticationMiddleware = authenticationMiddleware;
    }

    [ComponentInteraction("add_auth_modal")]
    public async Task AddAuth()
    {
        try
        {
            m_Logger.LogInformation("Processing add auth modal submission from user {UserId}", Context.User.Id);

            UserModel? user = await m_UserRepository.GetUserAsync(Context.User.Id);
            user ??= new UserModel
            {
                Id = Context.User.Id
            };

            Dictionary<string, string> inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            if (!ulong.TryParse(inputs["ltuid"], out ulong ltuid))
            {
                m_Logger.LogWarning("User {UserId} provided invalid UID format", Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Invalid UID!"))));
                return;
            }

            m_Logger.LogDebug("Encrypting cookie for user {UserId}", Context.User.Id);
            user.Profiles ??= [];
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
                GameUids = []
            };

            user.Profiles = user.Profiles.Append(profile);

            await m_UserRepository.CreateOrUpdateUserAsync(user);
            m_Logger.LogInformation("User {UserId} added new profile", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("Added profile successfully!"))));
            BotMetrics.TrackCommand(Context.Interaction.User, "profile add", true);
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
            BotMetrics.TrackCommand(Context.Interaction.User, "profile add", false);
        }
    }

    [ComponentInteraction("auth_modal")]
    public async Task AuthModalCallback(string guid, uint profile)
    {
        if (!m_AuthenticationMiddleware.ContainsAuthenticationRequest(guid))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(
                    MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(
                    new TextDisplayProperties(
                        "This authentication request has expired or is invalid. Please try again"))));
            return;
        }

        AuthenticationResult authResult = await AuthenticateUser(profile);

        // Notify the middleware about the authentication result
        await m_AuthenticationMiddleware.NotifyAuthenticationCompletedAsync(guid, authResult);
    }

    private async Task<AuthenticationResult> AuthenticateUser(uint profile)
    {
        try
        {
            m_Logger.LogInformation("Processing auth modal submission from user {UserId}", Context.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            UserModel? user = await m_UserRepository.GetUserAsync(Context.User.Id);
            if (user?.Profiles == null || user.Profiles.All(x => x.ProfileId != profile))
            {
                m_Logger.LogWarning("User {UserId} not found in database", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .WithComponents([new TextDisplayProperties("No profile found! Please add a profile first")]));
                return AuthenticationResult.Failure(Context.User.Id, "No profile found");
            }

            UserProfile selectedProfile = user.Profiles.First(x => x.ProfileId == profile);

            Dictionary<string, string> inputs = Context.Components.OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            string ltoken = m_CookieService.DecryptCookie(selectedProfile.LToken, inputs["passphrase"]);

            await m_TokenCacheService.AddCacheEntryAsync(selectedProfile.LtUid, ltoken, Context.Interaction.User.Id);
            return AuthenticationResult.Success(Context.User.Id, selectedProfile.LtUid, ltoken, Context);
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
                .WithComponents([new TextDisplayProperties("Invalid passphrase. Please try again")]));
            return AuthenticationResult.Failure(Context.User.Id, "Invalid passphrase");
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(
                    new TextDisplayProperties("An error occurred during authentication. Please try again later"))));
            return AuthenticationResult.Failure(Context.User.Id, "An error occurred during authentication");
        }
    }
}
