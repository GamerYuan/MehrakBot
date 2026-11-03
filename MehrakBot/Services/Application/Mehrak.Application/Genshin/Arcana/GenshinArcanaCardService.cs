#region

using System.Globalization;
using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin.Arcana;

public class GenshinArcanaCardService : CardServiceBase<GenshinTarotCardState>, IDisposable
{
    private const int CanvasWidth = 2400;
    private const int CanvasHeight = 2400;
    private const int CardsPerRow = 6;
    private const int CardWidth = 320;
    private const int CardHeight = 440;
    private const int ColumnGap = 24;
    private const int RowGap = 20;
    private const int LabelHeight = 90;
    private const int GridTop = 180;
    private const int HeaderBottom = 148;

    private static readonly Rgba32 ArcanaBaseColor = new(0x0d, 0x0c, 0x23);
    private static readonly int[] RowCounts = [6, 6, 6, 4];

    private readonly object m_ResourceLock = new();
    private readonly SemaphoreSlim m_InitializationLock = new(1, 1);

    private bool m_Disposed;

    public GenshinArcanaCardService(
        IImageRepository imageRepository,
        ILogger<GenshinArcanaCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Genshin Arcana",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 74, normalSize: 40, mediumSize: 34,
                smallSize: 26, tinySize: 24))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        await m_InitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (m_ResourceLock)
            {
                ObjectDisposedException.ThrowIf(m_Disposed, this);
                if (StaticBackground is not null) return;
            }

            var backgroundOverlay = await LoadRequiredImageAsync(
                FileNameFormat.Genshin.ArcanaBackgroundName, cancellationToken).ConfigureAwait(false);

            Image<Rgba32>? composedBackground = null;
            try
            {
                if (backgroundOverlay.Width != CanvasWidth || backgroundOverlay.Height != CanvasHeight)
                    throw new InvalidOperationException("Lunar Arcana background must be prepared at 2400x2400.");

                composedBackground = new Image<Rgba32>(CanvasWidth, CanvasHeight, ArcanaBaseColor);
                composedBackground.Mutate(drawingContext =>
                    drawingContext.DrawImage(backgroundOverlay, new Point(0, 0), 1f));

                lock (m_ResourceLock)
                {
                    ObjectDisposedException.ThrowIf(m_Disposed, this);
                    StaticBackground = composedBackground;
                    composedBackground = null;
                }
            }
            catch
            {
                composedBackground?.Dispose();
                throw;
            }
            finally
            {
                backgroundOverlay.Dispose();
            }
        }
        finally
        {
            m_InitializationLock.Release();
        }
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<GenshinTarotCardState> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cards = GenshinArcanaCardCatalog.Resolve(context.Data);

        lock (m_ResourceLock)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            if (StaticBackground is null)
                throw new InvalidOperationException("Lunar Arcana static resources have not been initialized.");
        }

        var artworkTasks = cards.Select(async (card, index) =>
        {
            var artwork = await LoadImageFromRepositoryAsync<Rgba32>(card.AssetKey, disposables, cancellationToken)
                .ConfigureAwait(false);
            if (artwork.Width != CardWidth || artwork.Height != CardHeight)
                throw new InvalidOperationException($"Lunar Arcana artwork must be prepared at {CardWidth}x{CardHeight}: {card.AssetKey}");
            return (index, artwork);
        }).ToArray();

        var loadedArtworks = await Task.WhenAll(artworkTasks).ConfigureAwait(false);
        var artworks = new Image<Rgba32>[cards.Count];
        foreach (var (index, artwork) in loadedArtworks)
            artworks[index] = artwork;

        background.Mutate(drawingContext =>
        {
            for (var index = 0; index < cards.Count; index++)
            {
                var cell = GetCellBounds(index);
                drawingContext.DrawImage(artworks[index], new Point((int)cell.X, (int)cell.Y), 1f);
            }

            drawingContext.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(60, HeaderBottom),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Lunar Arcana", Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(CanvasWidth - 60, HeaderBottom - 50),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"Collected {context.Data.CurrNum.ToString(CultureInfo.InvariantCulture)} / " +
                   $"{context.Data.TotalNum.ToString(CultureInfo.InvariantCulture)}",
                    Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Medium)
                {
                    Origin = new Vector2(CanvasWidth - 60, HeaderBottom),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level} · {context.GameProfile.GameUid}",
                    Brushes.Solid(Color.White), null);

                for (var index = 0; index < cards.Count; index++)
                {
                    var card = cards[index];
                    var cell = GetCellBounds(index);

                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new Vector2(cell.X + CardWidth / 2f, cell.Y + CardHeight + 10),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top
                    }, card.Name, Brushes.Solid(Color.White), null);

                    canvas.DrawText(new RichTextOptions(Fonts.Medium)
                    {
                        Origin = new Vector2(cell.X + CardWidth / 2f, cell.Y + CardHeight + 56),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top
                    }, card.UnlockNum.ToString(CultureInfo.InvariantCulture), Brushes.Solid(Color.White), null);
                }
            });
        });
    }

    public void Dispose()
    {
        Image<Rgba32>? background;

        lock (m_ResourceLock)
        {
            if (m_Disposed) return;
            m_Disposed = true;

            background = StaticBackground;
            StaticBackground = null;
        }

        background?.Dispose();
    }

    private async Task<Image<Rgba32>> LoadRequiredImageAsync(string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = await ImageRepository.DownloadFileToStreamAsync(fileName, cancellationToken)
            .ConfigureAwait(false);
        return await Image.LoadAsync<Rgba32>(stream, cancellationToken).ConfigureAwait(false);
    }

    private static RectangleF GetCellBounds(int index)
    {
        var row = index / CardsPerRow;
        var column = index % CardsPerRow;
        var rowCount = RowCounts[row];
        var rowWidth = rowCount * CardWidth + (rowCount - 1) * ColumnGap;
        var startX = (CanvasWidth - rowWidth) / 2f;

        return new RectangleF(
            startX + column * (CardWidth + ColumnGap),
            GridTop + row * (CardHeight + LabelHeight + RowGap),
            CardWidth,
            CardHeight);
    }
}
