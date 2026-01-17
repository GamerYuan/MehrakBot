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
[Route("genshin")]
public class GenshinController : ControllerBase
{
    private readonly IDashboardApplicationExecutorBuilder m_ExecutorBuilder;
    private readonly ILogger<GenshinController> m_Logger;

    public GenshinController(
        IDashboardApplicationExecutorBuilder executorBuilder,
        ILogger<GenshinController> logger)
    {
        m_ExecutorBuilder = executorBuilder;
        m_Logger = logger;
    }

    [HttpPost("character")]
    public async Task<IActionResult> ExecuteCharacter([FromBody] GenshinCharacterRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing Genshin character command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.Genshin, server,
            (nameof(request.Character).ToLowerInvariant(), request.Character.Trim()));

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Genshin.Character)
            .WithParameters(parameters)
            .AddValidator<string>(nameof(request.Character).ToLowerInvariant(), value => !string.IsNullOrWhiteSpace(value))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("abyss")]
    public async Task<IActionResult> ExecuteAbyss([FromBody] GenshinAbyssRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing Genshin abyss command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.Genshin, server, (nameof(request.Floor).ToLowerInvariant(), request.Floor));

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Genshin.Abyss)
            .WithParameters(parameters)
            .AddValidator<uint>(nameof(request.Floor).ToLowerInvariant(), floor => floor is >= 9 and <= 12)
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("theater")]
    public async Task<IActionResult> ExecuteTheater([FromBody] GenshinSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing Genshin theater command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.Genshin, server);

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Genshin.Theater)
            .WithParameters(parameters)
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("stygian")]
    public async Task<IActionResult> ExecuteStygian([FromBody] GenshinSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing Genshin stygian command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.Genshin, server);

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Genshin.Stygian)
            .WithParameters(parameters)
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return result.MapToActionResult();
    }

    [HttpPost("charlist")]
    public async Task<IActionResult> ExecuteCharList([FromBody] GenshinSimpleCommandRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryGetDiscordUserId(out var discordUserId, out var errorResult))
            return errorResult!;
        if (!TryParseServer(request.Server, out var server, out errorResult))
            return errorResult!;

        m_Logger.LogInformation("Executing Genshin charlist command for user {UserId}", discordUserId);

        var parameters = BuildParameters(Game.Genshin, server);

        var executor = m_ExecutorBuilder
            .WithDiscordUserId(discordUserId)
            .WithCommandName(CommandName.Genshin.CharList)
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
