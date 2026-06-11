using System.Security.Claims;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Auth;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private const string SessionTokenClaim = "dashboard_session";
    private const string PermissionClaim = "perm";
    private const string DiscordScheme = "Discord";
    private readonly IDashboardAuthService m_AuthService;
    private readonly ILogger<AuthController> m_Logger;

    public AuthController(IDashboardAuthService authService, ILogger<AuthController> logger)
    {
        m_AuthService = authService;
        m_Logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("discord")]
    public IActionResult Discord()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(DiscordCallback))
        };
        return Challenge(properties, DiscordScheme);
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> DiscordCallback()
    {
        var authenticateResult = await HttpContext.AuthenticateAsync(DiscordScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            m_Logger.LogWarning("Discord authentication failed");
            return Unauthorized(new { error = "Discord authentication failed." });
        }

        var discordIdClaim = authenticateResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(discordIdClaim) || !long.TryParse(discordIdClaim, out var discordId))
        {
            m_Logger.LogWarning("Discord authentication succeeded but no valid Discord ID found");
            return Unauthorized(new { error = "Could not determine Discord user ID." });
        }

        m_Logger.LogInformation("Discord login attempt for DiscordId {DiscordId}", discordId);

        var discordUsername = authenticateResult.Principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var result = await m_AuthService.LoginByDiscordAsync(discordId, discordUsername, HttpContext.RequestAborted);

        if (!result.Succeeded || result.SessionToken is null)
        {
            m_Logger.LogWarning("Discord login failed for DiscordId {DiscordId}: {Reason}", discordId, result.Error ?? "Unknown error");
            return Unauthorized(new { error = result.Error ?? "Login failed." });
        }

        m_Logger.LogInformation("Discord login succeeded for user {UserId}", result.UserId);

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
            claims.Add(new Claim(ClaimTypes.Role, "rootuser"));

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
            discordUserId = result.DiscordUserId.ToString(),
            isSuperAdmin = result.IsSuperAdmin,
            isRootUser = result.IsRootUser,
            gameWritePermissions = result.GameWritePermissions
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return Ok();
    }
}
