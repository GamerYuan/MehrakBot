#region

using System.Text.Json;
using Mehrak.Application.Genshin.Arcana;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.Genshin.Arcana;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

#endregion

// These local-image tests do not need the Docker setup fixture under Mehrak.Application.Tests.
namespace Mehrak.Application.Rendering.Tests.Genshin.Arcana;

public class GenshinArcanaCardServiceTests
{
    [Test]
    public void AssetKeys_UseExistingGenshinAssetPaths()
    {
        Assert.That(FileNameFormat.Genshin.ArcanaBackgroundName, Is.EqualTo("genshin/arcana_bg.png"));
        foreach (var definition in GenshinArcanaCardCatalog.Cards)
        {
            Assert.Multiple(() =>
            {
                Assert.That(definition.AssetKey, Does.StartWith("genshin/arcana_"));
                Assert.That(definition.LockedAssetKey, Does.StartWith("genshin/arcana_"));
                Assert.That(definition.AssetKey.Count(x => x == '/'), Is.EqualTo(1));
                Assert.That(definition.LockedAssetKey.Count(x => x == '/'), Is.EqualTo(1));
            });
        }
    }

    [Test]
    public async Task Resolve_OrdersByCountDescendingThenArcanaNumber()
    {
        var state = await ReadSampleAsync();
        state.List.Reverse();
        var namesBefore = state.List.Select(x => x.Name).ToArray();

        var resolved = GenshinArcanaCardCatalog.Resolve(state);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Select(x => x.Position), Is.EqualTo(new[]
            {
                18, 1, 2, 3, 6, 7, 9, 11, 14, 16, 20, 22, 4, 5, 8, 10, 12, 13, 15, 17, 19, 21
            }));
            Assert.That(resolved[0].Name, Is.EqualTo("The Moon"));
            Assert.That(resolved[0].UnlockNum, Is.EqualTo(3));
            Assert.That(state.List.Select(x => x.Name), Is.EqualTo(namesBefore), "Input order is not mutated");
        });
    }

    [Test]
    public async Task RenderCardAsync_SquareGrid_DrawsAllCardsNamesAndCountsWithOwnershipBrightness()
    {
        var repository = ArcanaSyntheticImageFactory.CreateRepository();
        using var service = new TestableCardService(repository.Object);
        await service.InitializeAsync();
        using var canvas = service.CreateCanvas();
        using var disposables = new DisposableBag();
        var state = await ReadSampleAsync();

        await service.RenderCardAsync(canvas, CreateContext(state), disposables);
        var cards = GenshinArcanaCardCatalog.Resolve(state);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Width, Is.EqualTo(2400));
            Assert.That(canvas.Height, Is.EqualTo(canvas.Width));
            for (var index = 0; index < cards.Count; index++)
            {
                var bounds = ArcanaSyntheticImageFactory.GetCellBounds(index);
                var expected = ArcanaSyntheticImageFactory.ExpectedRenderedColor(cards[index].Position - 1, cards[index].IsUnlock);
                var actual = canvas[bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2];
                Assert.That(actual, Is.EqualTo(expected), $"Card {index + 1} center/brightness");
                Assert.That(bounds.Y + bounds.Height + 90, Is.LessThanOrEqualTo(canvas.Height));
                Assert.That(HasWhiteText(canvas, bounds.X, bounds.Y + bounds.Height + 10, bounds.Width, 36),
                    Is.True, $"Card {index + 1} has its name below the artwork");
                Assert.That(TextMeasurer.MeasureBounds(cards[index].Name,
                        new TextOptions(service.CardNameFont)).Width, Is.LessThanOrEqualTo(bounds.Width),
                    $"Card name fits its column: {cards[index].Name}");
                Assert.That(HasWhiteText(canvas, bounds.X, bounds.Y + bounds.Height + 56, bounds.Width, 34),
                    Is.True, $"Card {index + 1} has a count below its name, including zero and Moon's three");
            }

            var first = ArcanaSyntheticImageFactory.GetCellBounds(18);
            var last = ArcanaSyntheticImageFactory.GetCellBounds(21);
            Assert.That(first.X, Is.EqualTo(canvas.Width - last.X - last.Width), "Last row is centered");
            Assert.That(HasWhiteText(canvas, canvas.Width - 340, canvas.Height - 90, 320, 80), Is.True,
                "Shared attribution is drawn in the bottom-right corner");
        });
    }

    [Test]
    public async Task GetCardAsync_WikiaAssetsAndUserSample_SavesSquarePreview()
    {
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin");
        foreach (var definition in GenshinArcanaCardCatalog.Cards)
        {
            var path = Path.Combine(assetsRoot, Path.GetFileName(definition.AssetKey));
            var lockedPath = Path.Combine(assetsRoot, Path.GetFileName(definition.LockedAssetKey));
            Assert.That(File.Exists(path), Is.True, $"Required artwork: {path}");
            Assert.That(File.Exists(lockedPath), Is.True, $"Required locked artwork: {lockedPath}");
            using var normal = Image.Load<Rgba32>(path);
            using var locked = Image.Load<Rgba32>(lockedPath);
            Assert.That((normal.Width, normal.Height), Is.EqualTo((320, 440)), definition.Name);
            Assert.That((locked.Width, locked.Height), Is.EqualTo((normal.Width, normal.Height)), definition.Name);
            AssertLockedVariant(normal, locked, definition.Name);
        }

        var repository = new Mock<IImageRepository>();
        repository.Setup(x => x.DownloadFileToStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                (Stream)File.OpenRead(Path.Combine(assetsRoot, Path.GetFileName(key))));
        using var service = new TestableCardService(repository.Object);
        await service.InitializeAsync();
        var state = await ReadSampleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(state.List, Has.Count.EqualTo(22));
            Assert.That(state.List.Count(x => x.IsUnlock), Is.EqualTo(12));
            Assert.That(state.List.Single(x => x.Name == "The Moon").UnlockNum, Is.EqualTo(3));
        });

        using var card = await service.GetCardAsync(CreateContext(state));
        var directory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "GenshinArcana_Data1_Square.jpg");
        await using (var output = File.Create(outputPath))
            await card.CopyToAsync(output);

        using var preview = Image.Load(outputPath);
        Assert.That((preview.Width, preview.Height), Is.EqualTo((2400, 2400)));
        foreach (var cardData in GenshinArcanaCardCatalog.Resolve(state))
            repository.Verify(x => x.DownloadFileToStreamAsync(cardData.AssetKey, It.IsAny<CancellationToken>()),
                Times.Once);
        TestContext.AddTestAttachment(outputPath, "Square Arcana preview using the user's collection state");
    }

    private static void AssertLockedVariant(Image<Rgba32> normal, Image<Rgba32> locked, string name)
    {
        var alphaMismatches = 0;
        var colorMismatches = 0;
        for (var y = 0; y < normal.Height; y++)
        {
            for (var x = 0; x < normal.Width; x++)
            {
                var source = normal[x, y];
                var target = locked[x, y];
                if (source.A != target.A) alphaMismatches++;
                if (Math.Abs(target.R - source.R / 2f) > 1 ||
                    Math.Abs(target.G - source.G / 2f) > 1 ||
                    Math.Abs(target.B - source.B / 2f) > 1) colorMismatches++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(alphaMismatches, Is.Zero, $"{name}: offline dimming preserves alpha");
            Assert.That(colorMismatches, Is.Zero, $"{name}: offline RGB brightness is 50% within rounding");
        });
    }

    private static bool HasWhiteText(Image<Rgba32> canvas, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                var pixel = canvas[column, row];
                if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240) return true;
            }
        }
        return false;
    }

    private static async Task<GenshinTarotCardState> ReadSampleAsync()
    {
        await using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
            "Arcana_TestData_1.json"));
        return await JsonSerializer.DeserializeAsync<GenshinTarotCardState>(input)
            ?? throw new InvalidOperationException("Missing Arcana fixture");
    }

    private static BaseCardGenerationContext<GenshinTarotCardState> CreateContext(GenshinTarotCardState state) =>
        new(1, state, new GameProfileDto { GameUid = "800000000", Nickname = "Test", Level = 60 });

    private sealed class TestableCardService(IImageRepository repository)
        : GenshinArcanaCardService(repository, NullLogger<GenshinArcanaCardService>.Instance,
            CardBenchmarkMetrics.Create())
    {
        public Font CardNameFont => Fonts.Small;

        public Image<Rgba32> CreateCanvas() => CreateBackground();
    }
}
