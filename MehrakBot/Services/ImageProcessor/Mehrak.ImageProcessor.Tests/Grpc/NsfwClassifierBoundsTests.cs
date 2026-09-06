using Google.Protobuf;
using Grpc.Core;
using Mehrak.Domain.Protobuf;
using Mehrak.ImageProcessor.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.ImageProcessor.Tests.Grpc;

/// <summary>
/// Pixel-budget regression tests (finding F2). All oversized fixtures carry only
/// container-header dimensions in tiny byte arrays: rejection must happen before any
/// native pixel allocation. No OOM probes are performed.
/// </summary>
[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class NsfwClassifierBoundsTests
{
    private Mock<INsfwClassifier> m_MockClassifier = null!;
    private Mock<GenshinWeaponImageProcessor> m_MockWeaponProcessor = null!;
    private Mock<PortraitImageMatcher> m_MockPortraitMatcher = null!;
    private GrpcImageProcessorService m_Service = null!;

    [SetUp]
    public void Setup()
    {
        m_MockClassifier = new Mock<INsfwClassifier>();
        m_MockWeaponProcessor = new Mock<GenshinWeaponImageProcessor>();
        m_MockPortraitMatcher = new Mock<PortraitImageMatcher>();
        m_Service = new GrpcImageProcessorService(
            m_MockClassifier.Object,
            m_MockWeaponProcessor.Object,
            m_MockPortraitMatcher.Object,
            Mock.Of<ILogger<GrpcImageProcessorService>>());
    }

    [Test]
    public void TryGetImageGeometry_OversizedPngMetadata_ReportsHugeDimensions()
    {
        var png = BuildPng(100000, 100000, animated: false);

        Assert.That(png.Length, Is.LessThan(1024), "Fixture must be metadata-only, not pixels.");
        Assert.That(NsfwClassifier.TryGetImageGeometry(png, out var geometry), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(geometry.Format, Is.EqualTo("png"));
            Assert.That(geometry.Width, Is.EqualTo(100000));
            Assert.That(geometry.Height, Is.EqualTo(100000));
        });
        Assert.Throws<ArgumentException>(() => NsfwClassifier.ValidatePixelBudget(geometry));
    }

    [Test]
    public void TryGetImageGeometry_OversizedJpegMetadata_IsOverBudget()
    {
        // JPEG SOF dimensions are 16-bit; 60000x60000 is representable and over budget.
        var jpeg = BuildJpeg(60000, 60000);

        Assert.That(jpeg.Length, Is.LessThan(1024), "Fixture must be metadata-only, not pixels.");
        Assert.That(NsfwClassifier.TryGetImageGeometry(jpeg, out var geometry), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(geometry.Format, Is.EqualTo("jpeg"));
            Assert.That(geometry.Width, Is.EqualTo(60000));
            Assert.That(geometry.Height, Is.EqualTo(60000));
        });
        Assert.Throws<ArgumentException>(() => NsfwClassifier.ValidatePixelBudget(geometry));
    }

    [Test]
    public void TryGetImageGeometry_OversizedPixelCount_IsOverBudget()
    {
        // 2000x9000 fits each dimension cap but exceeds the total pixel budget.
        var png = BuildPng(2000, 9000, animated: false);

        Assert.That(NsfwClassifier.TryGetImageGeometry(png, out var geometry), Is.True);
        Assert.Throws<ArgumentException>(() => NsfwClassifier.ValidatePixelBudget(geometry));
    }

    [Test]
    public void TryGetImageGeometry_AnimatedPngMetadata_ReportsMultipleFrames()
    {
        var apng = BuildPng(64, 64, animated: true);

        Assert.That(NsfwClassifier.TryGetImageGeometry(apng, out var geometry), Is.True);
        Assert.That(geometry.Frames, Is.GreaterThan(1));
        Assert.Throws<ArgumentException>(() => NsfwClassifier.ValidatePixelBudget(geometry));
    }

    [Test]
    public void ValidatePixelBudget_SmallImage_Passes()
    {
        Assert.DoesNotThrow(() =>
            NsfwClassifier.ValidatePixelBudget(new UploadImageGeometry("png", 1024, 768, 1)));
    }

    [Test]
    public void TryGetImageGeometry_UnknownBytes_ReturnsFalse()
    {
        Assert.That(NsfwClassifier.TryGetImageGeometry(new byte[] { 1, 2, 3, 4 }, out _), Is.False);
    }

    [Test]
    public void ClassifyImage_OversizedPayload_ReturnsInvalidArgumentWithoutClassifying()
    {
        var request = new ClassifyRequest
        {
            ImageData = ByteString.CopyFrom(new byte[NsfwClassifier.MaxImageBytes + 1])
        };

        var ex = Assert.Throws<RpcException>(() =>
            m_Service.ClassifyImage(request, CreateServerCallContext()).GetAwaiter().GetResult());

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        m_MockClassifier.Verify(c => c.Classify(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public void ClassifyImage_OverBudgetContent_ReturnsInvalidArgument()
    {
        m_MockClassifier.Setup(c => c.Classify(It.IsAny<byte[]>()))
            .Throws(new ArgumentException("Image exceeds the pixel budget."));
        var request = new ClassifyRequest
        {
            ImageData = ByteString.CopyFrom(BuildPng(100000, 100000, animated: false))
        };

        var ex = Assert.Throws<RpcException>(() =>
            m_Service.ClassifyImage(request, CreateServerCallContext()).GetAwaiter().GetResult());

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    private static ServerCallContext CreateServerCallContext() => new TestServerCallContext();

    private sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "ClassifyImage";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "localhost:5000";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata RequestHeadersCore => new();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());
        protected override Status StatusCore { get; set; }
        protected override Metadata ResponseTrailersCore => new();
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotImplementedException();
    }

    /// <summary>Minimal PNG with caller-chosen IHDR dimensions and an optional acTL chunk.</summary>
    private static byte[] BuildPng(int width, int height, bool animated)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        using (var ihdr = new MemoryStream())
        {
            WriteUInt32BE(ihdr, unchecked((uint)width));
            WriteUInt32BE(ihdr, unchecked((uint)height));
            ihdr.Write(new byte[] { 8, 2, 0, 0, 0 });
            WriteChunk(png, "IHDR", ihdr.ToArray());
        }
        if (animated)
            WriteChunk(png, "acTL", new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 });
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Minimal JPEG carrying only SOI + SOF0 dimensions (metadata, no scan data).</summary>
    private static byte[] BuildJpeg(int width, int height)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write(new byte[] { 0xFF, 0xD8 }); // SOI
        jpeg.Write(new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 0x08 }); // SOF0, length 11, precision 8
        jpeg.Write(new[] { (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width });
        jpeg.Write(new byte[] { 0x01, 0x01, 0x11, 0x00 }); // one component
        jpeg.Write(new byte[] { 0xFF, 0xD9 }); // EOI
        return jpeg.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        WriteUInt32BE(stream, unchecked((uint)data.Length));
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteUInt32BE(stream, ComputeCrc32([.. typeBytes, .. data]));
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    }

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var k = 0; k < 8; k++)
                entry = (entry & 1) != 0 ? 0xEDB88320u ^ (entry >> 1) : entry >> 1;
            table[i] = entry;
        }
        return table;
    }

    private static uint ComputeCrc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
