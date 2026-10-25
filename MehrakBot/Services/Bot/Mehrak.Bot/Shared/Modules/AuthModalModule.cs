#region

using System.Globalization;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Services;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Extensions;
using Mehrak.Infrastructure.User.Models;
using Mehrak.Infrastructure.User.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

#endregion

namespace Mehrak.Bot.Shared.Modules;

public class AuthModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    // Minimum for NEW/CHANGED passphrases. Must match Dashboard Add/UpdateProfileRequest. The decrypt-only AuthModal
    // below intentionally has no minimum so existing weak passphrases keep working.
    internal const int MinPassphraseLength = 12;
    internal const int MaxPassphraseLength = 64;

    internal static bool IsValidNewPassphrase(string? passphrase) =>
        !string.IsNullOrEmpty(passphrase) &&
        passphrase.Length >= MinPassphraseLength &&
        passphrase.Length <= MaxPassphraseLength;

    /// <summary>
    /// Resolves a pasted full cookie string (ltoken_v2 + ltuid_v2) into the credential pair. When
    /// <paramref name="expectedLtUid"/> is supplied (profile update), a cookie UID that differs from the stored
    /// UID is rejected just like a malformed cookie, before any upstream call. Outputs are cleared on failure so
    /// hostile input never flows back to callers.
    /// </summary>
    internal static bool TryResolveCookieCredentials(string? cookieInput, ulong? expectedLtUid, out ulong ltUid, out string ltoken)
    {
        ltUid = 0;
        ltoken = string.Empty;

        if (!CookieCredentialParser.TryParse(cookieInput, out var parsedUid, out var parsedToken))
            return false;

        if (expectedLtUid.HasValue && parsedUid != expectedLtUid.Value)
            return false;

        ltUid = parsedUid;
        ltoken = parsedToken;
        return true;
    }

    public static ModalProperties AddAuthModal => AddAuthModalFor(null);

    public static ModalProperties AddAuthModalFor(CultureInfo? uiCulture, ICommandUiLocalizer? localizer = null)
    {
        localizer ??= CommandUiLocalizer.Default;
        return new ModalProperties("add_auth_modal", localizer.Get(uiCulture, CommandUiKeys.ModalAuthTitle))
            .WithComponents([
                new LabelProperties(localizer.Get(uiCulture, CommandUiKeys.ModalCookieLabel),
                    new TextInputProperties("ltoken", TextInputStyle.Paragraph)
                        .WithPlaceholder(localizer.Get(uiCulture, CommandUiKeys.ModalCookiePlaceholder))),
                new LabelProperties(localizer.Get(uiCulture, CommandUiKeys.ModalPassphraseLabel),
                    new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                        .WithPlaceholder(localizer.Get(uiCulture, CommandUiKeys.ModalNewPassphrasePlaceholder))
                        .WithMinLength(MinPassphraseLength).WithMaxLength(MaxPassphraseLength))
            ]);
    }

    public static ModalProperties AuthModal(string guid) => AuthModal(guid, null);

    public static ModalProperties AuthModal(string guid, CultureInfo? uiCulture, ICommandUiLocalizer? localizer = null)
    {
        localizer ??= CommandUiLocalizer.Default;
        return new ModalProperties($"auth_modal:{guid}", localizer.Get(uiCulture, CommandUiKeys.ModalAuthTitle))
            .AddComponents(
                new LabelProperties(localizer.Get(uiCulture, CommandUiKeys.ModalPassphraseLabel),
                    new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                        .WithPlaceholder(localizer.Get(uiCulture, CommandUiKeys.ModalPassphrasePlaceholder))
                        .WithMaxLength(64))
            );
    }

    public static ModalProperties UpdateAuthModal(UserProfileDto profile) => UpdateAuthModal(profile, null);

    public static ModalProperties UpdateAuthModal(UserProfileDto profile, CultureInfo? uiCulture,
        ICommandUiLocalizer? localizer = null)
    {
        localizer ??= CommandUiLocalizer.Default;
        return new ModalProperties($"update_auth_modal:{profile.ProfileId}",
                localizer.Get(uiCulture, CommandUiKeys.ModalAuthUpdateTitle))
            .WithComponents([
                new TextDisplayProperties(localizer.Get(uiCulture, CommandUiKeys.ModalProfileHeader,
                    profile.ProfileId, profile.LtUid)),
                new LabelProperties(localizer.Get(uiCulture, CommandUiKeys.ModalCookieLabel),
                    new TextInputProperties("ltoken", TextInputStyle.Paragraph)
                        .WithPlaceholder(localizer.Get(uiCulture, CommandUiKeys.ModalCookiePlaceholder))),
                new LabelProperties(localizer.Get(uiCulture, CommandUiKeys.ModalPassphraseLabel),
                    new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                        .WithPlaceholder(localizer.Get(uiCulture, CommandUiKeys.ModalNewPassphrasePlaceholder))
                        .WithMinLength(MinPassphraseLength).WithMaxLength(MaxPassphraseLength))
            ]);
    }

    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly IEncryptionService m_CookieService;
    private readonly UserDbContext m_UserContext;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;
    private readonly ICacheService? m_CacheService;
    private readonly GameRoleApiService m_GameRoleApi;
    private readonly ICommandUiLocalizer m_Localizer;

    public AuthModalModule(
        IEncryptionService cookieService,
        UserDbContext userRepository,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRoleApiService gameRoleApi,
        ILogger<AuthModalModule> logger,
        ICacheService? cacheService = null,
        ICommandUiLocalizer? localizer = null)
    {
        m_Logger = logger;
        m_CookieService = cookieService;
        m_UserContext = userRepository;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_GameRoleApi = gameRoleApi;
        m_CacheService = cacheService;
        m_Localizer = localizer ?? CommandUiLocalizer.Default;
    }

    [ComponentInteraction("add_auth_modal")]
    public async Task AddAuth()
    {
        var culture = BotUiCulture.Resolve(Context);

        try
        {
            m_Logger.LogInformation("Processing add auth modal submission from user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

            var inputs = Context.Components
                .OfType<Label>()
                .Select(l => l.Component)
                .OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            // Parse the pasted cookie string first, before any upstream request or encryption. Only the extracted
            // values flow downstream: the raw string is never logged, encrypted, or sent upstream, and the value
            // itself is never logged.
            m_Logger.LogDebug("Resolving cookie for user {UserId}", Context.User.Id);
            if (!TryResolveCookieCredentials(inputs["ltoken"], null, out var ltuid, out var ltoken))
            {
                m_Logger.LogWarning("User {UserId} provided an unparsable cookie string", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthInvalidCookies))));
                return;
            }

            if (!IsValidNewPassphrase(inputs["passphrase"]))
            {
                // Enforce the minimum for new passphrases server-side as well (modal min-length is client-enforced).
                // Existing weak passphrases are unaffected: they only flow through the decrypt-only auth modal below.
                m_Logger.LogWarning("User {UserId} provided too-short passphrase for new profile", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthPassphraseLength, MinPassphraseLength, MaxPassphraseLength))));
                return;
            }

            // Validate cookie and fetch all game profiles before saving
            var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
                Context.User.Id, ltuid, ltoken, bypassCache: true);

            if (!gameProfilesResult.IsSuccess)
            {
                if (gameProfilesResult.StatusCode == Domain.Shared.Models.StatusCode.Unauthorized)
                {
                    m_Logger.LogWarning("User {UserId} provided invalid cookies for UID {LtUid}", Context.User.Id, ltuid);
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthInvalidCookies))));
                    return;
                }

                m_Logger.LogWarning("Failed to validate profile for user {UserId}: {Error}", Context.User.Id, gameProfilesResult.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthValidateFailed))));
                return;
            }

            if (gameProfilesResult.Data.Count == 0)
            {
                m_Logger.LogWarning("No supported game profiles found for user {UserId}, UID {LtUid}", Context.User.Id, ltuid);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthNoSupportedProfiles))));
                return;
            }

            UserProfileModel profile = new()
            {
                UserId = (long)Context.User.Id,
                LtUid = (long)ltuid,
                LToken = await Task.Run(() =>
                    m_CookieService.Encrypt(ltoken, inputs["passphrase"]))
            };

            // Save all game UIDs from validation
            foreach (var gameRole in gameProfilesResult.Data)
            {
                profile.GameUids.Add(new ProfileGameUid
                {
                    Game = gameRole.Game,
                    Region = gameRole.Region,
                    GameUid = gameRole.Profile.GameUid,
                    Level = gameRole.Profile.Level
                });
            }

            profile.GameUids.Sort((a, b) => a.GameUid.CompareTo(b.GameUid));

            ProfileAddResult addResult;
            try
            {
                addResult = await m_UserContext.ExecuteUserProfileMutationAsync(
                    (long)Context.User.Id,
                    async () =>
                    {
                        // The modal may have been submitted from a stale
                        // profile list. Recheck all limits and uniqueness after
                        // acquiring the shared per-user mutation lock.
                        m_UserContext.ChangeTracker.Clear();

                        var user = await m_UserContext.Users
                            .Where(u => u.Id == (long)Context.User.Id)
                            .Include(u => u.Profiles)
                            .SingleOrDefaultAsync();

                        if (user is null)
                        {
                            user = new UserModel
                            {
                                Id = (long)Context.User.Id,
                                Timestamp = DateTime.UtcNow,
                                Profiles = []
                            };
                            await m_UserContext.Users.AddAsync(user);
                        }

                        if (user.Profiles.Count >= 10)
                            return new ProfileAddResult(ProfileAddStatus.TooMany);

                        if (user.Profiles.Any(existing => existing.LtUid == (long)ltuid))
                            return new ProfileAddResult(ProfileAddStatus.Duplicate);
                        profile.ProfileId = user.Profiles.Count + 1;
                        user.Profiles.Add(profile);
                        await m_UserContext.SaveChangesAsync();
                        return new ProfileAddResult(ProfileAddStatus.Added);
                    });

                if (addResult.Status == ProfileAddStatus.TooMany)
                {
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthTooManyProfiles))));
                    return;
                }

                if (addResult.Status == ProfileAddStatus.Duplicate)
                {
                    m_Logger.LogWarning("User {UserId} already has a profile with UID {LtUid}", Context.User.Id, ltuid);
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthProfileExists))));
                    return;
                }

                m_Logger.LogInformation("User {UserId} added new profile with {Count} game profiles", Context.User.Id, gameProfilesResult.Data.Count);

                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(
                            m_Localizer.Get(culture, CommandUiKeys.AuthProfileAdded)),
                            new ComponentContainerProperties().AddComponents(new TextDisplayProperties(ProfileTextRenderer.Render(profile, m_Localizer, culture)))));
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to add profile for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthAddFailed))));
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthProcessingError))));
        }
    }


    [ComponentInteraction("update_auth_modal")]
    public async Task UpdateModalCallback(uint profileId)
    {
        var culture = BotUiCulture.Resolve(Context);

        try
        {
            m_Logger.LogInformation("Processing update auth modal submission from user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

            var user = await m_UserContext.Users
                .AsNoTracking()
                .Where(u => u.Id == (long)Context.User.Id)
                .Select(u => new UserDto()
                {
                    Id = (ulong)u.Id,
                    Profiles = u.Profiles.Where(p => p.ProfileId == profileId)
                        .Select(p => new UserProfileDto()
                        {
                            Id = p.Id,
                            ProfileId = p.ProfileId,
                            LtUid = (ulong)p.LtUid
                        }).ToList()
                }).FirstOrDefaultAsync();

            var profile = user?.Profiles?.FirstOrDefault();

            if (profile == null)
            {
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthProfileNotFound))));
                return;
            }

            var inputs = Context.Components
                .OfType<Label>()
                .Select(l => l.Component)
                .OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            // Parse the pasted cookie string before any upstream request or encryption. A cookie UID that differs
            // from the stored profile UID is rejected here, before upstream validation or persistence. Only the
            // extracted token flows downstream; the raw string is never logged, encrypted, or sent upstream, and the
            // value itself is never logged.
            if (!TryResolveCookieCredentials(inputs["ltoken"], profile.LtUid, out _, out var ltoken))
            {
                m_Logger.LogWarning("User {UserId} provided an unparsable or mismatched cookie string during update", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthInvalidCookies))));
                return;
            }

            if (!IsValidNewPassphrase(inputs["passphrase"]))
            {
                // Enforce the minimum for changed passphrases server-side as well (modal min-length is
                // client-enforced).
                m_Logger.LogWarning("User {UserId} provided too-short passphrase during update", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthPassphraseLength, MinPassphraseLength, MaxPassphraseLength))));
                return;
            }

            // Validate the new cookie against HoYoLAB before saving (bypass cache to always hit upstream)
            var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
                Context.User.Id, profile.LtUid, ltoken, bypassCache: true);

            if (!gameProfilesResult.IsSuccess)
            {
                if (gameProfilesResult.StatusCode == Domain.Shared.Models.StatusCode.Unauthorized)
                {
                    m_Logger.LogWarning("User {UserId} provided invalid cookies for UID {LtUid} during update", Context.User.Id, profile.LtUid);
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthInvalidCookies))));
                    return;
                }

                m_Logger.LogWarning("Failed to validate profile for user {UserId} during update: {Error}", Context.User.Id, gameProfilesResult.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthValidateFailed))));
                return;
            }

            if (gameProfilesResult.Data.Count == 0)
            {
                m_Logger.LogWarning("No supported game profiles found for user {UserId}, UID {LtUid} during update", Context.User.Id, profile.LtUid);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthNoSupportedProfiles))));
                return;
            }

            var newLToken = await Task.Run(() => m_CookieService.Encrypt(ltoken, inputs["passphrase"]));

            try
            {
                await m_UserContext.UserProfiles
                    .Where(p => p.Id == profile.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LToken, newLToken));
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to update profile for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthUpdateFailed))));
                return;
            }

            await m_AuthenticationMiddleware.RevokeAuthenticate(Context.User.Id, profile.LtUid);
            // A Bot-side rotation also revokes the Dashboard unlock for the same profile.
            try
            {
                if (m_CacheService is not null)
                    await m_CacheService.RemoveAsync(CacheKeys.DashboardLToken(Context.User.Id, profile.LtUid));
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Failed to revoke dashboard authentication cache for user {UserId}", Context.User.Id);
            }
            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthProfileUpdated))));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing update auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties(m_Localizer.Get(culture, CommandUiKeys.AuthProcessingError))));
        }
    }


    [ComponentInteraction("auth_modal")]
    public async Task AuthModalCallback(string guid)
    {
        var culture = BotUiCulture.Resolve(Context);
        var passphrase = Context.Components
            .OfType<Label>()
            .Select(l => l.Component)
            .OfType<TextInput>()
            .First(x => x.CustomId == "passphrase").Value;

        if (!m_AuthenticationMiddleware.NotifyAuthenticate(new AuthenticationResponse(Context.User.Id, guid, passphrase,
                Context)))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(
                    MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(
                    new TextDisplayProperties(
                        m_Localizer.Get(culture, CommandUiKeys.AuthModalExpired)))));
        }
    }

    private enum ProfileAddStatus
    {
        Added,
        TooMany,
        Duplicate
    }

    private sealed record ProfileAddResult(ProfileAddStatus Status);
}


