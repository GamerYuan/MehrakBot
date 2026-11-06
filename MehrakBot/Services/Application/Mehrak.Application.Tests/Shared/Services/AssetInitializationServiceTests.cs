#region

using System.Collections.Concurrent;
using Mehrak.Application.Genshin.Arcana;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SixLabors.ImageSharp;

#endregion

// Static-file seeding uses a mocked repository and does not require the Docker setup fixture.
namespace Mehrak.Application.Rendering.Tests.Shared.Services;

public class AssetInitializationServiceTests
{
    [Test]
    public async Task StartAsync_ExistingArcanaAssets_AreRefreshedWithoutReplacingOtherAssets()
    {
        var repository = CreateExistingRepository();
        var uploads = new ConcurrentDictionary<string, (int Width, int Height)>();
        repository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string?, CancellationToken>((key, stream, _, _) =>
            {
                var info = Image.Identify(stream);
                uploads[key] = (info.Width, info.Height);
            })
            .ReturnsAsync(true);
        using var provider = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        var service = new AssetInitializationService(provider);

        await service.StartAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploads, Has.Count.EqualTo(GenshinArcanaCardCatalog.Cards.Count * 2 + 1));
            Assert.That(uploads[FileNameFormat.Genshin.ArcanaBackgroundName], Is.EqualTo((2400, 2400)));
            foreach (var definition in GenshinArcanaCardCatalog.Cards)
            {
                Assert.That(uploads[definition.AssetKey], Is.EqualTo((320, 440)), definition.Name);
                Assert.That(uploads[definition.LockedAssetKey], Is.EqualTo((320, 440)), definition.Name);
            }
        });
        repository.Verify(x => x.FileExistsAsync(
            It.Is<string>(key => key.StartsWith("genshin/arcana_", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.UploadFileAsync(
            It.Is<string>(key => !key.StartsWith("genshin/arcana_", StringComparison.Ordinal)),
            It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.FileExistsAsync("genshin/theater_bg.png", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.FileExistsAsync(
            It.Is<string>(key => key.Contains("testassets", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void StartAsync_FailedArcanaRefresh_ReportsFailureInsteadOfKeepingStaleAssets()
    {
        var repository = CreateExistingRepository();
        repository.Setup(x => x.UploadFileAsync(FileNameFormat.Genshin.ArcanaBackgroundName, It.IsAny<Stream>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var provider = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        var service = new AssetInitializationService(provider);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain(FileNameFormat.Genshin.ArcanaBackgroundName));
    }

    private static Mock<IImageRepository> CreateExistingRepository()
    {
        var repository = new Mock<IImageRepository>();
        repository.Setup(x => x.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return repository;
    }
}
