using Google.Protobuf;
using Grpc.Core;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Protobuf;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Shared.Services;

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class WeaponImageProcessorGrpcClientTests
{
    private Mock<ImageProcessorService.ImageProcessorServiceClient> m_MockClient;
    private Mock<ILogger<WeaponImageProcessorGrpcClient>> m_MockLogger;
    private WeaponImageProcessorGrpcClient m_Client;

    [SetUp]
    public void Setup()
    {
        m_MockClient = new Mock<ImageProcessorService.ImageProcessorServiceClient>();
        m_MockLogger = new Mock<ILogger<WeaponImageProcessorGrpcClient>>();
        m_Client = new WeaponImageProcessorGrpcClient(m_MockClient.Object, m_MockLogger.Object);
    }

    [Test]
    public void ShouldProcess_ReturnsTrue()
    {
        Assert.That(m_Client.ShouldProcess, Is.True);
    }

    [Test]
    public void ProcessImage_WithValidImages_ReturnsProcessedStream()
    {
        var imageData = new byte[] { 1, 2, 3, 4, 5 };
        var expectedResponse = new ProcessWeaponImageResponse
        {
            ProcessedImage = ByteString.CopyFrom(imageData)
        };

        m_MockClient.Setup(c => c.ProcessWeaponImage(
                It.IsAny<ProcessWeaponImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(expectedResponse);

        using var stream1 = new MemoryStream(new byte[] { 10, 20 });
        using var stream2 = new MemoryStream(new byte[] { 30, 40 });

        var result = m_Client.ProcessImage([stream1, stream2]);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.EqualTo(Stream.Null));

        var resultBytes = new byte[result.Length];
        result.Position = 0;
        result.Read(resultBytes, 0, resultBytes.Length);
        Assert.That(resultBytes, Is.EqualTo(imageData));

        m_MockClient.Verify(c => c.ProcessWeaponImage(
            It.Is<ProcessWeaponImageRequest>(r => r.Images.Count == 2),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ProcessImage_WithEmptyResponse_ReturnsStreamNull()
    {
        var emptyResponse = new ProcessWeaponImageResponse();

        m_MockClient.Setup(c => c.ProcessWeaponImage(
                It.IsAny<ProcessWeaponImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(emptyResponse);

        using var stream1 = new MemoryStream(new byte[] { 10, 20 });
        using var stream2 = new MemoryStream(new byte[] { 30, 40 });

        var result = m_Client.ProcessImage([stream1, stream2]);

        Assert.That(result, Is.EqualTo(Stream.Null));
    }

    [Test]
    public void ProcessImage_SerializesImagesCorrectly()
    {
        var image1Bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var image2Bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        ProcessWeaponImageRequest? capturedRequest = null;
        m_MockClient.Setup(c => c.ProcessWeaponImage(
                It.IsAny<ProcessWeaponImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProcessWeaponImageRequest, Metadata, DateTime?, CancellationToken>(
                (req, _, _, _) => capturedRequest = req)
            .Returns(new ProcessWeaponImageResponse
            {
                ProcessedImage = ByteString.CopyFrom(new byte[] { 1 })
            });

        using var stream1 = new MemoryStream(image1Bytes);
        using var stream2 = new MemoryStream(image2Bytes);

        m_Client.ProcessImage([stream1, stream2]);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Images.Count, Is.EqualTo(2));
        Assert.That(capturedRequest.Images[0].ToByteArray(), Is.EqualTo(image1Bytes));
        Assert.That(capturedRequest.Images[1].ToByteArray(), Is.EqualTo(image2Bytes));
    }

    [Test]
    public void ProcessImage_GrpcFailure_LogsAndThrows()
    {
        var rpcException = new RpcException(new Status(StatusCode.Internal, "Server error"));

        m_MockClient.Setup(c => c.ProcessWeaponImage(
                It.IsAny<ProcessWeaponImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(rpcException);

        using var stream1 = new MemoryStream(new byte[] { 10, 20 });
        using var stream2 = new MemoryStream(new byte[] { 30, 40 });

        var thrown = Assert.Throws<RpcException>(() => m_Client.ProcessImage([stream1, stream2]));
        Assert.That(thrown, Is.SameAs(rpcException));

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to process weapon image")),
                It.IsAny<RpcException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ProcessImage_WithMultipleImages_SendsAllImages()
    {
        var imageBytes = Enumerable.Range(0, 5)
            .Select(i => new MemoryStream(new byte[] { (byte)i }))
            .ToList();

        m_MockClient.Setup(c => c.ProcessWeaponImage(
                It.IsAny<ProcessWeaponImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ProcessWeaponImageResponse
            {
                ProcessedImage = ByteString.CopyFrom(new byte[] { 1 })
            });

        var result = m_Client.ProcessImage(imageBytes);

        m_MockClient.Verify(c => c.ProcessWeaponImage(
            It.Is<ProcessWeaponImageRequest>(r => r.Images.Count == 5),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        foreach (var stream in imageBytes)
        {
            stream.Dispose();
        }

        result.Dispose();
    }
}
