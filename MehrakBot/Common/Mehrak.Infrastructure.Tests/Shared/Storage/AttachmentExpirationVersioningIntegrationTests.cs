using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Testcontainers.LocalStack;

namespace Mehrak.Infrastructure.Tests.Shared.Storage;

[TestFixture]
[NonParallelizable]
internal sealed class AttachmentExpirationVersioningIntegrationTests
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";

    private LocalStackContainer m_LocalStack = null!;
    private AmazonS3Client m_S3 = null!;
    private string m_Bucket = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        m_LocalStack = new LocalStackBuilder("localstack/localstack:4.14.0").Build();
        await m_LocalStack.StartAsync();

        m_S3 = new AmazonS3Client(
            "test-access-key",
            "test-secret-key",
            new AmazonS3Config
            {
                ServiceURL = m_LocalStack.GetConnectionString(),
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        m_S3.Dispose();
        await m_LocalStack.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        m_Bucket = $"attachment-expiry-{Guid.NewGuid():N}";
        await m_S3.PutBucketAsync(new PutBucketRequest { BucketName = m_Bucket });
        await m_S3.PutBucketVersioningAsync(
            new PutBucketVersioningRequest
            {
                BucketName = m_Bucket,
                VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
            });
    }

    [Test]
    public async Task ScanAsync_DeletesAllVersions_AndMakesExpiredKeyUnavailable()
    {
        await PutAsync("first payload");
        await PutAsync("second payload");

        var service = CreateService(DateTimeOffset.UtcNow.AddHours(2));
        await service.ScanAsync(CancellationToken.None);

        var storage = CreateStorage();
        Assert.That(await storage.ExistsAsync(Key), Is.False);
        Assert.That((await ListVersionsAsync()).Versions ?? [], Is.Empty);

        // A second scan is idempotent and does not recreate a marker or expose old data.
        await service.ScanAsync(CancellationToken.None);
        Assert.That(await storage.ExistsAsync(Key), Is.False);
        Assert.That((await ListVersionsAsync()).Versions ?? [], Is.Empty);
    }

    [Test]
    public async Task VersionPinnedDelete_PreservesSameContentReupload()
    {
        await PutAsync("same payload");
        var oldVersion = (await ListVersionsAsync()).Versions!.Single(x => x.IsLatest == true);

        await PutAsync("same payload");
        await m_S3.DeleteObjectAsync(
            new DeleteObjectRequest
            {
                BucketName = m_Bucket,
                Key = Key,
                VersionId = oldVersion.VersionId
            });

        var storage = CreateStorage();
        Assert.That(await storage.ExistsAsync(Key), Is.True);
        Assert.That(await ReadAsync(), Is.EqualTo("same payload"));
    }

    [Test]
    public async Task ScanAsync_SkipsCurrentDeleteMarker_AndDoesNotDeleteHistory()
    {
        await PutAsync("retained history");
        await m_S3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = m_Bucket, Key = Key });

        var before = await ListVersionsAsync();
        Assert.That(before.Versions, Has.Some.Matches<S3ObjectVersion>(x => x.IsLatest == true && x.IsDeleteMarker == true));
        var service = CreateService(DateTimeOffset.UtcNow.AddHours(2));
        await service.ScanAsync(CancellationToken.None);
        var after = await ListVersionsAsync();

        Assert.That(await CreateStorage().ExistsAsync(Key), Is.False);
        Assert.That(after.Versions ?? [], Has.Count.EqualTo(before.Versions?.Count ?? 0));
    }

    private AttachmentExpirationBackgroundService CreateService(DateTimeOffset now)
    {
        return new AttachmentExpirationBackgroundService(
            m_S3,
            Options.Create(new AttachmentStorageConfig
            {
                Bucket = m_Bucket,
                TtlMinutes = 60,
                ExpirationScanIntervalMinutes = 15
            }),
            NullLogger<AttachmentExpirationBackgroundService>.Instance,
            new FixedTimeProvider(now));
    }

    private AttachmentStorageService CreateStorage()
    {
        return new AttachmentStorageService(
            m_S3,
            Options.Create(new AttachmentStorageConfig { Bucket = m_Bucket }),
            NullLogger<AttachmentStorageService>.Instance);
    }

    private async Task PutAsync(string payload)
    {
        await m_S3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = m_Bucket,
                Key = Key,
                InputStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload)),
                AutoCloseStream = true,
                ContentType = "image/png"
            });
    }

    private Task<ListVersionsResponse> ListVersionsAsync()
    {
        return m_S3.ListVersionsAsync(new ListVersionsRequest { BucketName = m_Bucket });
    }

    private async Task<string> ReadAsync()
    {
        using var response = await m_S3.GetObjectAsync(new GetObjectRequest { BucketName = m_Bucket, Key = Key });
        using var reader = new StreamReader(response.ResponseStream);
        return await reader.ReadToEndAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
