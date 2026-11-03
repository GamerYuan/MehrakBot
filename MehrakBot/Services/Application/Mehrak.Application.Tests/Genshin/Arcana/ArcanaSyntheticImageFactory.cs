#region

using Mehrak.Application.Genshin.Arcana;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Tests.Genshin.Arcana;

internal static class ArcanaSyntheticImageFactory
{
    internal const int ArtWidth = 320;
    internal const int ArtHeight = 440;
    internal const int BackgroundWidth = 2400;
    internal const int BackgroundHeight = 2400;
    internal const int TransparentCornerSize = 200;

    private static readonly Lazy<byte[]> BackgroundPng = new(CreateBackgroundPng);
    private static readonly Lazy<byte[][]> ArtPngs = new(() => CreateArtPngs(true));
    private static readonly Lazy<byte[][]> LockedArtPngs = new(() => CreateArtPngs(false));

    internal static Rgba32 ArtColor(int index)
    {
        return new Rgba32(
            (byte)(30 + index * 8),
            (byte)(200 - index * 6),
            (byte)(120 + index * 2));
    }

    internal static Rgba32 ExpectedRenderedColor(int index, bool isUnlock)
    {
        var color = ArtColor(index);
        return isUnlock
            ? color
            : new Rgba32((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2));
    }

    internal static byte[] GetBackgroundPng() => BackgroundPng.Value;

    internal static byte[] GetArtPng(int position, bool isUnlock = true) =>
        (isUnlock ? ArtPngs : LockedArtPngs).Value[position - 1];

    internal static Mock<IImageRepository> CreateRepository(bool includeArt = true,
        Func<string, bool>? isMissing = null)
    {
        var repository = new Mock<IImageRepository>();
        repository.Setup(x => x.DownloadFileToStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string fileName, CancellationToken _) =>
            {
                if (fileName == FileNameFormat.Genshin.ArcanaBackgroundName)
                    return new MemoryStream(GetBackgroundPng(), false);

                var definition = GenshinArcanaCardCatalog.Cards
                    .SingleOrDefault(x => x.AssetKey == fileName || x.LockedAssetKey == fileName);
                if (definition is null || !includeArt || isMissing?.Invoke(fileName) == true)
                    throw new ImageNotFoundException(fileName);

                return new MemoryStream(GetArtPng(definition.Position, fileName == definition.AssetKey), false);
            });
        return repository;
    }

    internal static Rgba32 GetPixel(Image<Rgba32> image, int x, int y) => image[x, y];

    internal static (int X, int Y, int Width, int Height) GetCellBounds(int index)
    {
        // Mirrors the production layout constants for assertions without making them public.
        const int canvasWidth = 2400;
        const int cardWidth = 320;
        const int cardHeight = 440;
        const int columnGap = 24;
        const int rowGap = 20;
        const int labelHeight = 90;
        const int gridTop = 180;

        int[] rowCounts = [6, 6, 6, 4];
        var row = index / 6;
        var column = index % 6;
        var rowWidth = rowCounts[row] * cardWidth + (rowCounts[row] - 1) * columnGap;
        var startX = (canvasWidth - rowWidth) / 2;
        return (startX + column * (cardWidth + columnGap),
            gridTop + row * (cardHeight + labelHeight + rowGap), cardWidth, cardHeight);
    }

    private static byte[][] CreateArtPngs(bool isUnlock)
    {
        var result = new byte[GenshinArcanaCardCatalog.Cards.Count][];
        for (var index = 0; index < result.Length; index++)
            result[index] = CreateArtPng(index, isUnlock);
        return result;
    }

    private static byte[] CreateArtPng(int index, bool isUnlock)
    {
        using var image = new Image<Rgba32>(ArtWidth, ArtHeight, ExpectedRenderedColor(index, isUnlock));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] CreateBackgroundPng()
    {
        using var image = new Image<Rgba32>(BackgroundWidth, BackgroundHeight, new Rgba32(200, 100, 50, 128));
        for (var y = 0; y < TransparentCornerSize; y++)
        {
            for (var x = 0; x < TransparentCornerSize; x++)
                image[x, y] = new Rgba32(0, 0, 0, 0);
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
