using Mehrak.Dashboard.Shared;
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

[Authorize]
[Route("portraits")]
public class PortraitsController : GameWriteController
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

        var entries = new List<PortraitListEntry>();
        foreach (var sid in charModel.ServerIds)
        {
            entries.Add(new PortraitListEntry(sid.ServerId, null, GetPortraitImageName(gameEnum, sid.ServerId, null)!));

            if (gameEnum != Game.ZenlessZoneZero)
                continue;

            var outfitKeys = await m_ImageRepository.ListFilesAsync($"zzz/portrait_{sid.ServerId}_");
            foreach (var key in outfitKeys)
            {
                if (TryParseZzzOutfitKey(key, out var subId))
                    entries.Add(new PortraitListEntry(sid.ServerId, subId, key));
            }

            var outfitConfigSubIds = await m_CharacterContext.CharacterPortraitConfigs.AsNoTracking()
                .Where(c => c.ServerId == sid.ServerId && c.SubId != 0)
                .Select(c => c.SubId)
                .ToListAsync();
            foreach (var subId in outfitConfigSubIds)
                entries.Add(new PortraitListEntry(sid.ServerId, subId, GetPortraitImageName(gameEnum, sid.ServerId, subId)!));
        }

        return Ok(entries
            .DistinctBy(e => (e.ServerId, e.SubId))
            .OrderBy(e => e.ServerId)
            .ThenBy(e => e.SubId ?? 0));
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetPortraitConfig([FromQuery] string? game, [FromQuery] string? character, [FromQuery] int? serverId, [FromQuery] int? subId)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (serverId.HasValue)
        {
            var config = await m_PortraitConfigService.GetConfigAsync(gameEnum, serverId.Value, subId ?? 0);
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
                var config = await m_PortraitConfigService.GetConfigAsync(gameEnum, sid.ServerId, subId ?? 0);
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
    [Authorize(Policy = "RequireGameWrite")]
    public async Task<IActionResult> UpdatePortraitConfig([FromQuery] string? game, [FromQuery] int? serverId, [FromQuery] int? subId,
        [FromBody] CharacterPortraitConfigUpdate update)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (!await AuthorizeGameWriteAsync(gameEnum))
            return GameWriteDenied(gameEnum);

        if (!serverId.HasValue)
            return BadRequest(new { error = "Server ID parameter is required." });

        if (subId.HasValue && gameEnum != Game.ZenlessZoneZero)
            return BadRequest(new { error = "Sub ID is only supported for ZZZ outfits." });

        m_Logger.LogInformation("Updating portrait config for ServerId {ServerId} SubId {SubId} in game {Game}", serverId, subId ?? 0, gameEnum);

        var success = await m_PortraitConfigService.UpsertConfigAsync(gameEnum, serverId.Value, update, subId ?? 0);

        if (!success)
            return NotFound(new { error = "Server ID not found in character database." });

        return NoContent();
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetPortraitImage([FromQuery] string? game, [FromQuery] int? serverId, [FromQuery] int? subId)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (!serverId.HasValue)
            return BadRequest(new { error = "Server ID parameter is required." });

        if (subId.HasValue && gameEnum != Game.ZenlessZoneZero)
            return BadRequest(new { error = "Sub ID is only supported for ZZZ outfits." });

        var imageName = GetPortraitImageName(gameEnum, serverId.Value, subId);
        if (imageName == null)
            return BadRequest(new { error = $"Unsupported game: {gameEnum}" });

        if (await m_ImageRepository.FileExistsAsync(imageName))
        {
            var stream = await m_ImageRepository.DownloadFileToStreamAsync(imageName);
            return File(stream, FileNameFormat.PngContentType);
        }

        return NotFound(new { error = $"Portrait image for {serverId.Value} ({gameEnum.ToFriendlyString()}) not found, please generate an image with this character in the Characters tab and try again" });
    }

    private static string? GetPortraitImageName(Game game, int serverId, int? subId)
    {
        var format = game switch
        {
            Game.Genshin => FileNameFormat.Genshin.PortraitName,
            Game.HonkaiStarRail => FileNameFormat.Hsr.PortraitName,
            Game.ZenlessZoneZero => FileNameFormat.Zzz.PortraitName,
            Game.HonkaiImpact3 => FileNameFormat.Hi3.CostumeName,
            _ => null
        };

        if (format == null)
            return null;

        return game == Game.ZenlessZoneZero && subId is > 0
            ? string.Format(format, $"{serverId}_{subId.Value}")
            : string.Format(format, serverId);
    }

    private static bool TryParseZzzOutfitKey(string key, out int subId)
    {
        subId = 0;
        const string prefix = "zzz/portrait_";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !key.EndsWith(FileNameFormat.PngExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idPart = key[prefix.Length..^FileNameFormat.PngExtension.Length];
        var parts = idPart.Split('_');
        return parts.Length == 2 && int.TryParse(parts[1], out subId);
    }

    private sealed record PortraitListEntry(int ServerId, int? SubId, string ImageName);
}
