using System.Security.Claims;
using Mehrak.Dashboard.Auth;
using Mehrak.Dashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("profile-auth")]
public sealed class ProfileAuthenticationController : ControllerBase
{
    private readonly IDashboardProfileAuthenticationService m_ProfileAuthService;
    private readonly ILogger<ProfileAuthenticationController> m_Logger;

    public ProfileAuthenticationController(
        IDashboardProfileAuthenticationService profileAuthService,
        ILogger<ProfileAuthenticationController> logger)
    {
        m_ProfileAuthService = profileAuthService;
        m_Logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Authenticate([FromBody] ProfileAuthenticationRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;

        m_Logger.LogInformation("Authenticating profile {ProfileId} for user {UserId}", request.ProfileId, discordUserId);

        var result = await m_ProfileAuthService.AuthenticateAsync(
            discordUserId,
            request.ProfileId,
            request.Passphrase,
            HttpContext.RequestAborted);

        return result.Status switch
        {
            DashboardAuthStatus.Success => Ok(new
            {
                message = "Authentication successful.",
                ltUid = result.LtUid
            }),
            DashboardAuthStatus.NotFound => NotFound(new { error = result.Error ?? "Profile not found." }),
            DashboardAuthStatus.InvalidPassphrase => BadRequest(new { error = result.Error ?? "Invalid passphrase." }),
            DashboardAuthStatus.PassphraseRequired => BadRequest(new { error = "Passphrase is required." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                new { error = result.Error ?? "Unable to authenticate profile." })
        };
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
}
