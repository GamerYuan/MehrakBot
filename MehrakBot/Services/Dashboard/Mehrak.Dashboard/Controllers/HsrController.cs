using System.Security.Claims;
using Mehrak.Application.Services.Hsr.Anomaly;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Dashboard.Models;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("hsr")]
public sealed class HsrController : ControllerBase
{
    private readonly IDashboardApplicationExecutorBuilder m_ExecutorBuilder;
    private readonly ILogger<HsrController> m_Logger;

    public HsrController(
        IDashboardApplicationExecutorBuilder executorBuilder,
        ILogger<HsrController> logger)
    {
        m_ExecutorBuilder = executorBuilder;
        m_Logger = logger;
    }

    [HttpPost("character")]
    public async Task<IActionResult> ExecuteCharacter([FromBody] HsrCharacterRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HSR character command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiStarRail, server,
            (nameof(request.Character).ToLowerInvariant(), request.Character.Trim()));

        var executor = m_ExecutorBuilder.For<HsrCharacterApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new HsrCharacterApplicationContext(discordUserId, parameters))
            .AddValidator<string>(nameof(request.Character).ToLowerInvariant(), value => !string.IsNullOrWhiteSpace(value))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("moc")]
    public async Task<IActionResult> ExecuteMemory([FromBody] HsrSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HSR Memory of Chaos command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiStarRail, server);

        var executor = m_ExecutorBuilder.For<HsrMemoryApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new HsrMemoryApplicationContext(discordUserId, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("pf")]
    public async Task<IActionResult> ExecutePureFiction([FromBody] HsrSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HSR Pure Fiction command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiStarRail, server);

        var executor = m_ExecutorBuilder.For<HsrEndGameApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new HsrEndGameApplicationContext(discordUserId, HsrEndGameMode.PureFiction, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("as")]
    public async Task<IActionResult> ExecuteApocalypticShadow([FromBody] HsrSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HSR Apocalyptic Shadow command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiStarRail, server);

        var executor = m_ExecutorBuilder.For<HsrEndGameApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new HsrEndGameApplicationContext(discordUserId, HsrEndGameMode.ApocalypticShadow, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("charlist")]
    public async Task<IActionResult> ExecuteCharList([FromBody] HsrSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HSR character list command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiStarRail, server);

        var executor = m_ExecutorBuilder.For<HsrCharListApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new HsrCharListApplicationContext(discordUserId, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("aa")]
    public async Task<IActionResult> ExecuteAnomaly([FromBody] HsrSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing HSR Anomaly Arbitration command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.HonkaiStarRail, server);

        var executor = m_ExecutorBuilder.For<HsrAnomalyApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new HsrAnomalyApplicationContext(discordUserId, parameters))
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

    private bool TryParseServer(string serverValue, out Server server, out IActionResult? errorResult)
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

    private static List<(string, object)> BuildParameters(Game game, Server server, params (string Key, object Value)[] extras)
    {
        var parameters = new List<(string, object)> { ("game", game), ("server", server.ToString()) };
        foreach (var extra in extras)
            parameters.Add(extra);
        return parameters;
    }
}
