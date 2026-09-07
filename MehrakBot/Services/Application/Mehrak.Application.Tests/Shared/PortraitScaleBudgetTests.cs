using Mehrak.Application.Shared.Renderers;
using SixLabors.ImageSharp;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.User.Abstractions;
using Mehrak.Domain.User.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Mehrak.Application.Tests.Shared;

/// <summary>
/// Renderer allocation-budget tests: scale-derived portrait resize
/// widths must stay within the maximum dimension and pixel budget for both user
/// and stock portraits. Pure unit tests over the shared budget helper.
/// </summary>
[TestFixture]
public class PortraitScaleBudgetTests
{
    private static int Budget(int sourceWidth, int sourceHeight, float scale) =>
        CharacterCardServiceBase<object>.ComputePortraitTargetWidth(sourceWidth, sourceHeight, scale);

    private static Size BudgetSize(int sourceWidth, int sourceHeight, float scale) =>
        CharacterCardServiceBase<object>.ComputePortraitTargetSize(sourceWidth, sourceHeight, scale);

    [Test]
    public void OrdinaryScale_PassesThrough()
    {
        Assert.That(Budget(1000, 2000, 2f), Is.EqualTo(2000));
    }

    [Test]
    public void ExcessiveScale_ClampedToMaxDimension()
    {
        Assert.That(Budget(4096, 4096, 10f), Is.EqualTo(4096));
    }

    [Test]
    public void LargeSource_ClampedByPixelBudget()
    {
        // The height cap is stricter than the pixel cap for this 1:2 source.
        Assert.That(Budget(1500, 3000, 10f), Is.EqualTo(2048));
    }

    [Test]
    public void TallSource_OutputStaysWithinPixelBudget()
    {
        var width = Budget(500, 4000, 10f);
        var height = (double)width * 4000 / 500;
        Assert.Multiple(() =>
        {
            Assert.That(width, Is.LessThanOrEqualTo(4096));
            Assert.That((long)width * (long)height, Is.LessThanOrEqualTo(16_777_216));
        });
    }

    [Test]
    public void TinyResult_FloorsAtOnePixel()
    {
        Assert.That(Budget(100, 100, 0.01f), Is.EqualTo(1));
    }

    [Test]
    public void DefaultWidthScale_PreservesAspectRatio()
    {
        Assert.That(BudgetSize(1000, 500, 0.6f), Is.EqualTo(new Size(600, 300)));
    }

    [Test]
    public void ExtremeAspectRatio_DefaultPath_IsDimensionAndPixelSafe()
    {
        var size = BudgetSize(1000, 100_000, 0.6f);

        Assert.Multiple(() =>
        {
            Assert.That(size.Width, Is.LessThanOrEqualTo(4096));
            Assert.That(size.Height, Is.LessThanOrEqualTo(4096));
            Assert.That((long)size.Width * size.Height, Is.LessThanOrEqualTo(16_777_216));
        });
    }

    [Test]
    public void ExplicitScale_RoundingCannotCrossPixelBudget()
    {
        var size = BudgetSize(4095, 4095, 1.01f);

        Assert.That((long)size.Width * size.Height, Is.LessThanOrEqualTo(16_777_216));
    }

    [TestCase(0, 100)]
    [TestCase(100, 0)]
    [TestCase(-5, 100)]
    public void DegenerateSource_FloorsAtOnePixel(int sourceWidth, int sourceHeight)
    {
        Assert.That(Budget(sourceWidth, sourceHeight, 2f), Is.EqualTo(1));
    }

    [Test]
    public async Task DefaultScale_LoadPortraitAsync_ClampsTallPortrait()
    {
        using var source = new Image<Rgba32>(100, 1000);
        await using var sourceStream = new MemoryStream();
        await source.SaveAsPngAsync(sourceStream);
        sourceStream.Position = 0;

        var context = new Mock<ICardGenerationContext<object>>();
        context.SetupGet(x => x.PortraitImageStream).Returns(sourceStream);
        context.SetupGet(x => x.PortraitConfig).Returns((CharacterPortraitConfig?)null);
        using var disposables = new DisposableBag();

        using var portrait = await new TestCharacterCardService().LoadPortraitAsync(
            context.Object, disposables);

        Assert.That(portrait.Size, Is.EqualTo(new Size(410, 4096)));
    }

    private sealed class TestCharacterCardService : CharacterCardServiceBase<object>
    {
        public TestCharacterCardService() : base(
            "Test",
            Mock.Of<IImageRepository>(),
            NullLogger.Instance,
            Mock.Of<IApplicationMetrics>(),
            null!)
        {
        }

        protected override int DefaultPortraitWidth => 600;

        protected override IResampler PortraitResampler => KnownResamplers.NearestNeighbor;

        public override Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task RenderCardAsync(
            Image<Rgba32> background,
            ICardGenerationContext<object> context,
            DisposableBag disposables,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Image> LoadPortraitAsync(
            ICardGenerationContext<object> context,
            DisposableBag disposables) =>
            base.LoadPortraitAsync(context, () => Task.FromResult<Image>(new Image<Rgba32>(1, 1)), disposables);
    }
}
