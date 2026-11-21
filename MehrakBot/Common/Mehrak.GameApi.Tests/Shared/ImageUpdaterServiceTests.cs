using System.Net;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Image.Models;
using Mehrak.GameApi.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.GameApi.Tests.Shared;

[TestFixture]
public class ImageUpdaterServiceTests
{
    private const string FirstUrl = "https://images.example/first.png";
    private const string SecondUrl = "https://images.example/second.png";
    private const string ImageName = "test-image";

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task UpdateImageAsync_ReturnsUploadResult(bool shouldProcess, bool uploadResult)
    {
        using var content = new TrackingContent();
        using var handler = new StubHandler((_, _) => Task.FromResult(Response(content)));
        using var client = new HttpClient(handler);
        using var processed = new MemoryStream([4, 5, 6]);
        var repository = Repository(uploadResult);
        var processor = new Mock<IImageProcessor>(MockBehavior.Strict);
        processor.SetupGet(x => x.ShouldProcess).Returns(shouldProcess);
        processor.Setup(x => x.ProcessImage(It.IsAny<Stream>())).Returns(processed);
        var service = CreateService(repository, client);

        var result = await service.UpdateImageAsync(Data(), processor.Object);

        Assert.That(result, Is.EqualTo(uploadResult));
        repository.Verify(x => x.UploadFileAsync(ImageName,
            It.Is<Stream>(s => !shouldProcess || s == processed),
            FileNameFormat.PngContentType, It.IsAny<CancellationToken>()), Times.Once);
        processor.Verify(x => x.ProcessImage(It.IsAny<Stream>()), shouldProcess ? Times.Once() : Times.Never());
        Assert.That(content.IsDisposed, Is.True);
        Assert.That(content.Body.CanRead, Is.False);
        Assert.That(processed.CanRead, Is.EqualTo(!shouldProcess));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateMultiImageAsync_ReturnsUploadResult(bool uploadResult)
    {
        using var first = new TrackingContent();
        using var second = new TrackingContent();
        using var handler = new StubHandler((request, _) =>
            Task.FromResult(Response(request.RequestUri!.AbsoluteUri == FirstUrl ? first : second)));
        using var client = new HttpClient(handler);
        using var processed = new MemoryStream([4, 5, 6]);
        var repository = Repository(uploadResult);
        var processor = new Mock<IMultiImageProcessor>(MockBehavior.Strict);
        processor.Setup(x => x.ProcessImage(It.IsAny<IEnumerable<Stream>>()))
            .Callback<IEnumerable<Stream>>(streams => Assert.That(streams, Is.EqualTo(new[] { first.Body, second.Body })))
            .Returns(processed);

        var result = await CreateService(repository, client).UpdateMultiImageAsync(Data(), processor.Object);

        Assert.That(result, Is.EqualTo(uploadResult));
        repository.Verify(x => x.UploadFileAsync(ImageName, processed,
            FileNameFormat.PngContentType, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(handler.Requests, Is.EqualTo(new[] { FirstUrl, SecondUrl }));
        Assert.That(first.IsDisposed && second.IsDisposed, Is.True);
        Assert.That(first.Body.CanRead || second.Body.CanRead || processed.CanRead, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExistingImage_ReturnsTrueWithoutDownloadingProcessingOrUploading(bool multi)
    {
        var repository = new Mock<IImageRepository>(MockBehavior.Strict);
        repository.Setup(x => x.FileExistsAsync(ImageName, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var handler = new StubHandler((_, _) => throw new AssertionException("Unexpected download"));
        using var client = new HttpClient(handler);
        var service = CreateService(repository, client);

        var result = multi
            ? await service.UpdateMultiImageAsync(Data(), new Mock<IMultiImageProcessor>(MockBehavior.Strict).Object)
            : await service.UpdateImageAsync(Data(), new Mock<IImageProcessor>(MockBehavior.Strict).Object);

        Assert.That(result, Is.True);
        Assert.That(handler.Requests, Is.Empty);
        repository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("transport")]
    [TestCase("caller-cancel")]
    [TestCase("local-timeout")]
    public async Task UpdateMultiImageAsync_LaterDownloadFailure_DisposesEarlierResponse(string failure)
    {
        using var cancellation = new CancellationTokenSource();
        using var first = new TrackingContent();
        var transportError = new HttpRequestException("download failed");
        using var handler = new StubHandler((request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == FirstUrl)
                return Task.FromResult(Response(first));

            Assert.That(first.IsDisposed, Is.False);
            if (failure == "transport")
                throw transportError;
            if (failure == "caller-cancel")
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new OperationCanceledException("local timeout");
        });
        using var client = new HttpClient(handler);
        var repository = Repository(false);
        var processor = new Mock<IMultiImageProcessor>(MockBehavior.Strict);
        var service = CreateService(repository, client);

        if (failure == "transport")
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await service.UpdateMultiImageAsync(Data(), processor.Object, cancellation.Token));
            Assert.That(error, Is.SameAs(transportError));
        }
        else if (failure == "caller-cancel")
        {
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.UpdateMultiImageAsync(Data(), processor.Object, cancellation.Token));
        }
        else
        {
            Assert.That(await service.UpdateMultiImageAsync(Data(), processor.Object, cancellation.Token), Is.False);
        }

        Assert.That(handler.Requests, Is.EqualTo(new[] { FirstUrl, SecondUrl }));
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(first.Body.CanRead, Is.False);
        processor.Verify(x => x.ProcessImage(It.IsAny<IEnumerable<Stream>>()), Times.Never);
        repository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UpdateMultiImageAsync_StatusFailure_DisposesAllResponses(bool failFirst)
    {
        using var first = new TrackingContent();
        using var second = new TrackingContent();
        using var handler = new StubHandler((request, _) =>
        {
            var isFirst = request.RequestUri!.AbsoluteUri == FirstUrl;
            return Task.FromResult(Response(isFirst ? first : second,
                isFirst == failFirst ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler);
        var repository = Repository(false);
        var processor = new Mock<IMultiImageProcessor>(MockBehavior.Strict);

        var result = await CreateService(repository, client).UpdateMultiImageAsync(Data(), processor.Object);

        Assert.That(result, Is.False);
        Assert.That(handler.Requests, Is.EqualTo(new[] { FirstUrl, SecondUrl }));
        Assert.That(first.IsDisposed && second.IsDisposed, Is.True);
        Assert.That(first.Body.CanRead || second.Body.CanRead, Is.False);
        processor.Verify(x => x.ProcessImage(It.IsAny<IEnumerable<Stream>>()), Times.Never);
        repository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IImageRepository> Repository(bool uploadResult)
    {
        var repository = new Mock<IImageRepository>(MockBehavior.Strict);
        repository.Setup(x => x.FileExistsAsync(ImageName, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(x => x.UploadFileAsync(ImageName, It.IsAny<Stream>(), FileNameFormat.PngContentType,
                It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string?, CancellationToken>((_, stream, _, _) => Assert.That(stream.Position, Is.Zero))
            .ReturnsAsync(uploadResult);
        return repository;
    }

    private static ImageUpdaterService CreateService(Mock<IImageRepository> repository, HttpClient client)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        return new ImageUpdaterService(repository.Object, factory.Object, NullLogger<ImageUpdaterService>.Instance);
    }

    private static IMultiImageData Data()
    {
        var data = new Mock<IMultiImageData>();
        data.SetupGet(x => x.Name).Returns(ImageName);
        data.SetupGet(x => x.Url).Returns(FirstUrl);
        data.SetupGet(x => x.AdditionalUrls).Returns([SecondUrl]);
        return data.Object;
    }

    private static HttpResponseMessage Response(HttpContent content, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = content };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return send(request, cancellationToken);
        }
    }

    private sealed class TrackingContent : StreamContent
    {
        public TrackingContent() : this(new MemoryStream([1, 2, 3]))
        {
        }

        private TrackingContent(Stream body) : base(body)
        {
            Body = body;
        }

        public Stream Body { get; }
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
