using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Moq;

namespace Mehrak.Application.Tests.Shared;

[TestFixture]
public sealed class CardCacheFingerprintTests
{
    [Test]
    public void UnchangedInputs_ReuseTheSameName()
    {
        var profile = new GameProfileDto { GameUid = "uid", Nickname = "Traveler", Level = 60 };

        var first = TestAttachmentService.BuildName("genshin", "abyss", "v1", new { Score = 100 }, profile,
            new { Floor = 12, ConstMap = new SortedDictionary<int, int> { [1] = 6 } });
        var second = TestAttachmentService.BuildName("genshin", "abyss", "v1", new { Score = 100 }, profile,
            new { Floor = 12, ConstMap = new SortedDictionary<int, int> { [1] = 6 } });

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void ProfileAndAuxiliaryInputs_ChangeTheName()
    {
        var profile = new GameProfileDto { GameUid = "uid", Nickname = "Traveler", Level = 60 };
        var data = new { Score = 100 };
        var inputs = new { CharMap = new[] { new { Id = 1, Level = 60, Rank = 2 } } };

        var profileChanged = TestAttachmentService.BuildName("zzz", "tower", "v1", data,
            new GameProfileDto { GameUid = profile.GameUid, Nickname = "Aether", Level = profile.Level }, inputs);
        var auxiliaryChanged = TestAttachmentService.BuildName("zzz", "tower", "v1", data, profile,
            new { CharMap = new[] { new { Id = 1, Level = 61, Rank = 2 } } });
        var rendererChanged = TestAttachmentService.BuildName("zzz", "tower", "v2", data, profile, inputs);

        Assert.Multiple(() =>
        {
            Assert.That(profileChanged, Is.Not.EqualTo(TestAttachmentService.BuildName("zzz", "tower", "v1", data, profile, inputs)));
            Assert.That(auxiliaryChanged, Is.Not.EqualTo(TestAttachmentService.BuildName("zzz", "tower", "v1", data, profile, inputs)));
            Assert.That(rendererChanged, Is.Not.EqualTo(TestAttachmentService.BuildName("zzz", "tower", "v1", data, profile, inputs)));
        });
    }

    [Test]
    public async Task PortraitFallback_UsesStockIdentity_AndAllowsCustomRecovery()
    {
        var uploadId = Guid.NewGuid();
        var active = new ActivePortrait(
            "portraits/custom.jpg",
            uploadId,
            new UserPortraitConfigDto { TargetScale = 1.2f });
        var stockConfig = new CharacterPortraitConfig { OffsetX = 10 };
        var portraitService = new Mock<IUserPortraitService>();
        portraitService.Setup(x => x.GetPortraitImageAsync(
                1,
                active.Key,
                uploadId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null)
            .Callback(() => { });

        var fallback = await PortraitResolutionHelper.ResolveActivePortraitAsync(
            portraitService.Object, 1, active, () => Task.FromResult<CharacterPortraitConfig?>(stockConfig));

        portraitService.Setup(x => x.GetPortraitImageAsync(
                1,
                active.Key,
                uploadId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentDownloadResult(new MemoryStream([1]), "image/jpeg"));
        var recovered = await PortraitResolutionHelper.ResolveActivePortraitAsync(
            portraitService.Object, 1, active, () => Task.FromResult<CharacterPortraitConfig?>(stockConfig));

        var profile = new GameProfileDto { GameUid = "uid", Nickname = "Player", Level = 60 };
        var stockName = TestAttachmentService.BuildName("genshin", "character", "v1", "data", profile,
            new { Portrait = fallback.Config });
        var customName = TestAttachmentService.BuildName("genshin", "character", "v1", "data", profile,
            new { Portrait = active });
        var changedStockName = TestAttachmentService.BuildName("genshin", "character", "v1", "data", profile,
            new { Portrait = new CharacterPortraitConfig { OffsetX = 11 } });

        Assert.Multiple(() =>
        {
            Assert.That(fallback.UsedStockFallback, Is.True);
            Assert.That(recovered.UsedStockFallback, Is.False);
            Assert.That(recovered.ImageStream, Is.Not.Null);
            Assert.That(stockName, Is.Not.EqualTo(customName));
            Assert.That(stockName, Is.Not.EqualTo(changedStockName));
        });

        await recovered.ImageStream!.DisposeAsync();
    }

    private sealed class TestAttachmentService : BaseAttachmentApplicationService
    {
        private TestAttachmentService() : base(null!, null!, null!, null!)
        { }

        public static string BuildName<TData>(
            string game,
            string mode,
            string rendererVersion,
            TData data,
            GameProfileDto profile,
            object? effectiveInputs = null) =>
            GetCardFileName(game, mode, rendererVersion, data, profile, effectiveInputs);

        protected override Task<CommandResult> ExecuteCommandAsync(
            IApplicationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CommandResult.Success());
    }
}
