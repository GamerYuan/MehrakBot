using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mehrak.Infrastructure.Shared.Storage;

public class AttachmentExpirationBackgroundService : BackgroundService
{
    private readonly IAmazonS3 m_S3;
    private readonly AttachmentStorageConfig m_Config;
    private readonly ILogger<AttachmentExpirationBackgroundService> m_Logger;

    public AttachmentExpirationBackgroundService(
        IAmazonS3 s3,
        IOptions<AttachmentStorageConfig> config,
        ILogger<AttachmentExpirationBackgroundService> logger)
    {
        m_S3 = s3;
        m_Config = config.Value;
        m_Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(m_Config.ExpirationScanIntervalMinutes);
        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromMinutes(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Attachment expiration scan failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task ScanAsync(CancellationToken cancellationToken)
    {
        var bucket = m_Config.Bucket;
        if (string.IsNullOrEmpty(bucket))
            return;

        var ttl = TimeSpan.FromMinutes(m_Config.TtlMinutes);
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromMinutes(60);

        var cutoff = DateTime.UtcNow - ttl;
        var request = new ListVersionsRequest { BucketName = bucket };
        int tombstoned = 0;

        do
        {
            var response = await m_S3.ListVersionsAsync(request, cancellationToken).ConfigureAwait(false);

            // In this SDK, both object versions and delete markers are returned in Versions,
            // distinguished by IsDeleteMarker. Find the IsLatest entry per key.
            var latestByKey = new Dictionary<string, (bool IsDeleteMarker, DateTime LastModified, string? VersionId)>();
            foreach (var v in response.Versions ?? Enumerable.Empty<S3ObjectVersion>())
            {
                if (v.IsLatest != true)
                    continue;

                // ponytail: pin to the listed version so a concurrent re-upload can't be tombstoned
                latestByKey[v.Key] = (v.IsDeleteMarker == true, v.LastModified ?? DateTime.MinValue, v.VersionId);
            }

            foreach (var kvp in latestByKey)
            {
                var (isDeleteMarker, lastModified, versionId) = kvp.Value;
                if (isDeleteMarker)
                    continue; // already tombstoned -> idempotent skip

                if (lastModified < cutoff)
                {
                    try
                    {
                        // Delete the specific expired version. Pinning VersionId avoids tombstoning a
                        // newer version that may have been uploaded after listing; once the current
                        // version is removed GET returns 404, and old noncurrent versions are purged
                        // by the lifecycle rule.
                        await m_S3.DeleteObjectAsync(
                            new DeleteObjectRequest { BucketName = bucket, Key = kvp.Key, VersionId = versionId },
                            cancellationToken).ConfigureAwait(false);
                        tombstoned++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        m_Logger.LogWarning(ex, "Failed to tombstone object {Key}", kvp.Key);
                    }
                }
            }

            if (response.IsTruncated != true)
                break;

            request.KeyMarker = response.NextKeyMarker;
            request.VersionIdMarker = response.NextVersionIdMarker;
        } while (!cancellationToken.IsCancellationRequested);

        if (tombstoned > 0)
            m_Logger.LogInformation("Attachment expiration: tombstoned {Count} expired objects in bucket {Bucket}", tombstoned, bucket);
    }
}
