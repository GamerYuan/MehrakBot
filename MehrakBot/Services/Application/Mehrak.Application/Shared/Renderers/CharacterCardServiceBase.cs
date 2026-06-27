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
        if (config?.TargetScale > 0f)
        {
            var scale = config.TargetScale.Value;
            ctx.Resize((int)(ctx.GetCurrentSize().Width * scale), 0, PortraitResampler);
        }
        else
        {
            ctx.Resize(DefaultPortraitWidth, 0, PortraitResampler);
        }

        if (config?.FlipX == true)
            ctx.Flip(FlipMode.Horizontal);
    }
}
