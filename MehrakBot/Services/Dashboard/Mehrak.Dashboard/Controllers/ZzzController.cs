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
[Route("zzz")]
public sealed class ZzzController : ControllerBase
{
    private readonly IDashboardApplicationExecutorBuilder m_ExecutorBuilder;
    private readonly ILogger<ZzzController> m_Logger;

    public ZzzController(
        IDashboardApplicationExecutorBuilder executorBuilder,
        ILogger<ZzzController> logger)
    {
        m_ExecutorBuilder = executorBuilder;
        m_Logger = logger;
    }

    [HttpPost("character")]
    public async Task<IActionResult> ExecuteCharacter([FromBody] ZzzCharacterRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing ZZZ character command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.ZenlessZoneZero, server,
            (nameof(request.Character).ToLowerInvariant(), request.Character.Trim()));

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Zzz.Character)
            .WithParameters(parameters)
            .AddValidator<string>(nameof(request.Character).ToLowerInvariant(), value => !string.IsNullOrWhiteSpace(value))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("shiyu")]
    public async Task<IActionResult> ExecuteShiyu([FromBody] ZzzSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing ZZZ shiyu defense command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.ZenlessZoneZero, server);

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Zzz.Defense)
            .WithParameters(parameters)
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("da")]
    public async Task<IActionResult> ExecuteDeadlyAssault([FromBody] ZzzSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing ZZZ deadly assault command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.ZenlessZoneZero, server);

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Zzz.Assault)
            .WithParameters(parameters)
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("tower")]
    public async Task<IActionResult> ExecuteTower([FromBody] ZzzSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing ZZZ tower command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.ZenlessZoneZero, server);

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Zzz.Tower)
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
