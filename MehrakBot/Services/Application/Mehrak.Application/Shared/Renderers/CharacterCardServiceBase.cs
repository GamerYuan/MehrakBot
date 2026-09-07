#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.User.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

#endregion

namespace Mehrak.Application.Shared.Renderers;

/// <summary>
/// Base for character card services. Deduplicates the load-decision (user-uploaded portrait
/// stream vs stock repository image) and the resize step that each game previously
/// copy-pasted. Per-game behavior is configured via the protected abstract properties.
/// </summary>
public abstract class CharacterCardServiceBase<TData> : CardServiceBase<TData>
{
    protected CharacterCardServiceBase(
        string cardTypeName,
        IImageRepository imageRepository,
        ILogger logger,
        IApplicationMetrics metrics,
        FontDefinitions fonts) : base(cardTypeName, imageRepository, logger, metrics, fonts)
    {
    }

    /// <summary>Width used when no explicit <see cref="CharacterPortraitConfig.TargetScale"/> is set.</summary>
    protected abstract int DefaultPortraitWidth { get; }

    /// <summary>Resampler used for the portrait resize.</summary>
    protected abstract IResampler PortraitResampler { get; }

    // Allocation budget for the scaled portrait intermediate: even an in-range scale
    // is unsafe on an arbitrarily large source, so the output is clamped by both
    // dimension and total pixels. Applies to user and stock portraits alike.
    private const float MinPortraitScale = 0.01f;
    private const float MaxPortraitScale = 10f;
    private const int MaxPortraitOutputDimension = 4096;
    private const long MaxPortraitOutputPixels = 16_777_216; // 4096 x 4096

    /// <summary>
    /// Loads the character portrait — either the user's uploaded image (when
    /// <see cref="ICardGenerationContext{TData}.PortraitImageStream"/> is set) or the stock
    /// image produced by <paramref name="loadStockImage"/> — then applies resize from
    /// <paramref name="context"/>'s portrait config.
    /// </summary>
    protected async Task<Image> LoadPortraitAsync(
        ICardGenerationContext<TData> context,
        Func<Task<Image>> loadStockImage,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        Image portrait;
        if (context.PortraitImageStream != null)
        {
            portrait = await LoadImageFromStreamAsync<Rgba32>(
                context.PortraitImageStream, disposables, cancellationToken);
        }
        else
        {
            portrait = await loadStockImage();
        }

        portrait.Mutate(ctx => ApplyPortraitMutate(ctx, context.PortraitConfig));
        return portrait;
    }

    /// <summary>
    /// Generic overload allowing the stock loader to return <see cref="Image{TPixel}"/>
    /// (e.g. <see cref="Rgba32"/>) directly, since <see cref="Task{TResult}"/> is not covariant.
    /// </summary>
    protected async Task<Image> LoadPortraitAsync<TPixel>(
        ICardGenerationContext<TData> context,
        Func<Task<Image<TPixel>>> loadStockImage,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image portrait;
        if (context.PortraitImageStream != null)
        {
            portrait = await LoadImageFromStreamAsync<Rgba32>(
                context.PortraitImageStream, disposables, cancellationToken);
        }
        else
        {
            portrait = await loadStockImage();
        }

        portrait.Mutate(ctx => ApplyPortraitMutate(ctx, context.PortraitConfig));
        return portrait;
    }

    private void ApplyPortraitMutate(IImageProcessingContext ctx, CharacterPortraitConfig? config)
    {
        var size = ctx.GetCurrentSize();
        var scale = config?.TargetScale is float requestedScale &&
                    float.IsFinite(requestedScale) &&
                    requestedScale >= MinPortraitScale && requestedScale <= MaxPortraitScale
            ? requestedScale
            : (float)DefaultPortraitWidth / Math.Max(1, size.Width);
        var targetSize = ComputePortraitTargetSize(size.Width, size.Height, scale);
        ctx.Resize(new ResizeOptions
        {
            Size = targetSize,
            Mode = ResizeMode.Stretch,
            Sampler = PortraitResampler
        });

        if (config?.FlipX == true)
            ctx.Flip(FlipMode.Horizontal);
    }

    /// <summary>
    /// Computes a scale-derived resize width that can never exceed the portrait
    /// allocation budget. Resize preserves aspect ratio (height auto), so output
    /// pixels grow with the square of the width ratio: the width is capped by both
    /// the maximum dimension and the maximum pixel count.
    /// </summary>
    internal static int ComputePortraitTargetWidth(int sourceWidth, int sourceHeight, float scale)
    {
        return ComputePortraitTargetSize(sourceWidth, sourceHeight, scale).Width;
    }

    internal static Size ComputePortraitTargetSize(int sourceWidth, int sourceHeight, float scale)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || !float.IsFinite(scale) || scale <= 0)
            return new Size(1, 1);

        var sourcePixels = (double)sourceWidth * sourceHeight;
        var maximumScale = Math.Min(
            Math.Min((double)MaxPortraitOutputDimension / sourceWidth,
                (double)MaxPortraitOutputDimension / sourceHeight),
            Math.Sqrt(MaxPortraitOutputPixels / sourcePixels));
        var effectiveScale = Math.Min(scale, maximumScale);

        var width = Math.Clamp((int)Math.Round(sourceWidth * effectiveScale), 1, MaxPortraitOutputDimension);
        var height = Math.Clamp((int)Math.Round(sourceHeight * effectiveScale), 1, MaxPortraitOutputDimension);

        // Rounding both dimensions can add enough pixels to cross the budget. Reduce the
        // dimension with the larger rounding error until the final allocation is safe.
        while ((long)width * height > MaxPortraitOutputPixels)
        {
            var widthError = Math.Abs(width - sourceWidth * effectiveScale);
            var heightError = Math.Abs(height - sourceHeight * effectiveScale);
            if (widthError >= heightError && width > 1)
                width--;
            else if (height > 1)
                height--;
            else
                break;
        }

        return new Size(width, height);
    }
}
