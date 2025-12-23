using System;
using System.Linq;
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

    public UserController(IDashboardUserService userService)
    {
        m_UserService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var users = await m_UserService.GetDashboardUsersAsync(HttpContext.RequestAborted);
        var payload = users.Select(u => new
        {
            userId = u.UserId,
            username = u.Username,
            isSuperAdmin = u.IsSuperAdmin,
            gameWritePermissions = u.GameWritePermissions
        });

        return Ok(payload);
    }

    [HttpPost]
    public async Task<IActionResult> AddUser([FromBody] AddDashboardUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var permissions = request.GameWritePermissions?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray() ?? Array.Empty<string>();

        var result = await m_UserService.AddDashboardUserAsync(new AddDashboardUserRequestDto
        {
            Username = request.Username.Trim(),
            DiscordUserId = request.DiscordUserId,
            IsSuperAdmin = request.IsSuperAdmin,
            GameWritePermissions = permissions
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            var errorPayload = new { error = result.Error ?? "Failed to create dashboard user." };
            var isConflict = result.Error?.Contains("already", StringComparison.OrdinalIgnoreCase) == true;
            return isConflict ? Conflict(errorPayload) : BadRequest(errorPayload);
        }

        return Ok(new
        {
            userId = result.UserId,
            username = result.Username,
            temporaryPassword = result.TemporaryPassword,
            requiresPasswordReset = result.RequiresPasswordReset,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateDashboardUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var permissions = request.GameWritePermissions?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray() ?? [];

        var result = await m_UserService.UpdateDashboardUserAsync(new UpdateDashboardUserRequestDto
        {
            UserId = id,
            Username = request.Username.Trim(),
            DiscordUserId = request.DiscordUserId,
            IsSuperAdmin = request.IsSuperAdmin,
            IsActive = request.IsActive,
            GameWritePermissions = permissions
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            var errorPayload = new { error = result.Error ?? "Failed to update dashboard user." };
            var isConflict = result.Error?.Contains("already", StringComparison.OrdinalIgnoreCase) == true;
            return isConflict ? Conflict(errorPayload) : BadRequest(errorPayload);
        }

        return Ok(new
        {
            userId = result.UserId,
            username = result.Username,
            isActive = result.IsActive,
            isSuperAdmin = result.IsSuperAdmin,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [HttpPost("{id:guid}/password/require-reset")]
    public async Task<IActionResult> RequirePasswordReset(Guid id)
    {
        var result = await m_UserService.RequirePasswordResetAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded)
            return NotFound(new { error = result.Error ?? "User not found." });

        return Ok(new
        {
            requiresPasswordReset = true,
            sessionsInvalidated = result.SessionsInvalidated
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var result = await m_UserService.RemoveDashboardUserAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded)
            return NotFound(new { error = result.Error ?? "Failed to remove dashboard user." });

        return NoContent();
    }
}
