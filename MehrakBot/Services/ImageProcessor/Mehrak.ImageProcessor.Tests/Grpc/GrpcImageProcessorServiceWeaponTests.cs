using Google.Protobuf;
using Grpc.Core;
using Mehrak.Domain.Protobuf;
using Mehrak.ImageProcessor.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.ImageProcessor.Tests.Grpc;

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class GrpcImageProcessorServiceWeaponTests
{
    private Mock<GenshinWeaponImageProcessor> m_MockWeaponProcessor;
    private Mock<INsfwClassifier> m_MockClassifier;
    private Mock<ILogger<GrpcImageProcessorService>> m_MockLogger;
    private GrpcImageProcessorService m_Service;

    [SetUp]
    public void Setup()
    {
        m_MockWeaponProcessor = new Mock<GenshinWeaponImageProcessor>();
        m_MockClassifier = new Mock<INsfwClassifier>();
        m_MockLogger = new Mock<ILogger<GrpcImageProcessorService>>();
        m_Service = new GrpcImageProcessorService(
            m_MockClassifier.Object,
            m_MockWeaponProcessor.Object,
            m_MockLogger.Object);
    }

    [Test]
    public void ProcessWeaponImage_WithValidImages_ReturnsProcessedImage()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var processedBytes = new byte[] { 0x01, 0x02, 0x03 };

        m_MockWeaponProcessor.Setup(p => p.ProcessImage(It.IsAny<IEnumerable<Stream>>()))
            .Returns<IEnumerable<Stream>>(streams =>
            {
                streams.ToList().ForEach(s => s.Dispose());
                return new MemoryStream(processedBytes);
            });

        var request = new ProcessWeaponImageRequest();
        request.Images.Add(ByteString.CopyFrom(imageData));
        request.Images.Add(ByteString.CopyFrom(imageData));

        var result = m_Service.ProcessWeaponImage(request, CreateServerCallContext());

        Assert.That(result.Result.ProcessedImage.IsEmpty, Is.False);
        Assert.That(result.Result.ProcessedImage.ToByteArray(), Is.EqualTo(processedBytes));
    }

    [Test]
    public void ProcessWeaponImage_WhenProcessorReturnsNull_ReturnsEmptyResponse()
    {
        m_MockWeaponProcessor.Setup(p => p.ProcessImage(It.IsAny<IEnumerable<Stream>>()))
            .Returns<IEnumerable<Stream>>(streams =>
            {
                streams.ToList().ForEach(s => s.Dispose());
                return Stream.Null;
            });

        var request = new ProcessWeaponImageRequest();
        request.Images.Add(ByteString.CopyFrom(new byte[] { 1 }));
        request.Images.Add(ByteString.CopyFrom(new byte[] { 2 }));

        var result = m_Service.ProcessWeaponImage(request, CreateServerCallContext());

        Assert.That(result.Result.ProcessedImage, Is.EqualTo(ByteString.Empty));
    }

    [Test]
    public void ProcessWeaponImage_WhenProcessorThrows_LogsAndThrowsRpcException()
    {
        m_MockWeaponProcessor.Setup(p => p.ProcessImage(It.IsAny<IEnumerable<Stream>>()))
            .Returns<IEnumerable<Stream>>(streams =>
            {
                streams.ToList().ForEach(s => s.Dispose());
                throw new ArgumentException("At least two images are required");
            });

        var request = new ProcessWeaponImageRequest();
        request.Images.Add(ByteString.CopyFrom(new byte[] { 1 }));

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
        {
            await m_Service.ProcessWeaponImage(request, CreateServerCallContext());
        });

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Internal));
        Assert.That(ex.Status.Detail, Is.EqualTo("Weapon image processing failed."));

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error processing weapon image")),
                It.IsAny<ArgumentException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ProcessWeaponImage_DisposesInputStreamsOnSuccess()
    {
        var disposedStreams = new List<MemoryStream>();

        m_MockWeaponProcessor.Setup(p => p.ProcessImage(It.IsAny<IEnumerable<Stream>>()))
            .Returns<IEnumerable<Stream>>(streams =>
            {
                var streamList = streams.ToList();
                foreach (var s in streamList)
                {
                    if (s is MemoryStream ms)
                    {
                        disposedStreams.Add(ms);
                    }
                }
                return new MemoryStream(new byte[] { 0x01 });
            });

        var request = new ProcessWeaponImageRequest();
        request.Images.Add(ByteString.CopyFrom(new byte[] { 1 }));
        request.Images.Add(ByteString.CopyFrom(new byte[] { 2 }));

        _ = m_Service.ProcessWeaponImage(request, CreateServerCallContext());

        foreach (var ms in disposedStreams)
        {
            Assert.That(ms.CanRead, Is.False, "Stream should be disposed after processing");
        }
    }

    [Test]
    public void ProcessWeaponImage_DisposesInputStreamsOnException()
    {
        var inputStreams = new List<MemoryStream>();

        m_MockWeaponProcessor.Setup(p => p.ProcessImage(It.IsAny<IEnumerable<Stream>>()))
            .Returns<IEnumerable<Stream>>(streams =>
            {
                inputStreams = streams.Cast<MemoryStream>().ToList();
                throw new InvalidOperationException("Processing failed");
            });

        var request = new ProcessWeaponImageRequest();
        request.Images.Add(ByteString.CopyFrom(new byte[] { 1 }));
        request.Images.Add(ByteString.CopyFrom(new byte[] { 2 }));

        Assert.ThrowsAsync<RpcException>(async () =>
        {
            await m_Service.ProcessWeaponImage(request, CreateServerCallContext());
        });

        foreach (var ms in inputStreams)
        {
            Assert.That(ms.CanRead, Is.False, "Stream should be disposed even on exception");
        }
    }

    private static ServerCallContext CreateServerCallContext()
    {
        return new TestServerCallContext();
    }

    private class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "ProcessWeaponImage";
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
