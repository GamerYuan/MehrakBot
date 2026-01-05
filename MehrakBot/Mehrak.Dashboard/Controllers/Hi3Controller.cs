using System.Security.Claims;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Dashboard.Models;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("hi3")]
public sealed class Hi3Controller : ControllerBase
{
    private readonly IDashboardApplicationExecutorBuilder m_ExecutorBuilder;
    private readonly ILogger<Hi3Controller> m_Logger;

    public Hi3Controller(
        IDashboardApplicationExecutorBuilder executorBuilder,
        ILogger<Hi3Controller> logger)
    {
        m_ExecutorBuilder = executorBuilder;
        m_Logger = logger;
    }

    [HttpPost("battlesuit")]
    public async Task<IActionResult> ExecuteBattlesuit([FromBody] Hi3BattlesuitRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HI3 battlesuit command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiImpact3, server,
            ("character", request.Battlesuit.Trim()));

        var executor = m_ExecutorBuilder.For<Hi3CharacterApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new Hi3CharacterApplicationContext(discordUserId, parameters))
            .AddValidator<string>("character", value => !string.IsNullOrWhiteSpace(value))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
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

    private bool TryParseServer(string serverValue, out Hi3Server server, out IActionResult? errorResult)
    {
        errorResult = null;
        if (string.IsNullOrWhiteSpace(serverValue) || !Enum.TryParse(serverValue, true, out server))
        {
            server = default;
            errorResult = BadRequest(new { error = "Invalid server value." });
            return false;
        }

        return true;
    }

    private static List<(string, object)> BuildParameters(Game game, Hi3Server server, params (string Key, object Value)[] extras)
    {
        var parameters = new List<(string, object)> { ("game", game), ("server", server.ToString()) };
        foreach (var extra in extras)
            parameters.Add(extra);
        return parameters;
    }
}
