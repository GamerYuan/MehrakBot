using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using Mehrak.Dashboard.User.Models;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Mehrak.Dashboard.User;

[ApiController]
[Route("users")]
public class UserController : ControllerBase
{
    private const string SessionTokenClaim = "dashboard_session";
    private readonly IDashboardUserService m_UserService;
    private readonly IDashboardAuthService m_AuthService;
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<UserController> m_Logger;

    public UserController(
        IDashboardUserService userService,
        IDashboardAuthService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<UserController> logger)
    {
        m_UserService = userService;
        m_AuthService = authService;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    [Authorize(Policy = "RequireSuperAdmin")]
    [HttpGet("list")]
    public async Task<IActionResult> GetUsers()
    {
        m_Logger.LogInformation("Listing dashboard users");
        var users = await m_UserService.GetDashboardUsersAsync(HttpContext.RequestAborted);
        var payload = users.Select(u => new
        {
            discordUserId = u.DiscordUserId,
            isSuperAdmin = u.IsSuperAdmin,
            isRootUser = u.IsRootUser,
            gameWritePermissions = u.GameWritePermissions
        });

        return Ok(payload);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var discordIdClaim = User.FindFirstValue("discord_id");
        if (string.IsNullOrWhiteSpace(discordIdClaim) || !long.TryParse(discordIdClaim, out var discordUserId))
            return Unauthorized(new { error = "Invalid user token." });

        var sessionToken = User.FindFirstValue(SessionTokenClaim);
        if (string.IsNullOrWhiteSpace(sessionToken))
            return Unauthorized(new { error = "Invalid session." });

        m_Logger.LogInformation("Getting current dashboard user DiscordId {DiscordUserId}", discordUserId);
        var user = await m_UserService.GetDashboardUserByDiscordIdAsync(discordUserId, HttpContext.RequestAborted);

        string? avatarUrl = null;
        string? username = null;
        var accessToken = await m_AuthService.GetAccessTokenAsync(sessionToken, HttpContext.RequestAborted);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                var httpClient = m_HttpClientFactory.CreateClient("Default");
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var discordUser = await httpClient.GetFromJsonAsync<DiscordUserResponse>("https://discord.com/api/v10/users/@me", HttpContext.RequestAborted);
                if (discordUser?.Username != null)
                    username = discordUser.Username;
                if (discordUser?.Avatar != null)
                {
                    var extension = discordUser.Avatar.StartsWith("a_") ? "gif" : "png";
                    avatarUrl = $"https://cdn.discordapp.com/avatars/{discordUser.Id}/{discordUser.Avatar}.{extension}";
                }
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Failed to fetch Discord user info for user {DiscordUserId}", discordUserId);
            }
        }

        return Ok(new
        {
            username,
            discordUserId = discordUserId.ToString(),
            avatarUrl,
            isSuperAdmin = user?.IsSuperAdmin ?? false,
            isRootUser = user?.IsRootUser ?? false,
            gameWritePermissions = user?.GameWritePermissions ?? []
        });
    }

    [Authorize(Policy = "RequireSuperAdmin")]
    [HttpPost("add")]
    public async Task<IActionResult> AddUser([FromBody] AddDashboardUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseDiscordUserId(request.DiscordUserId, out var discordUserId, out var parseError))
            return parseError!;

        var gamePermissions = ParseGamePermissions(request.GameWritePermissions, ModelState);
        if (gamePermissions == null)
            return ValidationProblem(ModelState);

        m_Logger.LogInformation("Creating dashboard user for DiscordId {DiscordUserId}", discordUserId);

        var result = await m_UserService.AddDashboardUserAsync(new AddDashboardUserRequestDto
        {
            DiscordUserId = discordUserId,
            GameWritePermissions = gamePermissions
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to create dashboard user for DiscordId {DiscordUserId}: {Reason}", discordUserId,
                result.Error ?? "Unknown error");
            var errorPayload = new { error = result.Error ?? "Failed to create dashboard user." };
            var isConflict = result.Error?.Contains("already", StringComparison.OrdinalIgnoreCase) == true;
            return isConflict ? Conflict(errorPayload) : BadRequest(errorPayload);
        }

        m_Logger.LogInformation("Created dashboard user for DiscordId {DiscordUserId}", discordUserId);

        return Ok(new
        {
            discordUserId = result.DiscordUserId.ToString(),
            isRootUser = result.IsRootUser,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [Authorize(Policy = "RequireSuperAdmin")]
    [HttpPut("{discordUserId}")]
    public async Task<IActionResult> UpdateUser(string discordUserId, [FromBody] UpdateDashboardUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseDiscordUserId(discordUserId, out var parsedDiscordUserId, out var parseError))
            return parseError!;

        var gamePermissions = ParseGamePermissions(request.GameWritePermissions, ModelState);
        if (gamePermissions == null)
            return ValidationProblem(ModelState);

        m_Logger.LogInformation("Updating dashboard user DiscordId {DiscordUserId}", parsedDiscordUserId);

        var user = await m_UserService.GetDashboardUserByDiscordIdAsync(parsedDiscordUserId, HttpContext.RequestAborted);
        if (user == null)
        {
            m_Logger.LogWarning("Dashboard user DiscordId {DiscordUserId} not found for update", parsedDiscordUserId);
            return NotFound(new { error = "User not found." });
        }

        if (user.IsSuperAdmin != request.IsSuperAdmin && !User.IsInRole("rootuser"))
        {
            m_Logger.LogWarning("Non-root user attempted to change super admin status of DiscordId {DiscordUserId}", parsedDiscordUserId);
            return Forbid();
        }

        var result = await m_UserService.UpdateDashboardUserByDiscordIdAsync(new UpdateDashboardUserRequestDto
        {
            DiscordUserId = parsedDiscordUserId,
            IsSuperAdmin = request.IsSuperAdmin,
            GameWritePermissions = gamePermissions
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to update dashboard user DiscordId {DiscordUserId}: {Reason}", parsedDiscordUserId, result.Error ?? "Unknown error");
            var errorPayload = new { error = result.Error ?? "Failed to update dashboard user." };
            var isConflict = result.Error?.Contains("already", StringComparison.OrdinalIgnoreCase) == true;
            return isConflict ? Conflict(errorPayload) : BadRequest(errorPayload);
        }

        m_Logger.LogInformation("Updated dashboard user DiscordId {DiscordUserId}", parsedDiscordUserId);

        return Ok(new
        {
            isSuperAdmin = result.IsSuperAdmin,
            isRootUser = result.IsRootUser,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [Authorize(Policy = "RequireSuperAdmin")]
    [HttpDelete("{discordUserId}")]
    public async Task<IActionResult> DeleteUser(string discordUserId)
    {
        if (!TryParseDiscordUserId(discordUserId, out var parsedDiscordUserId, out var parseError))
            return parseError!;

        m_Logger.LogInformation("Deleting dashboard user DiscordId {DiscordUserId}", parsedDiscordUserId);

        var toDelete = await m_UserService.GetDashboardUserByDiscordIdAsync(parsedDiscordUserId, HttpContext.RequestAborted);
        if ((toDelete?.IsSuperAdmin ?? false) && !User.IsInRole("rootuser"))
        {
            m_Logger.LogWarning("Non-root user attempted to delete super admin user DiscordId {DiscordUserId}", parsedDiscordUserId);
            return Forbid();
        }

        var result = await m_UserService.RemoveDashboardUserByDiscordIdAsync(parsedDiscordUserId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to delete dashboard user DiscordId {DiscordUserId}: {Reason}", parsedDiscordUserId, result.Error ?? "Unknown error");
            return NotFound(new { error = "Failed to remove dashboard user." });
        }

        m_Logger.LogInformation("Deleted dashboard user DiscordId {DiscordUserId}", parsedDiscordUserId);
        return NoContent();
    }

    private bool TryParseDiscordUserId(string discordUserIdValue, out long discordUserId, out IActionResult? errorResult)
    {
        errorResult = null;
        discordUserId = 0;

        if (string.IsNullOrWhiteSpace(discordUserIdValue) ||
            !long.TryParse(discordUserIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out discordUserId) ||
            discordUserId <= 0)
        {
            ModelState.AddModelError("DiscordUserId", "DiscordUserId must be a valid numeric string.");
            errorResult = ValidationProblem(ModelState);
            return false;
        }

        return true;
    }

    private IReadOnlyCollection<Game>? ParseGamePermissions(IEnumerable<string>? rawPermissions, ModelStateDictionary modelState)
    {
        var result = new List<Game>();

        foreach (var p in rawPermissions ?? [])
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;

            if (!Enum.TryParse<Game>(p.Trim(), true, out var game) || game == Game.Unsupported)
            {
                modelState.AddModelError("GameWritePermissions", $"'{p}' is not a valid game name.");
                return null;
            }

            result.Add(game);
        }

        return result;
    }

    private sealed class DiscordUserResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public string? Username { get; set; }
    }
}
