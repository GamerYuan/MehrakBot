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

    [Authorize(Policy = "RequireSuperAdmin")]
    [HttpPost("add")]
    public async Task<IActionResult> AddUser([FromBody] AddDashboardUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var permissions = request.GameWritePermissions?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray() ?? Array.Empty<string>();

        var result = await m_AuthService.AddDashboardUserAsync(new AddDashboardUserRequestDto
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
}
