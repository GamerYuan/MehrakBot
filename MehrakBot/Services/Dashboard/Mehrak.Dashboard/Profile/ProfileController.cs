using System.Data.Common;
using System.Security.Claims;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure.User;
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

        var user = await m_UserContext.Users
            .Where(u => u.Id == (long)discordUserId)
            .Include(u => u.Profiles)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (user == null)
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
            return BadRequest(new { error = "You can only have 10 profiles." });

        if (user.Profiles.Any(x => x.LtUid == (long)request.LtUid))
            return Conflict(new { error = "A profile with this HoYoLAB UID already exists." });

        var hadProfiles = user.Profiles.Count > 0;

        // Validate cookie and fetch all game profiles before saving
        var gameProfilesResult = await m_GameRoleApi.GetAllGameProfilesAsync(
            discordUserId, request.LtUid, request.LToken, HttpContext.RequestAborted);

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
            ProfileId = user.Profiles.Count + 1,
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

        user.Profiles.Add(profile);

        try
        {
            await m_UserContext.SaveChangesAsync(HttpContext.RequestAborted);
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

        if (!hadProfiles) await m_UserTracker.AdjustUserCountAsync(1);

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

        var newLToken = await Task.Run(
            () => m_EncryptionService.Encrypt(request.LToken, request.Passphrase),
            HttpContext.RequestAborted);

        try
        {
            await m_UserContext.UserProfiles
                .Where(p => p.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LToken, newLToken), HttpContext.RequestAborted);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update profile {ProfileId} for user {UserId}", profileId, discordUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to update profile. Please try again later." });
        }

        try
        {
            await m_CacheService.RemoveAsync(CacheKeys.DashboardLToken(discordUserId, (ulong)profile.LtUid), HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            m_Logger.LogWarning(e, "Failed to remove cache for profile {ProfileId} for user {UserId}", profileId, discordUserId);
        }

        return Ok(new { message = "Profile updated successfully." });
    }

    [HttpDelete("{profileId:int}")]
    public async Task<IActionResult> DeleteProfile(int profileId)
    {
        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Deleting profile {ProfileId} for user {UserId}", profileId, discordUserId);

        var profiles = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)discordUserId)
            .OrderBy(p => p.ProfileId)
            .ToListAsync(HttpContext.RequestAborted);

        if (profiles.Count == 0)
            return NotFound(new { error = "No profiles found." });

        var profile = profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (profile == null)
            return NotFound(new { error = $"No profile with ID {profileId} found." });

        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            if (profiles[i].ProfileId == profile.ProfileId)
            {
                m_UserContext.UserProfiles.Remove(profiles[i]);
                profiles.RemoveAt(i);
            }
            else if (profiles[i].ProfileId > profile.ProfileId) profiles[i].ProfileId--;
        }

        try
        {
            await using var transaction = await m_UserContext.Database.BeginTransactionAsync(HttpContext.RequestAborted);

            if (profiles.Count > 0)
                m_UserContext.UserProfiles.UpdateRange(profiles);

            await m_UserContext.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete profile {ProfileId} for user {UserId}", profileId, discordUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete profile. Please try again later." });
        }

        try
        {
            await m_CacheService.RemoveAsync(CacheKeys.DashboardLToken(discordUserId, (ulong)profile.LtUid), HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            m_Logger.LogWarning(e, "Failed to remove cache for profile {ProfileId} for user {UserId}", profileId, discordUserId);
        }

        if (profiles.Count == 0)
            await m_UserTracker.AdjustUserCountAsync(-1);

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAllProfiles()
    {
        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Deleting all profiles for user {UserId}", discordUserId);

        var deleted = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)discordUserId)
            .ExecuteDeleteAsync(HttpContext.RequestAborted);

        if (deleted > 0)
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

    private static bool IsUniqueConstraintViolation(DbUpdateException e)
    {
        return e.InnerException is DbException dbEx
            && dbEx.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
