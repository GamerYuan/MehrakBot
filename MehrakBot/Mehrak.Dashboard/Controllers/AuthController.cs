using System.Security.Claims;
using Mehrak.Dashboard.Models;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
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

    public AuthController(IDashboardAuthService authService)
    {
        m_AuthService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await m_AuthService.LoginAsync(
            new LoginRequestDto { Username = request.Username, Password = request.Password },
            HttpContext.RequestAborted);

        if (!result.Succeeded || result.SessionToken is null)
            return Unauthorized(new { error = result.Error ?? "Invalid credentials" });

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username ?? string.Empty),
            new("discord_id", result.DiscordUserId.ToString()),
            new(SessionTokenClaim, result.SessionToken)
        };

        if (result.IsSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

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

        return Ok(new
        {
            username = result.Username,
            discordUserId = result.DiscordUserId,
            isSuperAdmin = result.IsSuperAdmin,
            gameWritePermissions = result.GameWritePermissions,
            requiresPasswordReset = result.RequiresPasswordReset
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
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
            return BadRequest(new { error = result.Error ?? "Unable to update password." });

        if (result.SessionsInvalidated)
            await HttpContext.SignOutAsync();

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

        var result = await m_AuthService.ForceResetPasswordAsync(new ForceResetDashboardPasswordRequestDto
        {
            UserId = userId,
            NewPassword = request.NewPassword
        }, HttpContext.RequestAborted);

        if (!result.Succeeded)
            return BadRequest(new { error = result.Error ?? "Unable to reset password." });

        await HttpContext.SignOutAsync();
        return Ok(new { requiresPasswordReset = false });
    }
}
