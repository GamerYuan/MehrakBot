using System.Security.Claims;
using Mehrak.Dashboard.Models;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private const string SessionTokenClaim = "dashboard_session";
    private const string PermissionClaim = "perm";
    private readonly IDashboardAuthService m_AuthService;
    private readonly ILogger<AuthController> m_Logger;
    private readonly IDashboardMetrics m_Metrics;

    public AuthController(IDashboardAuthService authService, ILogger<AuthController> logger, IDashboardMetrics metrics)
    {
        m_AuthService = authService;
        m_Logger = logger;
        m_Metrics = metrics;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        m_Logger.LogInformation("Login attempt for username {Username}", request.Username.ReplaceLineEndings("").Trim());

        var result = await m_AuthService.LoginAsync(
            new LoginRequestDto { Username = request.Username.ReplaceLineEndings("").Trim(), Password = request.Password },
            HttpContext.RequestAborted);

        if (!result.Succeeded || result.SessionToken is null)
        {
            m_Logger.LogWarning("Login failed for username {Username}: {Reason}", request.Username.ReplaceLineEndings("").Trim(),
                result.Error ?? "Unknown error");
            return Unauthorized(new { error = result.Error ?? "Invalid credentials" });
        }

        m_Logger.LogInformation("Login succeeded for user {UserId}", result.UserId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username ?? string.Empty),
            new("discord_id", result.DiscordUserId.ToString()),
            new(SessionTokenClaim, result.SessionToken)
        };

        if (result.IsSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

        if (result.IsRootUser)
            claims.Add(new Claim("is_root_user", "true"));

        foreach (var game in result.GameWritePermissions)
            claims.Add(new Claim(PermissionClaim, $"game_write:{game}"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false
            });

        m_Metrics.TrackUserLogin(result.UserId.ToString());

        return Ok(new
        {
            username = result.Username,
            discordUserId = result.DiscordUserId.ToString(),
            isSuperAdmin = result.IsSuperAdmin,
            isRootUser = result.IsRootUser,
            gameWritePermissions = result.GameWritePermissions,
            requiresPasswordReset = result.RequiresPasswordReset
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await HttpContext.SignOutAsync();
        if (!string.IsNullOrEmpty(userId))
            m_Metrics.TrackUserLogout(userId);
        return Ok();
    }

    [Authorize]
    [HttpPost("password")]
    public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = "Invalid user context." });

        var result = await m_AuthService.ChangePasswordAsync(new ChangeDashboardPasswordRequestDto
        {
            UserId = userId,
            CurrentPassword = request.CurrentPassword,
            NewPassword = request.NewPassword
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Password update failed for user {UserId}: {Reason}", userId, result.Error ?? "Unknown error");
            return BadRequest(new { error = result.Error ?? "Unable to update password." });
        }

        m_Logger.LogInformation("Password updated for user {UserId}. Sessions invalidated: {Invalidated}", userId, result.SessionsInvalidated);

        if (result.SessionsInvalidated)
        {
            await HttpContext.SignOutAsync();
            m_Metrics.TrackUserLogout(userId.ToString());
        }

        return Ok(new { requiresPasswordReset = false });
    }

    [Authorize]
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = "Invalid user context." });

        m_Logger.LogInformation("Forced password reset requested for user {UserId}", userId);

        var result = await m_AuthService.ForceResetPasswordAsync(new ForceResetDashboardPasswordRequestDto
        {
            UserId = userId,
            NewPassword = request.NewPassword
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            m_Logger.LogWarning("Forced password reset failed for user {UserId}: {Reason}", userId, result.Error ?? "Unknown error");
            return BadRequest(new { error = result.Error ?? "Unable to reset password." });
        }

        m_Logger.LogInformation("Forced password reset completed for user {UserId}", userId);

        await HttpContext.SignOutAsync();
        m_Metrics.TrackUserLogout(userId.ToString());
        return Ok(new { requiresPasswordReset = false });
    }
}
