using Google.Protobuf;
using Grpc.Core;
using Mehrak.Domain.Protobuf;
using Mehrak.ImageProcessor.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;
using OpenCvSharp;

namespace Mehrak.ImageProcessor.Tests.Grpc;

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class GrpcImageProcessorServiceMatchTests
{
    private Mock<PortraitImageMatcher> m_MockPortraitMatcher;
    private GrpcImageProcessorService m_Service;

    [SetUp]
    public void Setup()
    {
        m_MockPortraitMatcher = new Mock<PortraitImageMatcher>();
        m_Service = new GrpcImageProcessorService(
            Mock.Of<INsfwClassifier>(),
            Mock.Of<GenshinWeaponImageProcessor>(),
            m_MockPortraitMatcher.Object,
            Mock.Of<ILogger<GrpcImageProcessorService>>());
    }

    [Test]
    public void MatchImage_WithMatchingImages_ReturnsMatchResult()
    {
        m_MockPortraitMatcher
            .Setup(p => p.Match(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Returns((true, 0.87f));

        var request = new MatchImageRequest
        {
            ReferenceImage = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            CandidateImage = ByteString.CopyFrom(new byte[] { 4, 5, 6 })
        };

        var result = m_Service.MatchImage(request, CreateServerCallContext());

        Assert.That(result.Result.IsMatch, Is.True);
        Assert.That(result.Result.Confidence, Is.EqualTo(0.87f));
        m_MockPortraitMatcher.Verify(
            p => p.Match(It.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3 })),
                It.Is<byte[]>(b => b.SequenceEqual(new byte[] { 4, 5, 6 }))), Times.Once);
    }

    [Test]
    public void MatchImage_WithEmptyImage_ReturnsNoMatch()
    {
        var request = new MatchImageRequest
        {
            ReferenceImage = ByteString.Empty,
            CandidateImage = ByteString.CopyFrom(new byte[] { 4, 5, 6 })
        };

        var result = m_Service.MatchImage(request, CreateServerCallContext());

        Assert.That(result.Result.IsMatch, Is.False);
        Assert.That(result.Result.Confidence, Is.EqualTo(0f));
        m_MockPortraitMatcher.Verify(p => p.Match(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public void MatchImage_WhenMatcherThrows_LogsAndThrowsRpcException()
    {
        m_MockPortraitMatcher
            .Setup(p => p.Match(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Failed"));

        var request = new MatchImageRequest
        {
            ReferenceImage = ByteString.CopyFrom(new byte[] { 1 }),
            CandidateImage = ByteString.CopyFrom(new byte[] { 2 })
        };

        var ex = Assert.Throws<RpcException>(() => m_Service.MatchImage(request, CreateServerCallContext()));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Internal));
        Assert.That(ex.Status.Detail, Is.EqualTo("Image matching failed."));
    }

    private static ServerCallContext CreateServerCallContext()
    {
        return new TestServerCallContext();
    }

    private class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "MatchImage";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "localhost:5000";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata RequestHeadersCore => new();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new AuthContext(null, new Dictionary<string, List<AuthProperty>>());
        protected override Status StatusCore { get; set; }
        protected override Metadata ResponseTrailersCore => new();
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();
    }
}

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class PortraitImageMatcherTests
{
    private PortraitImageMatcher m_Matcher;

    [SetUp]
    public void Setup()
    {
        m_Matcher = new PortraitImageMatcher();
    }

    [Test]
    public void Match_CropOfPortrait_ReturnsMatch()
    {
        using var portrait = CreateStructuredImage(800, 1200);
        using var crop = portrait.Clone(new Rect(150, 250, 350, 350));
        using var enlarged = new Mat();
        Cv2.Resize(crop, enlarged, new Size(700, 700), interpolation: InterpolationFlags.Cubic);

        var (isMatch, confidence) = m_Matcher.Match(MatToBytes(enlarged), MatToBytes(portrait));

        Assert.That(isMatch, Is.True, $"Expected match but got confidence {confidence:F2}");
        Assert.That(confidence, Is.GreaterThan(0.4f));
    }

    [Test]
    public void Match_UnrelatedImages_ReturnsNoMatch()
    {
        using var portrait = CreateStructuredImage(800, 1200);
        using var other = CreateStructuredImage(600, 600, seed: 42);

        var (isMatch, _) = m_Matcher.Match(MatToBytes(other), MatToBytes(portrait));

        Assert.That(isMatch, Is.False);
    }

    /// <summary>
    /// Builds a grayscale image with a deterministic, feature-rich scene: a solid
    /// background with a filled rect, a circle, crossing lines, and a grid of small
    /// squares so AKAZE has plenty of corner-like keypoints.
    /// </summary>
    private static Mat CreateStructuredImage(int width, int height, int seed = 7)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, new Scalar(180));

        Cv2.Rectangle(mat, new Rect(width / 4, height / 4, width / 2, height / 2), new Scalar(60), -1);
        Cv2.Circle(mat, new Point(width / 2, height / 2), width / 6, new Scalar(240), -1);
        Cv2.Line(mat, new Point(0, 0), new Point(width, height), new Scalar(20), 8);
        Cv2.Line(mat, new Point(width, 0), new Point(0, height), new Scalar(250), 8);
        Cv2.Rectangle(mat, new Rect(width / 8, height / 8, width / 10, height / 10), new Scalar(255), -1);
        Cv2.Rectangle(mat, new Rect(width * 7 / 8, height * 7 / 8, width / 10, height / 10), new Scalar(0), -1);

        for (var y = height / 3; y < height; y += height / 12)
        {
            for (var x = 0; x < width; x += width / 10)
            {
                Cv2.Rectangle(mat, new Rect(x, y, width / 16, height / 16),
                    new Scalar((x + y + seed) % 255), -1);
            }
        }

        if (seed != 7)
        {
            Cv2.Rectangle(mat, new Rect(0, 0, width / 3, height), new Scalar(30), -1);
            Cv2.Circle(mat, new Point(width / 5, height / 5), width / 8, new Scalar(255), -1);
        }

        return mat;
    }

    private static byte[] MatToBytes(Mat mat)
    {
        return mat.ImEncode(".png");
    }
}
