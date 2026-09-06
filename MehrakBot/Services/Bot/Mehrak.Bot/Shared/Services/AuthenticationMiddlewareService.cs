#region

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Modules;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.Infrastructure.Shared;
using Mehrak.Infrastructure.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace Mehrak.Bot.Shared.Services;

/// <summary>
/// Bot profile unlock ticket. Bound to the exact stored credential
/// revision, with an absolute lifetime. A ticket cached before a credential
/// rotation never authenticates after it.
/// </summary>
public sealed record BotUnlockTicket(string CredentialHash, string LToken);

public class AuthenticationMiddlewareService : IAuthenticationMiddlewareService
{
    private readonly ICacheService m_CacheService;
    private readonly IEncryptionService m_EncryptionService;
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<AuthenticationMiddlewareService> m_Logger;
    private readonly IPassphraseAttemptRateLimiter m_PassphraseLimiter;
    private readonly ConcurrentDictionary<string, AuthenticationResponse> m_NotifiedRequests = [];
    private readonly ConcurrentDictionary<string, byte> m_CurrentRequests = [];

    private float TimeoutMinutes { get; set; } = 1;

    public AuthenticationMiddlewareService(
        ICacheService cacheService,
        IEncryptionService encryptionService,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AuthenticationMiddlewareService> logger,
        IPassphraseAttemptRateLimiter passphraseLimiter)
    {
        m_CacheService = cacheService;
        m_EncryptionService = encryptionService;
        m_ServiceScopeFactory = serviceScopeFactory;
        m_Logger = logger;
        m_PassphraseLimiter = passphraseLimiter;
    }

    public async Task<AuthenticationResult> GetAuthenticationAsync(AuthenticationRequest request)
    {
        m_Logger.LogDebug("GetAuthenticationAsync started for UserId={UserId}, ProfileId={ProfileId}",
            request.Context.Interaction.User.Id, request.ProfileId);

        using var scope = m_ServiceScopeFactory.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();

        var user = await userContext.Users
            .AsNoTracking()
            .Where(u => u.Id == (long)request.Context.Interaction.User.Id)
            .Select(u => new UserDto()
            {
                Id = (ulong)u.Id,
                Profiles = u.Profiles.Where(x => x.ProfileId == request.ProfileId).Select(p => new UserProfileDto()
                {
                    Id = p.Id,
                    ProfileId = p.ProfileId,
                    LtUid = (ulong)p.LtUid,
                    LToken = p.LToken
                }).ToList()
            }).FirstOrDefaultAsync();

        if (user == null)
        {
            m_Logger.LogWarning("User account not found for UserId={UserId}", request.Context.Interaction.User.Id);
            return AuthenticationResult.NotFound(request.Context, "User account not found. Please add a profile first.");
        }

        var profile = user.Profiles?.FirstOrDefault();

        if (profile == null)
        {
            m_Logger.LogWarning("Profile not found for UserId={UserId}, ProfileId={ProfileId}",
                request.Context.Interaction.User.Id, request.ProfileId);
            return AuthenticationResult.NotFound(request.Context, "No profiles found. Please add a profile first.");
        }

        var cacheKey = CacheKeys.BotLToken(request.Context.Interaction.User.Id, profile.LtUid);
        m_Logger.LogDebug("Checking cache for LToken. UserId={UserId}, LtUid={LtUid}",
            request.Context.Interaction.User.Id, profile.LtUid);
        var ticket = await TryGetTicketAsync(cacheKey);
        var credentialHash = ComputeHash(profile.LToken);

        if (ticket is not null
            && !string.IsNullOrEmpty(ticket.LToken)
            && string.Equals(ticket.CredentialHash, credentialHash, StringComparison.Ordinal))
        {
            m_Logger.LogDebug("Cache hit for LToken. UserId={UserId}, LtUid={LtUid}",
                request.Context.Interaction.User.Id, profile.LtUid);
            await request.Context.Interaction.SendResponseAsync(
                InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
            return AuthenticationResult.Success(request.Context.Interaction.User.Id, profile.LtUid, ticket.LToken, user,
                request.Context);
        }

        if (ticket is not null)
        {
            // Stale entry: the stored credentials were rotated after this
            // entry was cached, or the entry has an unexpected shape. Drop
            // it so a pre-rotation plaintext can never authenticate.
            m_Logger.LogInformation(
                "Dropping stale bot unlock for UserId={UserId}, LtUid={LtUid}",
                request.Context.Interaction.User.Id, profile.LtUid);
            await RemoveQuietlyAsync(cacheKey);
        }

        var guid = Guid.NewGuid().ToString();
        m_CurrentRequests.TryAdd(guid, 1);
        await request.Context.Interaction.SendResponseAsync(InteractionCallback.Modal(AuthModalModule.AuthModal(guid)));
        m_Logger.LogDebug("Auth modal sent. Guid={Guid}, UserId={UserId}, LtUid={LtUid}", guid,
            request.Context.Interaction.User.Id, profile.LtUid);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMinutes(TimeoutMinutes));
        m_Logger.LogDebug("Waiting for authentication response. Guid={Guid}, TimeoutMinutes={TimeoutMinutes}", guid,
            TimeoutMinutes);
        var authResponse = await WaitForAuthenticationAsync(guid, cts.Token).ConfigureAwait(false);

        if (authResponse is null)
        {
            m_Logger.LogInformation("Authentication timed out. Guid={Guid}, UserId={UserId}", guid,
                request.Context.Interaction.User.Id);
            return AuthenticationResult.Timeout();
        }

        await authResponse.Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        string? token;

        // Finding 12: reserve one attempt atomically before PBKDF2 work so
        // concurrent requests cannot all slip past the check. Shared via Redis.
        var reservation = await m_PassphraseLimiter.TryReserveAttemptAsync(request.Context.Interaction.User.Id);
        if (reservation is null)
        {
            m_Logger.LogWarning("Rate limit exceeded for passphrase attempts by user {UserId}", request.Context.Interaction.User.Id);
            return AuthenticationResult.Failure(authResponse.Context, "Too many incorrect attempts. Please try again later.");
        }

        var keepReservation = false;
        try
        {
            token = m_EncryptionService.Decrypt(profile.LToken, authResponse.Passphrase);
            m_Logger.LogDebug("Cookie decryption succeeded. Guid={Guid}, UserId={UserId}, LtUid={LtUid}",
                authResponse.Guid, request.Context.Interaction.User.Id, profile.LtUid);
        }
        catch (AuthenticationTagMismatchException e)
        {
            // The reservation stays as the failure record; do not record again.
            keepReservation = true;
            m_Logger.LogWarning(e, "Incorrect passphrase provided. Guid={Guid}, UserId={UserId}", authResponse.Guid,
                request.Context.Interaction.User.Id);
            return AuthenticationResult.Failure(authResponse.Context, "Incorrect passphrase. Please try again");
        }
        catch (CryptographicException e)
        {
            m_Logger.LogWarning(e, "Stored credential data is corrupted. Guid={Guid}, UserId={UserId}",
                authResponse.Guid, request.Context.Interaction.User.Id);
            return AuthenticationResult.Failure(authResponse.Context,
                "Stored authentication data is corrupted. Please remove and re-add this profile.");
        }
        finally
        {
            // Successes and corruption outcomes release so they never consume
            // failure quota; only wrong passphrases keep the reservation.
            if (!keepReservation && reservation is not null)
                await ReleasePassphraseReservationQuietlyAsync(request.Context.Interaction.User.Id, reservation);
        }

        var upgradedCipher = await TryUpgradeLegacyLtokenAsync(userContext, request.Context.Interaction.User.Id,
            profile.Id, profile.LToken, token, authResponse.Passphrase);

        // Re-read the stored credentials before caching: a rotation that
        // landed while this authentication was in flight must not be
        // repopulated into the cache with stale credentials.
        var currentCipher = await userContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.Id == profile.Id)
            .Select(p => p.LToken)
            .FirstOrDefaultAsync();

        if (currentCipher is null)
        {
            m_Logger.LogWarning(
                "Authentication refused: profile {ProfileId} for UserId={UserId} was removed during authentication",
                profile.Id, request.Context.Interaction.User.Id);
            return AuthenticationResult.NotFound(authResponse.Context,
                "No profiles found. Please add a profile first.");
        }

        if (!string.Equals(currentCipher, profile.LToken, StringComparison.Ordinal)
            && !string.Equals(currentCipher, upgradedCipher, StringComparison.Ordinal))
        {
            m_Logger.LogWarning(
                "Authentication refused: credentials for UserId={UserId}, LtUid={LtUid} changed during authentication",
                request.Context.Interaction.User.Id, profile.LtUid);
            return AuthenticationResult.Failure(authResponse.Context,
                "Profile credentials changed during authentication. Please try again.");
        }

        // A stale in-flight write can at most recreate an entry bound to
        // the now-superseded credential revision, which future reads drop.
        await m_CacheService.SetAsync(new CacheEntryBase<BotUnlockTicket>(cacheKey,
            new BotUnlockTicket(ComputeHash(currentCipher), token), TimeSpan.FromMinutes(10)));
        m_Logger.LogDebug("Authentication succeeded. UserId={UserId}, LtUid={LtUid}",
            request.Context.Interaction.User.Id, profile.LtUid);
        return AuthenticationResult.Success(request.Context.Interaction.User.Id, profile.LtUid, token, user,
            authResponse.Context);
    }

    private async Task ReleasePassphraseReservationQuietlyAsync(ulong userId, string reservation)
    {
        try
        {
            await m_PassphraseLimiter.ReleaseReservationAsync(userId, reservation);
        }
        catch (Exception ex)
        {
            m_Logger.LogDebug(ex, "Best-effort passphrase reservation release failed for UserId={UserId}", userId);
        }
    }
    /// <summary>
    /// Upgrades a legacy ciphertext in place. Returns the ciphertext now
    /// stored, or null when no upgrade was persisted.
    /// </summary>
    private async Task<string?> TryUpgradeLegacyLtokenAsync(
        UserDbContext userContext,
        ulong userId,
        long profileRowId,
        string storedLtoken,
        string decryptedLtoken,
        string passphrase)
    {
        if (!m_EncryptionService.IsLegacyFormat(storedLtoken)) return null;

        try
        {
            var upgraded = m_EncryptionService.Encrypt(decryptedLtoken, passphrase);
            var profileModel = await userContext.UserProfiles.SingleAsync(p => p.Id == profileRowId);
            profileModel.LToken = upgraded;
            await userContext.SaveChangesAsync();
            m_Logger.LogInformation("Upgraded legacy LToken encryption for UserId={UserId}, ProfileId={ProfileId}",
                userId, profileRowId);
            return upgraded;
        }
        catch (Exception e)
        {
            m_Logger.LogWarning(e,
                "Failed to persist upgraded LToken encryption for UserId={UserId}, ProfileId={ProfileId}; continuing with existing credentials",
                userId, profileRowId);
            return null;
        }
    }

    private async Task<BotUnlockTicket?> TryGetTicketAsync(string cacheKey)
    {
        try
        {
            return await m_CacheService.GetAsync<BotUnlockTicket>(cacheKey);
        }
        catch (Exception ex)
        {
            // Unreadable entries (for example legacy plaintext values stored
            // under this key) can never authenticate; drop them.
            m_Logger.LogDebug(ex, "Dropping unreadable bot unlock entry");
            await RemoveQuietlyAsync(cacheKey);
            return null;
        }
    }

    private async Task RemoveQuietlyAsync(string cacheKey)
    {
        try
        {
            await m_CacheService.RemoveAsync(cacheKey);
        }
        catch (Exception ex)
        {
            m_Logger.LogDebug(ex, "Best-effort bot unlock removal failed");
        }
    }

    internal static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public bool NotifyAuthenticate(AuthenticationResponse request)
    {
        if (!m_CurrentRequests.TryGetValue(request.Guid, out _))
        {
            m_Logger.LogWarning("No authentication requests found. Guid={Guid}", request.Guid);
            return false;
        }

        m_Logger.LogDebug("NotifyAuthenticateAsync received. Guid={Guid}, UserId={UserId}", request.Guid,
            request.UserId);
        return m_NotifiedRequests.TryAdd(request.Guid, request);
    }

    public async Task RevokeAuthenticate(ulong userId, ulong ltUid)
    {
        var cacheKey = CacheKeys.BotLToken(userId, ltUid);
        m_Logger.LogDebug("Revoking authentication. UserId={UserId}, LtUid={LtUid}", userId, ltUid);
        await m_CacheService.RemoveAsync(cacheKey);
    }

    private async Task<AuthenticationResponse?> WaitForAuthenticationAsync(string guid, CancellationToken token)
    {
        try
        {
            m_Logger.LogDebug("WaitForAuthenticationAsync started. Guid={Guid}", guid);
            AuthenticationResponse? response;
            while (!m_NotifiedRequests.TryRemove(guid, out response))
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(100, token);
            }

            m_Logger.LogDebug("Authentication response dequeued. Guid={Guid}", guid);
            return response;
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogDebug("WaitForAuthenticationAsync canceled. Guid={Guid}", guid);
            return null;
        }
        finally
        {
            m_NotifiedRequests.TryRemove(guid, out _);
            m_CurrentRequests.TryRemove(guid, out _);
        }
    }
}
