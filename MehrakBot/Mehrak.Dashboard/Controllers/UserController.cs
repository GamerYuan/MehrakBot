using System.Globalization;
using Mehrak.Dashboard.Models;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize(Policy = "RequireSuperAdmin")]
[Route("users")]
public class UserController : ControllerBase
{
    private readonly IDashboardUserService m_UserService;
    private readonly ILogger<UserController> m_Logger;

    public UserController(IDashboardUserService userService, ILogger<UserController> logger)
    {
        m_UserService = userService;
        m_Logger = logger;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetUsers()
    {
        m_Logger.LogInformation("Listing dashboard users");
        var users = await m_UserService.GetDashboardUsersAsync(HttpContext.RequestAborted);
        var payload = users.Select(u => new
        {
            userId = u.UserId,
            username = u.Username,
            discordUserId = u.DiscordUserId,
            isSuperAdmin = u.IsSuperAdmin,
            isRootUser = u.IsRootUser,
            gameWritePermissions = u.GameWritePermissions
        });

        return Ok(payload);
    }

    [HttpPost("add")]
    public async Task<IActionResult> AddUser([FromBody] AddDashboardUserRequest request)
    {
        request.Username = request.Username.ReplaceLineEndings("").Trim();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseDiscordUserId(request.DiscordUserId, out var discordUserId, out var parseError))
            return parseError!;

        m_Logger.LogInformation("Creating dashboard user {Username}", request.Username.ReplaceLineEndings("").Trim());

        var permissions = request.GameWritePermissions?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray() ?? Array.Empty<string>();

        var result = await m_UserService.AddDashboardUserAsync(new AddDashboardUserRequestDto
        {
            Username = request.Username.Trim(),
            DiscordUserId = discordUserId,
            IsSuperAdmin = request.IsSuperAdmin,
            GameWritePermissions = permissions
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to create dashboard user {Username}: {Reason}", request.Username.ReplaceLineEndings("").Trim(),
                result.Error ?? "Unknown error");
            var errorPayload = new { error = result.Error ?? "Failed to create dashboard user." };
            var isConflict = result.Error?.Contains("already", StringComparison.OrdinalIgnoreCase) == true;
            return isConflict ? Conflict(errorPayload) : BadRequest(errorPayload);
        }

        m_Logger.LogInformation("Created dashboard user {UserId}", result.UserId);

        return Ok(new
        {
            userId = result.UserId,
            username = result.Username,
            temporaryPassword = result.TemporaryPassword,
            requiresPasswordReset = result.RequiresPasswordReset,
            isRootUser = false,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateDashboardUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseDiscordUserId(request.DiscordUserId, out var discordUserId, out var parseError))
            return parseError!;

        m_Logger.LogInformation("Updating dashboard user {UserId}", id);

        var permissions = request.GameWritePermissions?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray() ?? [];

        var result = await m_UserService.UpdateDashboardUserAsync(new UpdateDashboardUserRequestDto
        {
            UserId = id,
            Username = request.Username.Trim(),
            DiscordUserId = discordUserId,
            IsSuperAdmin = request.IsSuperAdmin,
            IsActive = request.IsActive,
            GameWritePermissions = permissions
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to update dashboard user {UserId}: {Reason}", id, result.Error ?? "Unknown error");
            var errorPayload = new { error = result.Error ?? "Failed to update dashboard user." };
            var isConflict = result.Error?.Contains("already", StringComparison.OrdinalIgnoreCase) == true;
            return isConflict ? Conflict(errorPayload) : BadRequest(errorPayload);
        }

        m_Logger.LogInformation("Updated dashboard user {UserId}", id);

        return Ok(new
        {
            userId = result.UserId,
            username = result.Username,
            isActive = result.IsActive,
            isSuperAdmin = result.IsSuperAdmin,
            isRootUser = result.IsRootUser,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [HttpPost("{id:guid}/password/require-reset")]
    public async Task<IActionResult> RequirePasswordReset(Guid id)
    {
        m_Logger.LogInformation("Forcing password reset requirement for user {UserId}", id);

        var result = await m_UserService.RequirePasswordResetAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to set password reset requirement for user {UserId}: {Reason}", id, result.Error ?? "Unknown error");
            return NotFound(new { error = result.Error ?? "User not found." });
        }

        return Ok(new
        {
            requiresPasswordReset = true,
            sessionsInvalidated = result.SessionsInvalidated
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        m_Logger.LogInformation("Deleting dashboard user {UserId}", id);

        var result = await m_UserService.RemoveDashboardUserAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Failed to delete dashboard user {UserId}: {Reason}", id, result.Error ?? "Unknown error");
            return NotFound(new { error = result.Error ?? "Failed to remove dashboard user." });
        }

        m_Logger.LogInformation("Deleted dashboard user {UserId}", id);
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
}
