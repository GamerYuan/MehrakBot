#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Shared.Renderers;

/// <summary>
/// Base class for character card services, adding support for user-uploaded
/// portrait images. Only character cards render a portrait, so the
/// <see cref="IUserPortraitService"/> dependency lives here rather than on
/// <see cref="CardServiceBase{TData}"/>.
/// </summary>
public abstract class CharacterCardServiceBase<TData> : CardServiceBase<TData>
{
    protected readonly IUserPortraitService UserPortraitService;

    protected CharacterCardServiceBase(
        string cardTypeName,
        IImageRepository imageRepository,
        IUserPortraitService userPortraitService,
        ILogger logger,
        IApplicationMetrics metrics,
        FontDefinitions fonts)
        : base(cardTypeName, imageRepository, logger, metrics, fonts)
    {
        UserPortraitService = userPortraitService;
    }

    /// <summary>
    /// Loads the user's active uploaded portrait for the given character, if one exists.
    /// Returns null (no active portrait, image download failed, or a non-fatal error occurred)
    /// so callers can fall back to the stock portrait.
    /// </summary>
    /// <remarks>
    /// <paramref name="characterName"/> is nullable; a null or empty name skips the lookup
    /// and returns null, so callers may pass the raw API value without null-forgiving operators.
    /// Genuine cancellation is propagated rather than masked as a fallback.
    /// </remarks>
    protected async Task<UserPortraitLoadResult?> TryLoadUserPortraitAsync(
        ulong userId, Game game, string? characterName,
        DisposableBag disposables, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(characterName)) return null;

        try
        {
            var portraits = await UserPortraitService.GetUserPortraitsAsync(
                (long)userId, game, characterName, cancellationToken);
            var active = portraits.FirstOrDefault(p => p.IsActive);
            if (active == null) return null;

            var download = await UserPortraitService.GetPortraitImageAsync(
                (long)userId, active.Id, cancellationToken);
            if (download == null) return null;

            SixLabors.ImageSharp.Image<Rgba32> image;
            await using (download.Content)
                image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(download.Content, cancellationToken);
            disposables.Add(image);

            // The user's per-upload config wins over the global admin config,
            // since a custom image has different dimensions than the stock one.
            var config = new CharacterPortraitConfig
            {
                OffsetX = active.Config.OffsetX,
                OffsetY = active.Config.OffsetY,
                TargetScale = active.Config.TargetScale,
                EnableGradientFade = active.Config.EnableGradientFade,
                GradientFadeStart = active.Config.GradientFadeStart,
            };
            return new UserPortraitLoadResult(image, config);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Failed to load user portrait for user {UserId}, {Game}/{Character}; falling back to stock portrait",
                userId, game, characterName);
            return null;
        }
    }

    protected sealed record UserPortraitLoadResult(
        SixLabors.ImageSharp.Image<Rgba32> Image,
        CharacterPortraitConfig Config);
}
