using System.Security.Cryptography;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Models;

namespace Mehrak.Dashboard.Auth;

public interface IDashboardProfileAuthenticationService
{
    Task<DashboardProfileAuthenticationResult> AuthenticateAsync(
        ulong discordUserId,
        uint profileId,
        string? passphrase,
        CancellationToken ct = default);

    Task RefreshAsync(ulong discordUserId, ulong ltUid, string ltoken, CancellationToken ct = default);
}

public class DashboardProfileAuthenticationService : IDashboardProfileAuthenticationService
{
    private readonly IUserRepository m_UserRepository;
    private readonly IEncryptionService m_EncryptionService;
    private readonly ICacheService m_CacheService;
    private readonly ILogger<DashboardProfileAuthenticationService> m_Logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardProfileAuthenticationService(
        IUserRepository userRepository,
        IEncryptionService encryptionService,
        ICacheService cacheService,
        ILogger<DashboardProfileAuthenticationService> logger)
    {
        m_UserRepository = userRepository;
        m_EncryptionService = encryptionService;
        m_CacheService = cacheService;
        m_Logger = logger;
    }

    public async Task<DashboardProfileAuthenticationResult> AuthenticateAsync(
        ulong discordUserId,
        uint profileId,
        string? passphrase,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        m_Logger.LogDebug("Dashboard authentication requested for DiscordUserId={UserId}, ProfileId={ProfileId}",
            discordUserId, profileId);

        var user = await m_UserRepository.GetUserAsync(discordUserId);
        if (user == null)
        {
            m_Logger.LogWarning("Dashboard authentication failed: user {UserId} not found", discordUserId);
            return DashboardProfileAuthenticationResult.NotFound("User account not found. Please add a profile first.");
        }

        var profile = user.Profiles?.FirstOrDefault(p => p.ProfileId == profileId);
        if (profile == null)
        {
            m_Logger.LogWarning("Dashboard authentication failed: profile {ProfileId} not found for user {UserId}",
                profileId, discordUserId);
            return DashboardProfileAuthenticationResult.NotFound("Profile not found. Please add a profile first.");
        }

        var cacheKey = CacheKeys.DashboardLToken(discordUserId, profile.LtUid);
        var cachedToken = await m_CacheService.GetAsync<string>(cacheKey);

        if (!string.IsNullOrEmpty(cachedToken))
        {
            m_Logger.LogDebug("Dashboard authentication cache hit for user {UserId}, ltuid {LtUid}",
                discordUserId, profile.LtUid);
            await RefreshCacheAsync(cacheKey, cachedToken);
            return DashboardProfileAuthenticationResult.Success(user, profile.LtUid, cachedToken);
        }

        if (string.IsNullOrWhiteSpace(passphrase))
        {
            m_Logger.LogInformation("Dashboard authentication requires passphrase for user {UserId}, profile {ProfileId}",
                discordUserId, profileId);
            return DashboardProfileAuthenticationResult.PassphraseRequired(
                "Authentication required. Please provide your passphrase.");
        }

        try
        {
            var decrypted = m_EncryptionService.Decrypt(profile.LToken, passphrase);
            if (string.IsNullOrEmpty(decrypted))
            {
                m_Logger.LogWarning("Dashboard authentication failed due to empty decrypted token for user {UserId}", discordUserId);
                return DashboardProfileAuthenticationResult.Failure("Unable to decrypt authentication token.");
            }

            await RefreshCacheAsync(cacheKey, decrypted);
            m_Logger.LogInformation("Dashboard authentication succeeded for user {UserId}, profile {ProfileId}",
                discordUserId, profileId);
            return DashboardProfileAuthenticationResult.Success(user, profile.LtUid, decrypted);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            m_Logger.LogWarning(ex, "Dashboard authentication failed due to invalid passphrase for user {UserId}",
                discordUserId);
            return DashboardProfileAuthenticationResult.InvalidPassphrase("Incorrect passphrase. Please try again.");
        }
    }

    public Task RefreshAsync(ulong discordUserId, ulong ltUid, string ltoken, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cacheKey = CacheKeys.DashboardLToken(discordUserId, ltUid);
        m_Logger.LogDebug("Refreshing dashboard authentication cache for user {UserId}, ltuid {LtUid}",
            discordUserId, ltUid);
        return RefreshCacheAsync(cacheKey, ltoken);
    }

    private Task RefreshCacheAsync(string key, string token)
    {
        return m_CacheService.SetAsync(new CacheEntryBase<string>(key, token, CacheDuration));
    }
}

public enum DashboardAuthStatus
{
    Success,
    NotFound,
    PassphraseRequired,
    InvalidPassphrase,
    Failure
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

    public static DashboardProfileAuthenticationResult Failure(string error) =>
        new(DashboardAuthStatus.Failure, error, null, 0, null);
}
