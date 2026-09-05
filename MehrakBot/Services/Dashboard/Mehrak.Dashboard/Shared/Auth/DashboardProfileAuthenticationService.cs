using System.Security.Cryptography;
using System.Text;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.Infrastructure.Shared;
using Mehrak.Infrastructure.User;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Shared.Auth;

public interface IDashboardProfileAuthenticationService
{
    Task<DashboardProfileAuthenticationResult> AuthenticateAsync(
        ulong discordUserId,
        int profileId,
        string? passphrase,
        CancellationToken ct = default,
        string? sessionToken = null);

    /// <summary>
    /// Removes every credential cache entry (Bot and Dashboard) for one profile.
    /// </summary>
    Task RevokeAsync(ulong discordUserId, ulong ltUid, CancellationToken ct = default);

    /// <summary>
    /// Removes every credential cache entry (Bot and Dashboard) for all of a user's profiles.
    /// Best-effort: never throws for cache or lookup failures.
    /// </summary>
    Task RevokeAllAsync(ulong discordUserId, CancellationToken ct = default);
}

/// <summary>
/// Dashboard profile unlock ticket (finding 8).
/// Bound to the owning login session and to the exact stored credential
/// revision, with an absolute lifetime. A ticket created before a credential
/// rotation, or presented from a different session, never authenticates.
/// </summary>
public sealed record DashboardUnlockTicket(string CredentialHash, string SessionHash, string LToken);

public class DashboardProfileAuthenticationService : IDashboardProfileAuthenticationService
{
    private readonly UserDbContext m_UserRepository;
    private readonly IEncryptionService m_EncryptionService;
    private readonly ICacheService m_CacheService;
    private readonly ILogger<DashboardProfileAuthenticationService> m_Logger;
    private readonly IPassphraseAttemptRateLimiter m_PassphraseLimiter;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardProfileAuthenticationService(
        UserDbContext userRepository,
        IEncryptionService encryptionService,
        ICacheService cacheService,
        ILogger<DashboardProfileAuthenticationService> logger,
        IPassphraseAttemptRateLimiter passphraseLimiter)
    {
        m_UserRepository = userRepository;
        m_EncryptionService = encryptionService;
        m_CacheService = cacheService;
        m_Logger = logger;
        m_PassphraseLimiter = passphraseLimiter;
    }

    public async Task<DashboardProfileAuthenticationResult> AuthenticateAsync(
        ulong discordUserId,
        int profileId,
        string? passphrase,
        CancellationToken ct = default,
        string? sessionToken = null)
    {
        ct.ThrowIfCancellationRequested();

        m_Logger.LogDebug("Dashboard authentication requested for DiscordUserId={UserId}, ProfileId={ProfileId}",
            discordUserId, profileId);

        var user = await m_UserRepository.Users.Where(u => u.Id == (long)discordUserId)
            .Select(u => new UserDto
            {
                Id = (ulong)u.Id,
                Profiles = u.Profiles
                    .Where(p => p.ProfileId == profileId)
                    .Select(p => new UserProfileDto
                    {
                        Id = p.Id,
                        ProfileId = p.ProfileId,
                        LtUid = (ulong)p.LtUid,
                        LToken = p.LToken
                    })
            }).FirstOrDefaultAsync(ct);

        if (user == null)
        {
            m_Logger.LogWarning("Dashboard authentication failed: user {UserId} not found", discordUserId);
            return DashboardProfileAuthenticationResult.NotFound("User account not found. Please add a profile first.");
        }

        var profile = user.Profiles?.FirstOrDefault();
        if (profile == null)
        {
            m_Logger.LogWarning("Dashboard authentication failed: profile {ProfileId} not found for user {UserId}",
                profileId, discordUserId);
            return DashboardProfileAuthenticationResult.NotFound("Profile not found. Please add a profile first.");
        }

        var cacheKey = CacheKeys.DashboardLToken(discordUserId, profile.LtUid);
        var credentialHash = ComputeHash(profile.LToken);
        var sessionHash = ComputeHash(sessionToken ?? string.Empty);

        var ticket = await TryGetTicketAsync(cacheKey, ct);
        if (ticket is not null
            && !string.IsNullOrEmpty(ticket.LToken)
            && string.Equals(ticket.CredentialHash, credentialHash, StringComparison.Ordinal)
            && string.Equals(ticket.SessionHash, sessionHash, StringComparison.Ordinal))
        {
            m_Logger.LogDebug("Dashboard authentication cache hit for user {UserId}, ltuid {LtUid}",
                discordUserId, profile.LtUid);
            // Absolute lifetime: a hit never extends the entry.
            return DashboardProfileAuthenticationResult.Success(user, profile.LtUid, ticket.LToken);
        }

        if (ticket is not null)
        {
            // Stale entry: rotated credentials, a different login session, or a
            // legacy plaintext value. Drop it so it can never authenticate.
            m_Logger.LogInformation(
                "Dropping stale dashboard unlock for user {UserId}, ltuid {LtUid}", discordUserId, profile.LtUid);
            await RemoveQuietlyAsync(cacheKey, ct);
        }

        if (string.IsNullOrWhiteSpace(passphrase))
        {
            m_Logger.LogInformation("Dashboard authentication requires passphrase for user {UserId}, profile {ProfileId}",
                discordUserId, profileId);
            return DashboardProfileAuthenticationResult.PassphraseRequired(
                "Authentication required. Please provide your passphrase.");
        }

        if (await m_PassphraseLimiter.IsBlockedAsync(discordUserId, ct))
        {
            m_Logger.LogWarning("Rate limit exceeded for passphrase attempts by user {UserId}", discordUserId);
            return DashboardProfileAuthenticationResult.RateLimited("Too many incorrect attempts. Please try again later.");
        }

        try
        {
            var decrypted = m_EncryptionService.Decrypt(profile.LToken, passphrase);
            if (string.IsNullOrEmpty(decrypted))
            {
                m_Logger.LogWarning("Dashboard authentication failed due to empty decrypted token for user {UserId}", discordUserId);
                return DashboardProfileAuthenticationResult.Failure("Unable to decrypt authentication token.");
            }

            var upgradedCipher = await TryUpgradeLegacyTokenAsync(profile.Id, profile.LToken, decrypted, passphrase, discordUserId, profileId);

            // Re-read the stored credentials before caching: a rotation that
            // landed while this authentication was in flight must not be
            // repopulated into the cache with stale credentials.
            var currentCipher = await m_UserRepository.UserProfiles
                .Where(p => p.Id == profile.Id)
                .Select(p => p.LToken)
                .FirstOrDefaultAsync(ct);

            if (currentCipher is null)
            {
                m_Logger.LogWarning(
                    "Dashboard authentication failed: profile {ProfileId} for user {UserId} was removed during authentication",
                    profileId, discordUserId);
                return DashboardProfileAuthenticationResult.NotFound("Profile not found. Please add a profile first.");
            }

            if (!string.Equals(currentCipher, profile.LToken, StringComparison.Ordinal)
                && !string.Equals(currentCipher, upgradedCipher, StringComparison.Ordinal))
            {
                m_Logger.LogWarning(
                    "Dashboard authentication refused: credentials for user {UserId}, profile {ProfileId} changed during authentication",
                    discordUserId, profileId);
                return DashboardProfileAuthenticationResult.Failure(
                    "Profile credentials changed during authentication. Please try again.");
            }

            // A stale in-flight write can at most recreate an entry bound to
            // the now-superseded credential revision, which future reads drop.
            await m_CacheService.SetAsync(new CacheEntryBase<DashboardUnlockTicket>(
                cacheKey,
                new DashboardUnlockTicket(ComputeHash(currentCipher), sessionHash, decrypted),
                CacheDuration), ct);
            m_Logger.LogInformation("Dashboard authentication succeeded for user {UserId}, profile {ProfileId}",
                discordUserId, profileId);
            return DashboardProfileAuthenticationResult.Success(user, profile.LtUid, decrypted);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            m_Logger.LogWarning(ex, "Dashboard authentication failed due to invalid passphrase for user {UserId}",
                discordUserId);
            await m_PassphraseLimiter.RecordFailureAsync(discordUserId, ct);
            return DashboardProfileAuthenticationResult.InvalidPassphrase("Incorrect passphrase. Please try again.");
        }
        catch (CryptographicException ex)
        {
            m_Logger.LogWarning(ex, "Dashboard authentication failed due to corrupted credential data for user {UserId}",
                discordUserId);
            return DashboardProfileAuthenticationResult.Failure(
                "Stored authentication data is corrupted. Please remove and re-add this profile.");
        }
    }

    public async Task RevokeAsync(ulong discordUserId, ulong ltUid, CancellationToken ct = default)
    {
        await RemoveQuietlyAsync(CacheKeys.BotLToken(discordUserId, ltUid), ct);
        await RemoveQuietlyAsync(CacheKeys.DashboardLToken(discordUserId, ltUid), ct);
    }

    public async Task RevokeAllAsync(ulong discordUserId, CancellationToken ct = default)
    {
        List<long> ltUids;
        try
        {
            ltUids = await m_UserRepository.UserProfiles
                .Where(p => p.UserId == (long)discordUserId)
                .Select(p => p.LtUid)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Failed to list profiles for unlock revocation for user {UserId}", discordUserId);
            return;
        }

        foreach (var ltUid in ltUids)
            await RevokeAsync(discordUserId, (ulong)ltUid, ct);
    }

    private async Task<DashboardUnlockTicket?> TryGetTicketAsync(string key, CancellationToken ct)
    {
        try
        {
            return await m_CacheService.GetAsync<DashboardUnlockTicket>(key, ct);
        }
        catch (Exception ex)
        {
            // Unreadable entries (for example legacy plaintext values stored
            // under this key) can never authenticate; drop them.
            m_Logger.LogDebug(ex, "Dropping unreadable dashboard unlock entry");
            await RemoveQuietlyAsync(key, ct);
            return null;
        }
    }

    private async Task RemoveQuietlyAsync(string key, CancellationToken ct)
    {
        try
        {
            await m_CacheService.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Failed to remove dashboard unlock entry");
        }
    }

    internal static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Upgrades a legacy ciphertext in place. Returns the ciphertext now
    /// stored, or null when no upgrade was persisted.
    /// </summary>
    private async Task<string?> TryUpgradeLegacyTokenAsync(
        long profileRowId,
        string storedLtoken,
        string decryptedLtoken,
        string passphrase,
        ulong discordUserId,
        int profileId)
    {
        if (!m_EncryptionService.IsLegacyFormat(storedLtoken)) return null;

        try
        {
            var upgraded = m_EncryptionService.Encrypt(decryptedLtoken, passphrase);
            var profileModel = await m_UserRepository.UserProfiles.SingleAsync(p => p.Id == profileRowId);
            profileModel.LToken = upgraded;
            await m_UserRepository.SaveChangesAsync();
            m_Logger.LogInformation("Upgraded legacy LToken encryption for user {UserId}, profile {ProfileId}",
                discordUserId, profileId);
            return upgraded;
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex,
                "Failed to persist upgraded LToken encryption for user {UserId}, profile {ProfileId}; continuing with existing credentials",
                discordUserId, profileId);
            return null;
        }
    }
}

public enum DashboardAuthStatus
{
    Success,
    NotFound,
    PassphraseRequired,
    InvalidPassphrase,
    Failure,
    RateLimited
}

public class DashboardProfileAuthenticationResult
{
    public DashboardAuthStatus Status { get; }
    public string? Error { get; }
    public UserDto? User { get; }
    public ulong LtUid { get; }
    public string? LToken { get; }
    public bool IsSuccess => Status == DashboardAuthStatus.Success;

    private DashboardProfileAuthenticationResult(
        DashboardAuthStatus status,
        string? error,
        UserDto? user,
        ulong ltUid,
        string? ltoken)
    {
        Status = status;
        Error = error;
        User = user;
        LtUid = ltUid;
        LToken = ltoken;
    }

    public static DashboardProfileAuthenticationResult Success(UserDto user, ulong ltUid, string ltoken) =>
        new(DashboardAuthStatus.Success, null, user, ltUid, ltoken);

    public static DashboardProfileAuthenticationResult NotFound(string error) =>
        new(DashboardAuthStatus.NotFound, error, null, 0, null);

    public static DashboardProfileAuthenticationResult PassphraseRequired(string error) =>
        new(DashboardAuthStatus.PassphraseRequired, error, null, 0, null);

    public static DashboardProfileAuthenticationResult InvalidPassphrase(string error) =>
        new(DashboardAuthStatus.InvalidPassphrase, error, null, 0, null);

    public static DashboardProfileAuthenticationResult RateLimited(string error) =>
        new(DashboardAuthStatus.RateLimited, error, null, 0, null);

    public static DashboardProfileAuthenticationResult Failure(string error) =>
        new(DashboardAuthStatus.Failure, error, null, 0, null);
}
