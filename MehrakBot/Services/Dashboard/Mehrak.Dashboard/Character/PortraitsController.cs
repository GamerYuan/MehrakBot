using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Character;

[ApiController]
[Authorize]
[Route("portraits")]
public class PortraitsController : ControllerBase
{
    private readonly ICharacterPortraitConfigService m_PortraitConfigService;
    private readonly CharacterDbContext m_CharacterContext;
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<PortraitsController> m_Logger;

    public PortraitsController(ICharacterPortraitConfigService portraitConfigService,
        CharacterDbContext characterContext,
        IImageRepository imageRepository,
        ILogger<PortraitsController> logger)
    {
        m_PortraitConfigService = portraitConfigService;
        m_CharacterContext = characterContext;
        m_ImageRepository = imageRepository;
        m_Logger = logger;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetPortraitList([FromQuery] string? game, [FromQuery] string? character)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (string.IsNullOrWhiteSpace(character))
            return BadRequest(new { error = "Character parameter is required." });

        var normalized = character.ReplaceLineEndings("").Trim();

        var charModel = await m_CharacterContext.Characters
            .AsNoTracking()
            .Include(x => x.ServerIds)
            .FirstOrDefaultAsync(x => x.Game == gameEnum && x.Name == normalized);

        if (charModel == null)
            return NotFound(new { error = "Character not found." });

        return Ok(charModel.ServerIds.Select(x => x.ServerId));
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetPortraitConfig([FromQuery] string? game, [FromQuery] string? character, [FromQuery] int? serverId)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (serverId.HasValue)
        {
            var config = await m_PortraitConfigService.GetConfigAsync(gameEnum, serverId.Value);
            return config == null ? NotFound(new { error = "No config found for this server ID." }) : Ok(config);
        }

        if (!string.IsNullOrWhiteSpace(character))
        {
            var normalized = character.ReplaceLineEndings("").Trim();

            var charModel = await m_CharacterContext.Characters
                .AsNoTracking()
                .Include(x => x.ServerIds)
                .FirstOrDefaultAsync(x => x.Game == gameEnum && x.Name == normalized);

            if (charModel == null)
                return NotFound(new { error = "Character not found." });

            var configs = new Dictionary<string, CharacterPortraitConfig>();
            foreach (var sid in charModel.ServerIds)
            {
                var config = await m_PortraitConfigService.GetConfigAsync(gameEnum, sid.ServerId);
                if (config != null)
                {
                    var key = charModel.ServerIds.Count > 1
                        ? $"{normalized}_{sid.ServerId}"
                        : normalized;
                    configs[key] = config;
                }
            }

            return Ok(configs);
        }

        var allConfigs = await m_PortraitConfigService.GetAllConfigsAsync(gameEnum);
        return Ok(allConfigs);
    }

    [HttpPatch("config")]
    public async Task<IActionResult> UpdatePortraitConfig([FromQuery] string? game, [FromQuery] int? serverId,
        [FromBody] CharacterPortraitConfigUpdate update)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (!serverId.HasValue)
            return BadRequest(new { error = "Server ID parameter is required." });

        if (!HasGameWriteAccess(game!))
            return Forbid();

        m_Logger.LogInformation("Updating portrait config for ServerId {ServerId} in game {Game}", serverId, gameEnum);

        var success = await m_PortraitConfigService.UpsertConfigAsync(gameEnum, serverId.Value, update);

        if (!success)
            return NotFound(new { error = "Server ID not found in character database." });

        return NoContent();
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetPortraitImage([FromQuery] string? game, [FromQuery] int? serverId)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (!serverId.HasValue)
            return BadRequest(new { error = "Server ID parameter is required." });

        var format = gameEnum switch
        {
            Game.Genshin => FileNameFormat.Genshin.PortraitName,
            Game.HonkaiStarRail => FileNameFormat.Hsr.PortraitName,
            Game.ZenlessZoneZero => FileNameFormat.Zzz.PortraitName,
            Game.HonkaiImpact3 => FileNameFormat.Hi3.CostumeName,
            _ => null
        };

        if (format == null)
            return BadRequest(new { error = $"Unsupported game: {gameEnum}" });

        var imageName = string.Format(format, serverId.Value);
        if (await m_ImageRepository.FileExistsAsync(imageName))
        {
            var stream = await m_ImageRepository.DownloadFileToStreamAsync(imageName);
            return File(stream, FileNameFormat.PngContentType);
        }

        return NotFound(new { error = $"Portrait image for {serverId.Value} ({gameEnum.ToFriendlyString()}) not found, please generate an image with this character in the Characters tab and try again" });
    }

    private static bool TryParseGame(string? input, out Game game, out string error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Game parameter is required.";
            game = default;
            return false;
        }

        if (!Enum.TryParse<Game>(input, true, out game))
        {
            error = "Invalid game parameter.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool HasGameWriteAccess(string game)
    {
        if (User.IsInRole("superadmin"))
            return true;

        var normalized = game.Trim().ToLowerInvariant();
        var claimValue = $"game_write:{normalized}";
        return User.Claims.Any(c =>
            string.Equals(c.Type, "perm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, claimValue, StringComparison.OrdinalIgnoreCase));
    }
}
