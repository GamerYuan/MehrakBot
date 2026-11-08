using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Shared.Telemetry;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Mehrak.Infrastructure.Tests.Shared.Storage;

[Parallelizable(ParallelScope.All)]
public sealed class AttachmentStorageServiceTests
{
    private static readonly string ValidFileName = $"{new string('a', 64)}.jpg";

    [Test]
    public async Task ExistsAsync_NotFound_MarksExistenceProbeAndReturnsFalse()
    {
        var s3 = new Mock<IAmazonS3>();
        ExpectedDependencyKind observedKind = ExpectedDependencyKind.None;
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectMetadataRequest, CancellationToken>((_, _) =>
                observedKind = ExpectedDependency.Current)
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        var service = CreateService(s3.Object);

        var exists = await service.ExistsAsync(ValidFileName);

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False);
            Assert.That(observedKind, Is.EqualTo(ExpectedDependencyKind.ExistenceProbe));
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
        });
    }

    [Test]
    public void ExistsAsync_ServerError_IsNotSwallowed()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("denied") { StatusCode = HttpStatusCode.InternalServerError });
        var service = CreateService(s3.Object);

        Assert.ThrowsAsync<AmazonS3Exception>(async () => await service.ExistsAsync(ValidFileName));
    }

    [Test]
    public async Task ExistsAsync_MissingBucket_LogsErrorAndReturnsFalse()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("bucket missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchBucket"
            });
        var logger = new CapturingLogger<AttachmentStorageService>();
        var service = CreateService(s3.Object, logger);

        var exists = await service.ExistsAsync(ValidFileName);

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False);
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
        });
    }

    private static AttachmentStorageService CreateService(IAmazonS3 s3,
        ILogger<AttachmentStorageService>? logger = null)
    {
        return new AttachmentStorageService(s3,
            Options.Create(new AttachmentStorageConfig { Bucket = "test-bucket" }),
            logger ?? new CapturingLogger<AttachmentStorageService>());
    }

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
