using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Telemetry;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Mehrak.Infrastructure.Tests.Shared.Storage;

[Parallelizable(ParallelScope.All)]
public class ImageRepositoryTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ListFilesAsync_EmptyListing_ReturnsEmptyList(bool nullObjects)
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = nullObjects ? null : [],
                IsTruncated = null
            });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = CreateRepository(s3.Object, cache);
        using var cts = new CancellationTokenSource();

        var keys = await repository.ListFilesAsync("zzz/portrait_1191_", cts.Token);

        Assert.That(keys, Is.Empty);
        s3.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.BucketName == "test-bucket" && r.Prefix == "zzz/portrait_1191_" &&
            r.ContinuationToken == null), cts.Token), Times.Once);
    }

    [Test]
    public async Task ListFilesAsync_NullIntermediatePage_PreservesKeysAndContinuesPagination()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "zzz/portrait_1371_1.png" }],
                IsTruncated = true,
                NextContinuationToken = "page-2"
            })
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = null,
                IsTruncated = true,
                NextContinuationToken = "page-3"
            })
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "zzz/portrait_1371_2.png" }],
                IsTruncated = false
            });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = CreateRepository(s3.Object, cache);

        var keys = await repository.ListFilesAsync("zzz/portrait_1371_");

        Assert.That(keys, Is.EqualTo(new[] { "zzz/portrait_1371_1.png", "zzz/portrait_1371_2.png" }));
        foreach (var key in keys)
            Assert.That(await repository.FileExistsAsync(key), Is.True);
        s3.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.ContinuationToken == "page-2"), It.IsAny<CancellationToken>()), Times.Once);
        s3.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.ContinuationToken == "page-3"), It.IsAny<CancellationToken>()), Times.Once);
        s3.Verify(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FileExistsAsync_NotFound_MarksExistenceProbeAndReturnsFalse()
    {
        var s3 = new Mock<IAmazonS3>();
        ExpectedDependencyKind observedKind = ExpectedDependencyKind.None;
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectMetadataRequest, CancellationToken>((_, _) =>
                observedKind = ExpectedDependency.Current)
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = CreateRepository(s3.Object, cache);

        var exists = await repository.FileExistsAsync("genshin/test.png");

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False);
            Assert.That(observedKind, Is.EqualTo(ExpectedDependencyKind.ExistenceProbe));
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
        });
    }

    [Test]
    public async Task FileExistsAsync_NotFound_DoesNotLogAnError()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new CapturingLogger<ImageRepository>();
        var repository = CreateRepository(s3.Object, cache, logger);

        var exists = await repository.FileExistsAsync("genshin/test.png");

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False);
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Error));
        });
    }

    [Test]
    public async Task FileExistsAsync_MissingBucket_LogsErrorAndReturnsFalse()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("bucket missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchBucket"
            });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new CapturingLogger<ImageRepository>();
        var repository = CreateRepository(s3.Object, cache, logger);

        var exists = await repository.FileExistsAsync("genshin/test.png");

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False);
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
        });
    }

    [Test]
    public async Task FileExistsAsync_ServerError_ReturnsFalseAndLogsError()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("denied") { StatusCode = HttpStatusCode.InternalServerError });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new CapturingLogger<ImageRepository>();
        var repository = CreateRepository(s3.Object, cache, logger);

        var exists = await repository.FileExistsAsync("genshin/test.png");

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False);
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
        });
    }

    [Test]
    public void DownloadFileToStreamAsync_NotFound_IsNotMarkedAsExistenceProbe()
    {
        var s3 = new Mock<IAmazonS3>();
        ExpectedDependencyKind observedKind = ExpectedDependencyKind.ExistenceProbe;
        s3.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectRequest, CancellationToken>((_, _) => observedKind = ExpectedDependency.Current)
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = CreateRepository(s3.Object, cache);

        Assert.ThrowsAsync<ImageNotFoundException>(async () =>
            await repository.DownloadFileToStreamAsync("genshin/test.png"));
        Assert.Multiple(() =>
        {
            Assert.That(observedKind, Is.EqualTo(ExpectedDependencyKind.None));
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
        });
    }

    private static ImageRepository CreateRepository(IAmazonS3 s3, IMemoryCache cache) =>
        new(s3, Options.Create(new S3StorageConfig { Bucket = "test-bucket" }),
            NullLogger<ImageRepository>.Instance, cache);

    private static ImageRepository CreateRepository(IAmazonS3 s3, IMemoryCache cache,
        ILogger<ImageRepository> logger) =>
        new(s3, Options.Create(new S3StorageConfig { Bucket = "test-bucket" }), logger, cache);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }
}
