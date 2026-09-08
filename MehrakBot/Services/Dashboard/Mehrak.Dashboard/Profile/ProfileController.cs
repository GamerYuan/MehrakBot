using System.Data.Common;
using System.Security.Claims;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Extensions;
using Mehrak.Infrastructure.User.Models;
using Mehrak.Infrastructure.User.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Profile;

[ApiController]
[Authorize]
[Route("profiles")]
public sealed class ProfileController : ControllerBase
{
    private readonly UserDbContext m_UserContext;
    private readonly IEncryptionService m_EncryptionService;
    private readonly ICacheService m_CacheService;
    private readonly UserCountTrackerService m_UserTracker;
    private readonly GameRoleApiService m_GameRoleApi;
    private readonly ILogger<ProfileController> m_Logger;

    public ProfileController(
        UserDbContext userContext,
        IEncryptionService encryptionService,
        ICacheService cacheService,
        UserCountTrackerService userTracker,
        GameRoleApiService gameRoleApi,
        ILogger<ProfileController> logger)
    {
        m_UserContext = userContext;
        m_EncryptionService = encryptionService;
        m_CacheService = cacheService;
        m_UserTracker = userTracker;
        m_GameRoleApi = gameRoleApi;
        m_Logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> ListProfiles()
    {
        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Listing profiles for user {UserId}", discordUserId);

        var user = await m_UserContext.Users
            .AsNoTracking()
            .Where(u => u.Id == (long)discordUserId)
            .Include(u => u.Profiles)
                .ThenInclude(p => p.GameUids.OrderBy(g => g.Game).ThenBy(g => g.GameUid))
            .Include(u => u.Profiles)
                .ThenInclude(p => p.LastUsedRegions)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (user?.Profiles == null || user.Profiles.Count == 0)
            return Ok(Array.Empty<object>());

        var profiles = user.Profiles
            .OrderBy(p => p.ProfileId)
            .Select(p => new
            {
                profileId = p.ProfileId,
                ltUid = (ulong)p.LtUid,
                gameUids = p.GameUids
                    .GroupBy(g => g.Game)
                    .ToDictionary(
                        g => g.Key.ToString(),
                        g => g.ToDictionary(x => x.Region, x => x.GameUid)),
                lastUsedRegions = p.LastUsedRegions
                    .DistinctBy(r => r.Game)
                    .ToDictionary(r => r.Game.ToString(), r => r.Region)
            });

        return Ok(profiles);
    }

    [HttpPost]
    public async Task<IActionResult> AddProfile([FromBody] AddProfileRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Adding profile for user {UserId}, LtUid {LtUid}", discordUserId, request.LtUid);

        // Validate cookie and fetch all game profiles before saving
        var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
            discordUserId, request.LtUid, request.LToken, HttpContext.RequestAborted, bypassCache: true);

        if (!gameProfilesResult.IsSuccess)
        {
            if (gameProfilesResult.StatusCode == Domain.Shared.Models.StatusCode.Unauthorized)
            {
                m_Logger.LogWarning("User {UserId} provided invalid cookies for UID {LtUid}", discordUserId, request.LtUid);
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Invalid HoYoLAB UID or Cookies. Please check your credentials and try again." });
            }

            m_Logger.LogWarning("Failed to validate profile for user {UserId}: {Error}", discordUserId, gameProfilesResult.ErrorMessage);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Failed to validate profile with HoYoLAB. Please try again later." });
        }

        if (gameProfilesResult.Data.Count == 0)
        {
            m_Logger.LogWarning("No supported game profiles found for user {UserId}, LtUid {LtUid}", discordUserId, request.LtUid);
            return BadRequest(new { error = "No supported game profiles were found for this HoYoLAB account." });
        }

        string encryptedLToken;
        try
        {
            encryptedLToken = await Task.Run(
                () => m_EncryptionService.Encrypt(request.LToken, request.Passphrase),
                HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to encrypt LToken for user {UserId}", discordUserId);
            return BadRequest(new { error = "Invalid LToken or passphrase." });
        }

        UserProfileModel profile = new()
        {
            UserId = (long)discordUserId,
            LtUid = (long)request.LtUid,
            LToken = encryptedLToken
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

        ProfileAddResult addResult;
        try
        {
            addResult = await m_UserContext.ExecuteUserProfileMutationAsync(
                (long)discordUserId,
                async () =>
                {
                    // Any pre-lock state is only a request snapshot. Profile
                    // limits, uniqueness and the next ID must be decided from
                    // the rows visible after the per-user lock is held.
                    m_UserContext.ChangeTracker.Clear();

                    var user = await m_UserContext.Users
                        .Where(u => u.Id == (long)discordUserId)
                        .Include(u => u.Profiles)
                        .FirstOrDefaultAsync(HttpContext.RequestAborted);

                    if (user is null)
                    {
                        user = new UserModel
                        {
                            Id = (long)discordUserId,
                            Timestamp = DateTime.UtcNow,
                            Profiles = []
                        };
                        await m_UserContext.Users.AddAsync(user, HttpContext.RequestAborted);
                    }

                    if (user.Profiles.Count >= 10)
                        return new ProfileAddResult(ProfileAddStatus.TooMany, false);

                    if (user.Profiles.Any(existing => existing.LtUid == (long)request.LtUid))
                        return new ProfileAddResult(ProfileAddStatus.Duplicate, false);

                    var hadProfiles = user.Profiles.Count > 0;
                    profile.ProfileId = user.Profiles.Count + 1;
                    user.Profiles.Add(profile);
                    await m_UserContext.SaveChangesAsync(HttpContext.RequestAborted);
                    return new ProfileAddResult(ProfileAddStatus.Added, hadProfiles);
                },
                HttpContext.RequestAborted);
        }
        catch (DbUpdateException e) when (IsUniqueConstraintViolation(e))
        {
            m_Logger.LogWarning(e, "Duplicate profile for user {UserId}, LtUid {LtUid}", discordUserId, request.LtUid);
            return Conflict(new { error = "A profile with this HoYoLAB UID already exists." });
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to add profile for user {UserId}", discordUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to add profile. Please try again later." });
        }

        if (addResult.Status == ProfileAddStatus.TooMany)
            return BadRequest(new { error = "You can only have 10 profiles." });

        if (addResult.Status == ProfileAddStatus.Duplicate)
            return Conflict(new { error = "A profile with this HoYoLAB UID already exists." });

        if (!addResult.HadProfiles) await m_UserTracker.AdjustUserCountAsync(1);

        m_Logger.LogInformation("User {UserId} added new profile with {Count} game profiles", discordUserId, gameProfilesResult.Data.Count);

        return CreatedAtAction(nameof(ListProfiles), new
        {
            profileId = profile.ProfileId,
            ltUid = request.LtUid,
            gameProfileCount = gameProfilesResult.Data.Count
        });
    }

    [HttpPut("{profileId:int}")]
    public async Task<IActionResult> UpdateProfile(int profileId, [FromBody] UpdateProfileRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Updating profile {ProfileId} for user {UserId}", profileId, discordUserId);

        var profile = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)discordUserId && p.ProfileId == profileId)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (profile == null)
            return NotFound(new { error = $"No profile with ID {profileId} found." });

        // Validate the new cookie against HoYoLAB before saving (bypass cache to always hit upstream)
        var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
            discordUserId, (ulong)profile.LtUid, request.LToken, HttpContext.RequestAborted, bypassCache: true);

        if (!gameProfilesResult.IsSuccess)
        {
            if (gameProfilesResult.StatusCode == Domain.Shared.Models.StatusCode.Unauthorized)
            {
                m_Logger.LogWarning("User {UserId} provided invalid cookies for UID {LtUid} during update", discordUserId, profile.LtUid);
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Invalid HoYoLAB UID or Cookies. Please check your credentials and try again." });
            }

            m_Logger.LogWarning("Failed to validate profile for user {UserId} during update: {Error}", discordUserId, gameProfilesResult.ErrorMessage);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Failed to validate profile with HoYoLAB. Please try again later." });
        }

        if (gameProfilesResult.Data.Count == 0)
        {
            m_Logger.LogWarning("No supported game profiles found for user {UserId}, LtUid {LtUid} during update", discordUserId, profile.LtUid);
            return BadRequest(new { error = "No supported game profiles were found for this HoYoLAB account." });
        }

        var newLToken = await Task.Run(
            () => m_EncryptionService.Encrypt(request.LToken, request.Passphrase),
            HttpContext.RequestAborted);

        // Load-then-save (no ExecuteUpdateAsync) so rotation stays testable on
        // providers without bulk-update support.
        profile.LToken = newLToken;
        try
        {
            await m_UserContext.SaveChangesAsync(HttpContext.RequestAborted);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update profile {ProfileId} for user {UserId}", profileId, discordUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to update profile. Please try again later." });
        }

        // Rotation revokes both client caches. The stored credential revision also changes, so any surviving stale
        // entry is dropped on its next read.
        await RevokeProfileCachesAsync(discordUserId, (ulong)profile.LtUid, profileId);

        return Ok(new { message = "Profile updated successfully." });
    }

    [HttpDelete("{profileId:int}")]
    public async Task<IActionResult> DeleteProfile(int profileId)
    {
        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Deleting profile {ProfileId} for user {UserId}", profileId, discordUserId);

        var profiles = await m_UserContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == (long)discordUserId)
            .OrderBy(p => p.ProfileId)
            .ToListAsync(HttpContext.RequestAborted);

        if (profiles.Count == 0)
            return NotFound(new { error = "No profiles found." });

        var profile = profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (profile == null)
            return NotFound(new { error = $"No profile with ID {profileId} found." });

        try
        {
            var deletion = await m_UserContext.DeleteAndReindexProfilesAsync(
                profile, HttpContext.RequestAborted);
            if (deletion is null)
                return NotFound(new { error = $"No profile with ID {profileId} found." });

            // Deletion revokes both client caches.
            await RevokeProfileCachesAsync(discordUserId, (ulong)deletion.LtUid, deletion.ProfileId);

            if (deletion.RemainingProfileCount == 0)
                await m_UserTracker.AdjustUserCountAsync(-1);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete profile {ProfileId} for user {UserId}", profileId, discordUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete profile. Please try again later." });
        }

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAllProfiles()
    {
        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Deleting all profiles for user {UserId}", discordUserId);

        // Load-then-remove (no ExecuteDeleteAsync): providers without
        // bulk-delete support must still clear every profile, and the LtUids
        // are needed to revoke every credential cache entry below.
        List<UserProfileModel> allProfiles;
        try
        {
            allProfiles = await m_UserContext.ExecuteUserProfileMutationAsync(
                (long)discordUserId,
                async () =>
                {
                    // The snapshot and delete must be inside the same
                    // per-user mutation lock as profile adds and reindexing.
                    m_UserContext.ChangeTracker.Clear();
                    var currentProfiles = await m_UserContext.UserProfiles
                        .Where(p => p.UserId == (long)discordUserId)
                        .ToListAsync(HttpContext.RequestAborted);

                    if (currentProfiles.Count > 0)
                    {
                        m_UserContext.UserProfiles.RemoveRange(currentProfiles);
                        await m_UserContext.SaveChangesAsync(HttpContext.RequestAborted);
                    }

                    return currentProfiles;
                },
                HttpContext.RequestAborted);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete all profiles for user {UserId}", discordUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete profiles. Please try again later." });
        }

        if (allProfiles.Count == 0)
            return NoContent();

        // Deleting every profile revokes both client caches for every profile.
        foreach (var existing in allProfiles)
            await RevokeProfileCachesAsync(discordUserId, (ulong)existing.LtUid, existing.ProfileId);

        await m_UserTracker.AdjustUserCountAsync(-1);

        return NoContent();
    }

    private bool TryGetDiscordUserId(out ulong discordUserId, out IActionResult? errorResult)
    {
        discordUserId = 0;
        errorResult = null;

        var claimValue = User.FindFirstValue("discord_id");
        if (!ulong.TryParse(claimValue, out discordUserId))
        {
            errorResult = Unauthorized(new { error = "Discord account information is missing from the current session." });
            return false;
        }

        return true;
    }

    private async Task RevokeProfileCachesAsync(ulong discordUserId, ulong ltUid, int profileId)
    {
        try
        {
            await m_CacheService.RemoveAsync(CacheKeys.BotLToken(discordUserId, ltUid), HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            m_Logger.LogWarning(e, "Failed to remove bot cache for profile {ProfileId} for user {UserId}", profileId, discordUserId);
        }

        try
        {
            await m_CacheService.RemoveAsync(CacheKeys.DashboardLToken(discordUserId, ltUid), HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            m_Logger.LogWarning(e, "Failed to remove dashboard cache for profile {ProfileId} for user {UserId}", profileId, discordUserId);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException e)
    {
        return e.InnerException is DbException dbEx
            && dbEx.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private enum ProfileAddStatus
    {
        Added,
        TooMany,
        Duplicate
    }

    private sealed record ProfileAddResult(ProfileAddStatus Status, bool HadProfiles);
}


