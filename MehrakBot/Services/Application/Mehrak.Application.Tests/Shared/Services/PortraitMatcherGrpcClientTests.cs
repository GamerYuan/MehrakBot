using Google.Protobuf;
using Grpc.Core;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Protobuf;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Shared.Services;

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class PortraitMatcherGrpcClientTests
{
    private Mock<ImageProcessorService.ImageProcessorServiceClient> m_MockClient;
    private PortraitMatcherGrpcClient m_Client;

    [SetUp]
    public void Setup()
    {
        m_MockClient = new Mock<ImageProcessorService.ImageProcessorServiceClient>();
        m_Client = new PortraitMatcherGrpcClient(
            m_MockClient.Object,
            Mock.Of<ILogger<PortraitMatcherGrpcClient>>());
    }

    [Test]
    public async Task MatchAsync_SerializesImagesAndMapsResponse()
    {
        var referenceImage = new byte[] { 1, 2, 3 };
        var candidateImage = new byte[] { 4, 5, 6 };
        MatchImageRequest? capturedRequest = null;
        DateTime? capturedDeadline = null;

        m_MockClient.Setup(x => x.MatchImageAsync(
                It.IsAny<MatchImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<MatchImageRequest, Metadata, DateTime?, CancellationToken>(
                (request, _, deadline, _) =>
                {
                    capturedRequest = request;
                    capturedDeadline = deadline;
                })
            .Returns(CreateCall(new MatchImageResponse { IsMatch = true, Confidence = 0.9f }));

        var result = await m_Client.MatchAsync(referenceImage, candidateImage);

        Assert.That(result.IsMatch, Is.True);
        Assert.That(result.Confidence, Is.EqualTo(0.9f));
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.ReferenceImage.ToByteArray(), Is.EqualTo(referenceImage));
        Assert.That(capturedRequest.CandidateImage.ToByteArray(), Is.EqualTo(candidateImage));
        Assert.That(capturedDeadline, Is.Not.Null);
        Assert.That(capturedDeadline, Is.GreaterThan(DateTime.UtcNow));
    }

    [Test]
    public void MatchAsync_WhenRpcIsCancelled_PropagatesCancellation()
    {
        m_MockClient.Setup(x => x.MatchImageAsync(
                It.IsAny<MatchImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Cancelled, "cancelled")));

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await m_Client.MatchAsync([1], [2], new CancellationTokenSource().Token));
    }

    [Test]
    public void MatchAsync_WhenRpcFails_PropagatesRpcFailure()
    {
        m_MockClient.Setup(x => x.MatchImageAsync(
                It.IsAny<MatchImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "ImageProcessor unavailable")));

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await m_Client.MatchAsync([1], [2]));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
    }

    private static AsyncUnaryCall<MatchImageResponse> CreateCall(MatchImageResponse response)
    {
        return new AsyncUnaryCall<MatchImageResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }
}

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class ImageFetcherTests
{
    [Test]
    public async Task FetchBytesAsync_WhenHttpRequestFails_ReturnsNull()
    {
        using var client = new HttpClient(new ThrowingHandler());
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);

        var fetcher = new ImageFetcher(factory.Object, Mock.Of<ILogger<ImageFetcher>>());

        var result = await fetcher.FetchBytesAsync("https://example.com/portrait.png");

        Assert.That(result, Is.Null);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection failed");
        }
    }
}
