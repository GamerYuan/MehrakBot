using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Mehrak.Infrastructure.Tests.Shared.Storage;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class AttachmentExpirationBackgroundServiceTests
{
    private readonly Mock<IAmazonS3> m_S3 = new();
    private readonly AttachmentStorageConfig m_Config = new()
    {
        Bucket = "test-bucket",
        TtlMinutes = 60,
        ExpirationScanIntervalMinutes = 15
    };

    private AttachmentExpirationBackgroundService CreateService()
    {
        var options = Options.Create(m_Config);
        return new AttachmentExpirationBackgroundService(m_S3.Object, options, NullLogger<AttachmentExpirationBackgroundService>.Instance);
    }

    private static S3ObjectVersion Version(string key, bool isLatest, bool isDeleteMarker, DateTime lastModified)
    {
        return new S3ObjectVersion
        {
            Key = key,
            VersionId = key + (isDeleteMarker ? "-dm" : "-v1"),
            IsLatest = isLatest,
            IsDeleteMarker = isDeleteMarker,
            LastModified = lastModified
        };
    }

    [Test]
    public async Task ScanAsync_TombsExpiredObjects_AndSkipsActiveAndTombstoned()
    {
        var response = new ListVersionsResponse
        {
            Versions =
            [
                Version("expired.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2)),
                Version("active.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddMinutes(-5)),
                Version("already-tombstoned.png", isLatest: true, isDeleteMarker: true, DateTime.UtcNow.AddHours(-3))
            ],
            IsTruncated = false
        };

        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        m_S3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        m_S3.Verify(
            x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r => r.BucketName == "test-bucket" && r.Key == "expired.png"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        m_S3.Verify(
            x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r => r.Key == "active.png" || r.Key == "already-tombstoned.png"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ScanAsync_Paginates_WhenTruncated()
    {
        var page1 = new ListVersionsResponse
        {
            Versions = [Version("expired.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2))],
            IsTruncated = true,
            NextKeyMarker = "expired.png",
            NextVersionIdMarker = "v2"
        };
        var page2 = new ListVersionsResponse
        {
            Versions = [Version("active.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddMinutes(-5))],
            IsTruncated = false
        };

        var sequence = new Queue<ListVersionsResponse>([page1, page2]);
        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence.Dequeue());
        m_S3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        m_S3.Verify(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        m_S3.Verify(
            x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r => r.Key == "expired.png"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_HandlesNullVersionsCollection_WithoutThrowing()
    {
        var response = new ListVersionsResponse
        {
            Versions = null,
            IsTruncated = false
        };

        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        m_S3.Verify(
            x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
