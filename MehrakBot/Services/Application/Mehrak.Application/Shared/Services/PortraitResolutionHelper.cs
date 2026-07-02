#region

using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;

#endregion

namespace Mehrak.Application.Shared.Services;

/// <summary>
/// Resolves which portrait a card should render: either the user's uploaded portrait
/// (if one is active) or the stock per-character config. Two-phase so the application
/// service can obtain the portrait key for cache invalidation without paying the image
/// download cost on a cache hit.
/// </summary>
public static class PortraitResolutionHelper
{
    /// <summary>
    /// Phase 1 — metadata only (cheap DB query). Returns the active user portrait for the
    /// given character, or <see langword="null"/> if none is active.
    /// </summary>
    public static async Task<ActivePortrait?> GetActivePortraitAsync(
        IUserPortraitService portraitService, ulong userId, Game game,
        string characterName, CancellationToken cancellationToken = default)
    {
        var portraits = await portraitService.GetUserPortraitsAsync(
            (long)userId, game, characterName, cancellationToken);
        var active = portraits?.FirstOrDefault(p => p.IsActive);
        if (active == null)
            return null;

        return new ActivePortrait(active.S3Key, active.Id, active.Config);
    }

    /// <summary>
    /// Phase 2 — download the active user portrait image and map its config. Called only
    /// on cache miss. <paramref name="stockConfigFactory"/> resolves the stock config and
    /// is used as the fallback if the portrait download fails, so the card never renders
    /// with the user's geometry applied to a stock image.
    /// </summary>
    public static async Task<PortraitResolution> ResolveActivePortraitAsync(
        IUserPortraitService portraitService, ulong userId, ActivePortrait portrait,
        Func<Task<CharacterPortraitConfig?>> stockConfigFactory,
        CancellationToken cancellationToken = default)
    {
        var portraitResult = await portraitService.GetPortraitImageAsync(
            (long)userId, portrait.Key, portrait.Id, cancellationToken);

        if (portraitResult == null)
        {
            // Download failed — fall back to the stock image AND the stock config so the
            // card does not render a stock image with the user's tuned offset/scale.
            return new PortraitResolution(null, await stockConfigFactory());
        }

        return new PortraitResolution(portraitResult.Content, MapConfig(portrait.Config));
    }

    private static CharacterPortraitConfig MapConfig(UserPortraitConfigDto dto) => new()
    {
        OffsetX = dto.OffsetX,
        OffsetY = dto.OffsetY,
        TargetScale = dto.TargetScale,
        FlipX = dto.FlipX,
        ArtistAttribution = dto.ArtistAttribution
    };
}

/// <summary>Metadata for an active user portrait, resolved in phase 1.</summary>
public sealed record ActivePortrait(string Key, Guid Id, UserPortraitConfigDto Config);

/// <summary>
/// The resolved portrait to render: the image stream (null = use stock image) and the
/// portrait config that should be applied to whichever image is rendered.
/// </summary>
public sealed record PortraitResolution(Stream? ImageStream, CharacterPortraitConfig? Config);
