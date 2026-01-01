using System.Security.Claims;
using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Dashboard.Models;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
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
    private readonly IAttachmentStorageService m_AttachmentStorage;

    public GenshinController(
        IDashboardApplicationExecutorBuilder executorBuilder,
        ILogger<GenshinController> logger,
        IAttachmentStorageService attachmentStorage)
    {
        m_ExecutorBuilder = executorBuilder;
        m_Logger = logger;
        m_AttachmentStorage = attachmentStorage;
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

        var executor = m_ExecutorBuilder.For<GenshinCharacterApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new GenshinCharacterApplicationContext(discordUserId, parameters))
            .AddValidator<string>(nameof(request.Character).ToLowerInvariant(), value => !string.IsNullOrWhiteSpace(value))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return await MapExecutionResultAsync(result, HttpContext.RequestAborted);
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

        var executor = m_ExecutorBuilder.For<GenshinAbyssApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new GenshinAbyssApplicationContext(discordUserId, parameters))
            .AddValidator<uint>(nameof(request.Floor).ToLowerInvariant(), floor => floor is >= 9 and <= 12)
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return await MapExecutionResultAsync(result, HttpContext.RequestAborted);
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

        var executor = m_ExecutorBuilder.For<GenshinTheaterApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new GenshinTheaterApplicationContext(discordUserId, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return await MapExecutionResultAsync(result, HttpContext.RequestAborted);
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

        var executor = m_ExecutorBuilder.For<GenshinStygianApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new GenshinStygianApplicationContext(discordUserId, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return await MapExecutionResultAsync(result, HttpContext.RequestAborted);
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

        var executor = m_ExecutorBuilder.For<GenshinCharListApplicationContext>()
            .WithDiscordUserId(discordUserId)
            .WithApplicationContext(new GenshinCharListApplicationContext(discordUserId, parameters))
            .Build();

        var result = await executor.ExecuteAsync(request.ProfileId, HttpContext.RequestAborted);
        return await MapExecutionResultAsync(result, HttpContext.RequestAborted);
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

    private async Task<IActionResult> MapExecutionResultAsync(DashboardApplicationExecutionResult result, CancellationToken cancellationToken)
    {
        return result.Status switch
        {
            DashboardExecutionStatus.Success when result.CommandResult is not null =>
                await TryExtractAttachmentAsync(result.CommandResult, cancellationToken) is { } attachment
                    ? Ok(attachment)
                    : HandleMissingAttachment(),
            DashboardExecutionStatus.ValidationFailed
                => BadRequest(new { error = result.ErrorMessage ?? "Validation failed.", validationErrors = result.ValidationErrors }),
            DashboardExecutionStatus.AuthenticationRequired
                => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage ?? "Authentication required.", code = "AUTH_REQUIRED" }),
            DashboardExecutionStatus.AuthenticationFailed
                => Unauthorized(new { error = result.ErrorMessage ?? "Authentication failed." }),
            DashboardExecutionStatus.NotFound
                => NotFound(new { error = result.ErrorMessage ?? "Requested resource was not found." }),
            _
                => StatusCode(StatusCodes.Status500InternalServerError, new { error = result.ErrorMessage ?? "Unable to execute command." })
        };
    }

    private async Task<DashboardCommandAttachmentDto?> TryExtractAttachmentAsync(CommandResult commandResult, CancellationToken cancellationToken)
    {
        if (commandResult.Data is null)
            return null;

        foreach (var component in commandResult.Data.Components)
        {
            CommandAttachment? attachment = component switch
            {
                CommandAttachment direct => direct,
                CommandSection section => section.Attachment,
                _ => null
            };

            if (attachment is null)
                continue;

            var stored = await m_AttachmentStorage.StoreAsync(attachment, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                m_Logger.LogWarning("Failed to persist attachment {AttachmentFile} for dashboard request", attachment.FileName);
                continue;
            }

            return new DashboardCommandAttachmentDto(stored.OriginalFileName, stored.StorageFileName);
        }

        return null;
    }

    private IActionResult HandleMissingAttachment()
    {
        var discordId = User.FindFirstValue("discord_id") ?? "unknown";
        m_Logger.LogWarning(
            "Dashboard command result contained no attachment for user {UserId}",
            discordId);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { error = "Command executed successfully but no image was generated." });
    }
}
