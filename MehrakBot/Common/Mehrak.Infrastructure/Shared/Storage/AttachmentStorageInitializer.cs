using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mehrak.Infrastructure.Shared.Storage;

public class AttachmentStorageInitializer : IHostedService
{
    private readonly IAmazonS3 m_S3;
    private readonly AttachmentStorageConfig m_Config;
    private readonly ILogger<AttachmentStorageInitializer> m_Logger;

    public AttachmentStorageInitializer(IAmazonS3 s3, IOptions<AttachmentStorageConfig> config, ILogger<AttachmentStorageInitializer> logger)
    {
        m_S3 = s3;
        m_Config = config.Value;
        m_Logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bucket = m_Config.Bucket;

        if (string.IsNullOrEmpty(bucket))
        {
            m_Logger.LogWarning("AttachmentStorage bucket is not configured, skipping S3 setup");
            return;
        }

        try
        {
            // 1. Ensure bucket exists (best-effort; bucket is often auto-created on first write)
            try
            {
                await m_S3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex)
            {
                // Bucket already exists (or backend auto-creates) -> proceed with versioning/lifecycle
                m_Logger.LogDebug(ex, "PutBucket for {Bucket} failed; may already exist or be auto-created", bucket);
            }

            // 2. Enable versioning (DELETE creates soft-delete tombstones; PUT creates new active versions)
            try
            {
                await m_S3.PutBucketVersioningAsync(
                    new PutBucketVersioningRequest
                    {
                        BucketName = bucket,
                        VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex)
            {
                // Ignore if versioning is already configured
                m_Logger.LogDebug(ex, "PutBucketVersioning for {Bucket} failed; versioning may already be enabled", bucket);
            }

            // 3. Apply lifecycle rules (hard purge of old data + orphan tombstones, >= 1 day granularity)
            await m_S3.PutLifecycleConfigurationAsync(
                new PutLifecycleConfigurationRequest
                {
                    BucketName = bucket,
                    Configuration = new LifecycleConfiguration
                    {
                        Rules =
                        [
                            new LifecycleRule
                            {
                                Id = "ExpireNoncurrentAttachmentVersions",
                                Status = LifecycleRuleStatus.Enabled,
                                Filter = new LifecycleFilter(),
                                NoncurrentVersionExpiration = new LifecycleRuleNoncurrentVersionExpiration { NoncurrentDays = 1 }
                            },
                            new LifecycleRule
                            {
                                Id = "ExpireAttachmentDeleteMarkers",
                                Status = LifecycleRuleStatus.Enabled,
                                Filter = new LifecycleFilter(),
                                Expiration = new LifecycleRuleExpiration { ExpiredObjectDeleteMarker = true }
                            },
                            new LifecycleRule
                            {
                                Id = "AbortIncompleteMultipartUploads",
                                Status = LifecycleRuleStatus.Enabled,
                                Filter = new LifecycleFilter(),
                                AbortIncompleteMultipartUpload = new LifecycleRuleAbortIncompleteMultipartUpload { DaysAfterInitiation = 1 }
                            }
                        ]
                    }
                },
                cancellationToken).ConfigureAwait(false);

            m_Logger.LogInformation("Attachment storage bucket {Bucket} configured successfully", bucket);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to configure attachment storage bucket {Bucket}", bucket);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
