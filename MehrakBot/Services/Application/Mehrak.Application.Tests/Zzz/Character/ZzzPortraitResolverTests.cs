using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Zzz.Character;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Wiki;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.ImageProcessor.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mehrak.Application.Tests.Zzz.Character;

[Parallelizable(ParallelScope.All)]
public class ZzzPortraitResolverTests
{
    private static readonly string PortraitPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz", "Portraits");

    [Test]
    public async Task ResolveAsync_EllenRejectsMindscapeAndCroppedHeaderBeforeRetryingAnotherLocale()
    {
        var avatar = await LoadYixuanAsync();
        avatar.Id = 1191;
        avatar.RoleVerticalPaintingUrl = "https://example.com/role_vertical_painting_1191.png";
        avatar.RoleSquareUrl = "https://example.com/role_square_avatar_1191.png";
        var wiki = new Mock<IApiService<JsonNode, WikiApiContext>>();
        wiki.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WikiApiContext c, CancellationToken _) => Result<JsonNode>.Success(
                c.Locale == WikiLocales.EN
                    ? Gallery("EllenMindscape.png", "EllenHeader.png")
                    : Gallery("EllenPortrait.png")));
        var fetcher = Fetcher(avatar.RoleVerticalPaintingUrl, "EllenReference.png");
        var matcher = RealMatcher();
        var resolver = new ZzzPortraitResolver(wiki.Object, fetcher.Object, matcher.Object, NullLogger.Instance);

        var result = await resolver.ResolveAsync(1, "29", avatar, true, false, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Data!.Base, Is.EqualTo("https://example.com/EllenPortrait.png"));
        wiki.Verify(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.CN), It.IsAny<CancellationToken>()), Times.Once);
        // Neither opaque mindscape art nor the cropped header reaches OpenCV.
        matcher.Verify(x => x.MatchAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResolveAsync_YixuanMatchesBaseAndOutfitInEitherOrderAndAcrossLocales(bool outfitFirst)
    {
        var avatar = await LoadYixuanAsync();
        Assert.That(avatar.GetPortraitSubId(), Is.EqualTo(3113711));
        Assert.That(avatar.ToImageName(), Is.EqualTo("zzz/portrait_1371_3113711.png"));
        var wiki = new Mock<IApiService<JsonNode, WikiApiContext>>();
        wiki.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WikiApiContext c, CancellationToken _) => Result<JsonNode>.Success(c.Locale == WikiLocales.EN
                ? Gallery(outfitFirst ? "YixuanOutfitPortrait.png" : "YixuanBasePortrait.png")
                : Gallery("animation.gif", outfitFirst ? "YixuanBasePortrait.png" : "YixuanOutfitPortrait.png")));
        var fetcher = Fetcher(avatar.RoleVerticalPaintingUrl, "YixuanOutfitReference.png");
        fetcher.Setup(x => x.FetchBytesAsync(ZzzPortraitResolver.GetBaseReferenceUrl(avatar), It.IsAny<CancellationToken>()))
            .ReturnsAsync(File.ReadAllBytes(Path.Combine(PortraitPath, "YixuanBaseReference.png")));
        var matcher = RealMatcher();
        var resolver = new ZzzPortraitResolver(wiki.Object, fetcher.Object, matcher.Object, NullLogger.Instance);

        var result = await resolver.ResolveAsync(1, "752", avatar, true, true, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Data!.Base, Is.EqualTo("https://example.com/YixuanBasePortrait.png"));
            Assert.That(result.Data!.Outfit, Is.EqualTo("https://example.com/YixuanOutfitPortrait.png"));
        }
        fetcher.Verify(x => x.FetchBytesAsync(It.Is<string>(s => s.EndsWith(".gif")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ResolveAsync_DeduplicatesRejectedCandidatesAndReturnsFailureWhenNothingMatches()
    {
        var avatar = await LoadYixuanAsync();
        var wiki = new Mock<IApiService<JsonNode, WikiApiContext>>();
        wiki.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JsonNode>.Success(Gallery("EllenPortrait.png", "mirror.png", "EllenPortrait.png", "animation.gif")));
        var fetcher = Fetcher(avatar.RoleVerticalPaintingUrl, "YixuanOutfitReference.png");
        fetcher.Setup(x => x.FetchBytesAsync("https://example.com/mirror.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(File.ReadAllBytes(Path.Combine(PortraitPath, "EllenPortrait.png")));
        var matcher = new Mock<IPortraitMatcher>();
        matcher.Setup(x => x.MatchAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, 0f));
        var resolver = new ZzzPortraitResolver(wiki.Object, fetcher.Object, matcher.Object, NullLogger.Instance);

        var result = await resolver.ResolveAsync(1, "752", avatar, false, true, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        wiki.Verify(x => x.GetAsync(It.IsAny<WikiApiContext>(), It.IsAny<CancellationToken>()), Times.Exactly(Enum.GetValues<WikiLocales>().Length));
        fetcher.Verify(x => x.FetchBytesAsync("https://example.com/EllenPortrait.png", It.IsAny<CancellationToken>()), Times.Once);
        matcher.Verify(x => x.MatchAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ResolveAsync_PropagatesCallerCancellation()
    {
        var avatar = await LoadYixuanAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var resolver = new ZzzPortraitResolver(Mock.Of<IApiService<JsonNode, WikiApiContext>>(),
            Mock.Of<IImageFetcher>(), Mock.Of<IPortraitMatcher>(), NullLogger.Instance);
        Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(1, "752", avatar, true, true, cts.Token));
    }

    [TestCase("EllenMindscape.png", false)]
    [TestCase("EllenHeader.png", false)]
    [TestCase("EllenPortrait.png", true)]
    [TestCase("YixuanBasePortrait.png", true)]
    [TestCase("YixuanOutfitPortrait.png", true)]
    public void IsPortraitCandidate_FiltersActualPixels(string name, bool expected)
    {
        Assert.That(ZzzPortraitResolver.IsPortraitCandidate(File.ReadAllBytes(Path.Combine(PortraitPath, name))), Is.EqualTo(expected));
    }

    [Test]
    public void IsPortraitCandidate_RejectsOpaquePngEmptyCutoutInvalidBytesAndDisguisedJpeg()
    {
        using var image = new Image<Rgba32>(16, 16, new Rgba32(100, 100, 100, 255));
        using var png = new MemoryStream();
        image.SaveAsPng(png);
        using var jpeg = new MemoryStream();
        image.SaveAsJpeg(jpeg);
        using var empty = new Image<Rgba32>(16, 16);
        using var emptyPng = new MemoryStream();
        empty.SaveAsPng(emptyPng);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ZzzPortraitResolver.IsPortraitCandidate(png.ToArray()), Is.False);
            Assert.That(ZzzPortraitResolver.IsPortraitCandidate(jpeg.ToArray()), Is.False);
            Assert.That(ZzzPortraitResolver.IsPortraitCandidate(emptyPng.ToArray()), Is.False);
            Assert.That(ZzzPortraitResolver.IsPortraitCandidate([1, 2, 3]), Is.False);
        }
    }

    private static Mock<IPortraitMatcher> RealMatcher()
    {
        var engine = new PortraitImageMatcher();
        var mock = new Mock<IPortraitMatcher>();
        mock.Setup(x => x.MatchAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] reference, byte[] candidate, CancellationToken _) => engine.Match(reference, candidate));
        return mock;
    }

    private static Mock<IImageFetcher> Fetcher(string referenceUrl, string referenceFile)
    {
        var mock = new Mock<IImageFetcher>();
        mock.Setup(x => x.FetchBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => File.ReadAllBytes(Path.Combine(PortraitPath,
                url == referenceUrl ? referenceFile : Path.GetFileName(new Uri(url).AbsolutePath))));
        return mock;
    }

    private static async Task<ZzzAvatarData> LoadYixuanAsync()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz", "Yixuan_TestData.json"));
        return JsonSerializer.Deserialize<ZzzFullAvatarData>(json)!.AvatarList[0];
    }

    private static JsonNode Gallery(params string[] names) => new JsonObject
    {
        ["data"] = new JsonObject
        {
            ["page"] = new JsonObject
            {
                ["modules"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["components"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["component_id"] = "gallery_character",
                                ["data"] = JsonSerializer.Serialize(new { list = names.Select(name => new { img = "https://example.com/" + name }) })
                            }
                        }
                    }
                }
            }
        }
    };
}
