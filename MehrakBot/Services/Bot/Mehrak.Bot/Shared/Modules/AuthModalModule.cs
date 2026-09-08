﻿﻿#region

using Mehrak.Bot.Shared.Abstractions;
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

    public static ModalProperties AddAuthModal => new ModalProperties("add_auth_modal", "Authenticate")
        .WithComponents([
            new LabelProperties("HoYoLAB UID", new TextInputProperties("ltuid", TextInputStyle.Short)),
            new LabelProperties("HoYoLAB Cookies", new TextInputProperties("ltoken", TextInputStyle.Paragraph)),
            new LabelProperties("Passphrase", new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                .WithPlaceholder("Do not use the same password as your Discord or HoYoLAB account!").WithMinLength(MinPassphraseLength).WithMaxLength(MaxPassphraseLength))
        ]);

    public static ModalProperties AuthModal(string guid)
    {
        return new ModalProperties($"auth_modal:{guid}", "Authenticate")
            .AddComponents(
                new LabelProperties("Passphrase", new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                    .WithPlaceholder("Your Passphrase").WithMaxLength(64))
            );
    }

    public static ModalProperties UpdateAuthModal(UserProfileDto profile)
    {
        return new ModalProperties($"update_auth_modal:{profile.ProfileId}", "Update Authentication")
            .WithComponents([
                new TextDisplayProperties($"## Profile {profile.ProfileId}\n### HoYoLAB UID: {profile.LtUid}"),
                new LabelProperties("HoYoLAB Cookies", new TextInputProperties("ltoken", TextInputStyle.Paragraph)),
                new LabelProperties("Passphrase", new TextInputProperties("passphrase", TextInputStyle.Paragraph)
                    .WithPlaceholder("Do not use the same password as your Discord or HoYoLAB account!").WithMinLength(MinPassphraseLength).WithMaxLength(MaxPassphraseLength))
            ]);

    }

    private readonly ILogger<AuthModalModule> m_Logger;
    private readonly IEncryptionService m_CookieService;
    private readonly UserDbContext m_UserContext;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;
    private readonly ICacheService? m_CacheService;
    private readonly UserCountTrackerService m_UserTracker;
    private readonly GameRoleApiService m_GameRoleApi;

    public AuthModalModule(
        IEncryptionService cookieService,
        UserDbContext userRepository,
        IAuthenticationMiddlewareService authenticationMiddleware,
        UserCountTrackerService userTracker,
        GameRoleApiService gameRoleApi,
        ILogger<AuthModalModule> logger,
        ICacheService? cacheService = null)
    {
        m_Logger = logger;
        m_CookieService = cookieService;
        m_UserContext = userRepository;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_UserTracker = userTracker;
        m_GameRoleApi = gameRoleApi;
        m_CacheService = cacheService;
    }

    [ComponentInteraction("add_auth_modal")]
    public async Task AddAuth()
    {
        try
        {
            m_Logger.LogInformation("Processing add auth modal submission from user {UserId}", Context.User.Id);

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));

            var inputs = Context.Components
                .OfType<Label>()
                .Select(l => l.Component)
                .OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            if (!ulong.TryParse(inputs["ltuid"], out var ltuid))
            {
                m_Logger.LogWarning("User {UserId} provided invalid UID format", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Invalid UID!")));
                return;
            }

            m_Logger.LogDebug("Encrypting cookie for user {UserId}", Context.User.Id);
            if (!LTokenValidator.IsValidLToken(inputs["ltoken"]))
            {
                // Reject malformed credential characters/lengths before the token reaches the GameApi Cookie-header
                // construction, where illegal characters would throw a credential-embedding FormatException into
                // retained logs. Never log the value itself.
                m_Logger.LogWarning("User {UserId} provided malformed cookie format", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please check your credentials and try again.")));
                return;
            }

            if (!IsValidNewPassphrase(inputs["passphrase"]))
            {
                // Enforce the minimum for new passphrases server-side as well (modal min-length is client-enforced).
                // Existing weak passphrases are unaffected: they only flow through the decrypt-only auth modal below.
                m_Logger.LogWarning("User {UserId} provided too-short passphrase for new profile", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties($"Passphrase must be between {MinPassphraseLength} and {MaxPassphraseLength} characters long. Please choose a longer passphrase.")));
                return;
            }

            // Validate cookie and fetch all game profiles before saving
            var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
                Context.User.Id, ltuid, inputs["ltoken"], bypassCache: true);

            if (!gameProfilesResult.IsSuccess)
            {
                if (gameProfilesResult.StatusCode == Domain.Shared.Models.StatusCode.Unauthorized)
                {
                    m_Logger.LogWarning("User {UserId} provided invalid cookies for UID {LtUid}", Context.User.Id, ltuid);
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please check your credentials and try again.")));
                    return;
                }

                m_Logger.LogWarning("Failed to validate profile for user {UserId}: {Error}", Context.User.Id, gameProfilesResult.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to validate profile. Please try again later.")));
                return;
            }

            if (gameProfilesResult.Data.Count == 0)
            {
                m_Logger.LogWarning("No supported game profiles found for user {UserId}, UID {LtUid}", Context.User.Id, ltuid);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("No supported game profiles were found for this HoYoLAB account.")));
                return;
            }

            UserProfileModel profile = new()
            {
                UserId = (long)Context.User.Id,
                LtUid = (long)ltuid,
                LToken = await Task.Run(() =>
                    m_CookieService.Encrypt(inputs["ltoken"], inputs["passphrase"]))
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
                            return new ProfileAddResult(ProfileAddStatus.TooMany, false);

                        if (user.Profiles.Any(existing => existing.LtUid == (long)ltuid))
                            return new ProfileAddResult(ProfileAddStatus.Duplicate, false);

                        var hadProfiles = user.Profiles.Count > 0;
                        profile.ProfileId = user.Profiles.Count + 1;
                        user.Profiles.Add(profile);
                        await m_UserContext.SaveChangesAsync();
                        return new ProfileAddResult(ProfileAddStatus.Added, hadProfiles);
                    });

                if (addResult.Status == ProfileAddStatus.TooMany)
                {
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties("You can only have 10 profiles!")));
                    return;
                }

                if (addResult.Status == ProfileAddStatus.Duplicate)
                {
                    m_Logger.LogWarning("User {UserId} already has a profile with UID {LtUid}", Context.User.Id, ltuid);
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties("Profile already exists!")));
                    return;
                }

                if (!addResult.HadProfiles)
                    try
                    {
                        await m_UserTracker.AdjustUserCountAsync(1);
                    }
                    catch (Exception e)
                    {
                        m_Logger.LogWarning(e, "Failed to adjust user count for user {UserId}", Context.User.Id);
                    }
                m_Logger.LogInformation("User {UserId} added new profile with {Count} game profiles", Context.User.Id, gameProfilesResult.Data.Count);

                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties(
                            $"Added profile successfully!"),
                            new ComponentContainerProperties().AddComponents(new TextDisplayProperties(profile.ToDisplayString()))));
            }
            catch (DbUpdateException e)
            {
                m_Logger.LogError(e, "Failed to add profile for user {UserId}", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to add profile! Please try again later")));
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("An error occurred while processing your request. Please try again later")));
        }
    }


    [ComponentInteraction("update_auth_modal")]
    public async Task UpdateModalCallback(uint profileId)
    {
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
                        .AddComponents(new TextDisplayProperties("No profile found!")));
                return;
            }

            var inputs = Context.Components
                .OfType<Label>()
                .Select(l => l.Component)
                .OfType<TextInput>()
                .ToDictionary(x => x.CustomId, x => x.Value);

            // Reject malformed credential characters/lengths before the token reaches the GameApi Cookie-header
            // construction, where illegal characters would throw a credential-embedding FormatException into retained
            // logs. Never log the value itself.
            if (!LTokenValidator.IsValidLToken(inputs["ltoken"]))
            {
                m_Logger.LogWarning("User {UserId} provided malformed cookie format during update", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please check your credentials and try again.")));
                return;
            }

            if (!IsValidNewPassphrase(inputs["passphrase"]))
            {
                // Enforce the minimum for changed passphrases server-side as well (modal min-length is
                // client-enforced).
                m_Logger.LogWarning("User {UserId} provided too-short passphrase during update", Context.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties($"Passphrase must be between {MinPassphraseLength} and {MaxPassphraseLength} characters long. Please choose a longer passphrase.")));
                return;
            }

            // Validate the new cookie against HoYoLAB before saving (bypass cache to always hit upstream)
            var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
                Context.User.Id, profile.LtUid, inputs["ltoken"], bypassCache: true);

            if (!gameProfilesResult.IsSuccess)
            {
                if (gameProfilesResult.StatusCode == Domain.Shared.Models.StatusCode.Unauthorized)
                {
                    m_Logger.LogWarning("User {UserId} provided invalid cookies for UID {LtUid} during update", Context.User.Id, profile.LtUid);
                    await Context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                            .AddComponents(new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please check your credentials and try again.")));
                    return;
                }

                m_Logger.LogWarning("Failed to validate profile for user {UserId} during update: {Error}", Context.User.Id, gameProfilesResult.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("Failed to validate profile. Please try again later.")));
                return;
            }

            if (gameProfilesResult.Data.Count == 0)
            {
                m_Logger.LogWarning("No supported game profiles found for user {UserId}, UID {LtUid} during update", Context.User.Id, profile.LtUid);
                await Context.Interaction.SendFollowupMessageAsync(
                    new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                        .AddComponents(new TextDisplayProperties("No supported game profiles were found for this HoYoLAB account.")));
                return;
            }

            var newLToken = await Task.Run(() => m_CookieService.Encrypt(inputs["ltoken"], inputs["passphrase"]));

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
                        .AddComponents(new TextDisplayProperties("Failed to update profile! Please try again later")));
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
                    .AddComponents(new TextDisplayProperties("Profile successfully updated!")));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing update auth modal for user {UserId}", Context.User.Id);

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("An error occurred while processing your request. Please try again later")));
        }
    }


    [ComponentInteraction("auth_modal")]
    public async Task AuthModalCallback(string guid)
    {
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
                        "This authentication request has expired or is invalid. Please try again"))));
        }
    }

    private enum ProfileAddStatus
    {
        Added,
        TooMany,
        Duplicate
    }

    private sealed record ProfileAddResult(ProfileAddStatus Status, bool HadProfiles);
}


