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

    private static S3ObjectVersion Version(string key, bool isLatest, bool isDeleteMarker, DateTime lastModified, string versionSuffix = "v1")
    {
        return new S3ObjectVersion
        {
            Key = key,
            VersionId = key + (isDeleteMarker ? "-dm" : $"-{versionSuffix}"),
            ETag = key + "-etag",
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
                Version("already-tombstoned.png", isLatest: true, isDeleteMarker: true, DateTime.UtcNow.AddHours(-3)),
                Version("already-tombstoned.png", isLatest: false, isDeleteMarker: false, DateTime.UtcNow.AddHours(-4), "old")
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
                It.Is<DeleteObjectRequest>(r => r.BucketName == "test-bucket" && r.Key == "expired.png" && r.VersionId == "expired.png-v1" && r.IfMatch == null),
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
                It.Is<DeleteObjectRequest>(r => r.Key == "expired.png" && r.VersionId == "expired.png-v1" && r.IfMatch == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_PreservesSameContentReuploadThatRacesWithListing()
    {
        var oldVersion = Version("reuploaded.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2), "old");
        var newVersion = Version("reuploaded.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow, "new");
        newVersion.ETag = oldVersion.ETag;
        var remainingVersionIds = new HashSet<string> { oldVersion.VersionId, newVersion.VersionId };

        var response = new ListVersionsResponse
        {
            Versions = [oldVersion],
            IsTruncated = false
        };

        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // The re-upload happens after the scanner receives its snapshot.
                remainingVersionIds.Add(newVersion.VersionId);
                return response;
            });
        m_S3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => remainingVersionIds.Remove(request.VersionId))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        Assert.That(remainingVersionIds, Is.EquivalentTo(new[] { newVersion.VersionId }));
        m_S3.Verify(
            x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r => r.Key == "reuploaded.png" && r.VersionId == oldVersion.VersionId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_DeletesAllListedVersions_HistoricalBeforeCurrent()
    {
        var response = new ListVersionsResponse
        {
            Versions =
            [
                Version("expired.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2), "current"),
                Version("expired.png", isLatest: false, isDeleteMarker: false, DateTime.UtcNow.AddHours(-3), "old")
            ],
            IsTruncated = false
        };
        var requests = new List<DeleteObjectRequest>();

        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        m_S3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        Assert.That(requests.Select(x => x.VersionId), Is.EqualTo(new[] { "expired.png-old", "expired.png-current" }));
        Assert.That(requests, Has.All.Matches<DeleteObjectRequest>(x => x.BucketName == "test-bucket" && x.Key == "expired.png" && x.IfMatch == null));
    }

    [Test]
    public async Task ScanAsync_DoesNotDeleteCurrentWhenHistoricalDeleteFails()
    {
        var response = new ListVersionsResponse
        {
            Versions =
            [
                Version("expired.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2), "current"),
                Version("expired.png", isLatest: false, isDeleteMarker: false, DateTime.UtcNow.AddHours(-3), "old")
            ],
            IsTruncated = false
        };
        var requests = new List<DeleteObjectRequest>();

        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        m_S3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => requests.Add(request))
            .ThrowsAsync(new AmazonS3Exception("historical delete failed")
            {
                StatusCode = System.Net.HttpStatusCode.InternalServerError
            });

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        Assert.That(requests.Select(x => x.VersionId), Is.EqualTo(new[] { "expired.png-old" }));
    }

    [Test]
    public async Task ScanAsync_SkipsKeyWhenFreshVersionAppearsOnLaterPage()
    {
        var oldVersion = Version("reuploaded.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2), "old");
        var freshVersion = Version("reuploaded.png", isLatest: false, isDeleteMarker: false, DateTime.UtcNow, "new");
        var page1 = new ListVersionsResponse
        {
            Versions = [oldVersion],
            IsTruncated = true,
            NextKeyMarker = oldVersion.Key,
            NextVersionIdMarker = oldVersion.VersionId
        };
        var page2 = new ListVersionsResponse
        {
            Versions = [freshVersion],
            IsTruncated = false
        };

        var sequence = new Queue<ListVersionsResponse>([page1, page2]);
        m_S3.Setup(x => x.ListVersionsAsync(It.IsAny<ListVersionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence.Dequeue());

        var service = CreateService();
        await service.ScanAsync(CancellationToken.None);

        m_S3.Verify(
            x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ScanAsync_SkipsKeyWhenListingClaimsMultipleCurrentVersions()
    {
        var response = new ListVersionsResponse
        {
            Versions =
            [
                Version("reuploaded.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2), "old"),
                Version("reuploaded.png", isLatest: true, isDeleteMarker: false, DateTime.UtcNow.AddHours(-2), "new")
            ],
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
