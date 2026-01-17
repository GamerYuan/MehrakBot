using System.Security.Claims;
using Mehrak.Dashboard.Models;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Common;
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

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Hsr.Character)
            .WithParameters(parameters)
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

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Hsr.Memory)
            .WithParameters(parameters)
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

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Hsr.PureFiction)
            .WithParameters(parameters.Concat([("mode", HsrEndGameMode.PureFiction)]))
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

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Hsr.ApocalypticShadow)
            .WithParameters(parameters.Concat([("mode", HsrEndGameMode.ApocalypticShadow)]))
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

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Hsr.CharList)
            .WithParameters(parameters)
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

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Hsr.Anomaly)
            .WithParameters(parameters)
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
