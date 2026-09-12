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
    private readonly TimeProvider m_TimeProvider;

    public AttachmentExpirationBackgroundService(
        IAmazonS3 s3,
        IOptions<AttachmentStorageConfig> config,
        ILogger<AttachmentExpirationBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        m_S3 = s3;
        m_Config = config.Value;
        m_Logger = logger;
        m_TimeProvider = timeProvider ?? TimeProvider.System;
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

        var cutoff = m_TimeProvider.GetUtcNow().UtcDateTime - ttl;
        var request = new ListVersionsRequest { BucketName = bucket };
        int deletedVersions = 0;
        var versionsByKey = new Dictionary<string, List<S3ObjectVersion>>(StringComparer.Ordinal);
        var listingCompleted = false;

        do
        {
            var response = await m_S3.ListVersionsAsync(request, cancellationToken).ConfigureAwait(false);

            // In this SDK, both object versions and delete markers are returned in Versions,
            // distinguished by IsDeleteMarker. Keep the complete listing because deleting only
            // the current version would expose an older version of the key.
            foreach (var v in response.Versions ?? Enumerable.Empty<S3ObjectVersion>())
            {
                if (!versionsByKey.TryGetValue(v.Key, out var versions))
                {
                    versions = [];
                    versionsByKey.Add(v.Key, versions);
                }

                versions.Add(v);
            }

            if (response.IsTruncated != true)
            {
                listingCompleted = true;
                break;
            }

            request.KeyMarker = response.NextKeyMarker;
            request.VersionIdMarker = response.NextVersionIdMarker;
        } while (!cancellationToken.IsCancellationRequested);

        if (!listingCompleted || cancellationToken.IsCancellationRequested)
            return;

        foreach (var (key, versions) in versionsByKey)
        {
            var currentVersions = versions.Where(v => v.IsLatest == true).ToList();
            if (currentVersions.Count != 1)
                continue;

            var currentVersion = currentVersions[0];
            if (currentVersion.IsDeleteMarker == true || versions.Any(v => v.LastModified >= cutoff))
                continue;

            if (versions.Any(v => string.IsNullOrWhiteSpace(v.VersionId)))
            {
                m_Logger.LogWarning("Cannot expire object {Key} because a listed version has no version ID", key);
                continue;
            }

            // Delete historical versions first and the listed current version last. This avoids
            // exposing a previous version after the current one is removed. Every request is
            // pinned to a version from this listing, so a re-upload racing with the scan has a
            // new version ID and is preserved, including when it has the same ETag.
            var historicalDeletionFailed = false;
            foreach (var version in versions.Where(v => v.IsLatest != true))
            {
                try
                {
                    await m_S3.DeleteObjectAsync(
                        new DeleteObjectRequest { BucketName = bucket, Key = key, VersionId = version.VersionId },
                        cancellationToken).ConfigureAwait(false);
                    deletedVersions++;
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Another scan may have already removed this exact version.
                    m_Logger.LogDebug(ex, "Version {VersionId} of object {Key} was already deleted", version.VersionId, key);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    historicalDeletionFailed = true;
                    m_Logger.LogWarning(ex, "Failed to delete version {VersionId} of object {Key}", version.VersionId, key);
                }
            }

            if (historicalDeletionFailed)
            {
                m_Logger.LogWarning("Skipped deleting current version of object {Key} because a historical version could not be deleted", key);
                continue;
            }

            try
            {
                await m_S3.DeleteObjectAsync(
                    new DeleteObjectRequest { BucketName = bucket, Key = key, VersionId = currentVersion.VersionId },
                    cancellationToken).ConfigureAwait(false);
                deletedVersions++;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Another scan may have already removed this exact version.
                m_Logger.LogDebug(ex, "Version {VersionId} of object {Key} was already deleted", currentVersion.VersionId, key);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_Logger.LogWarning(ex, "Failed to delete version {VersionId} of object {Key}", currentVersion.VersionId, key);
            }
        }

        if (deletedVersions > 0)
            m_Logger.LogInformation("Attachment expiration: deleted {Count} expired object versions in bucket {Bucket}", deletedVersions, bucket);
    }
}
