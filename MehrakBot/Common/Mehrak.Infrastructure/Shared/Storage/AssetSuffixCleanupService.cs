#region

using System.Globalization;
using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Storage;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace Mehrak.Infrastructure.Shared.Storage;

/// <summary>
/// Removes asset objects whose full key ends with a literal suffix. Assets are copied into a
/// dedicated backup bucket before the source object is conditionally deleted. Dry runs never
/// change storage, including bucket or lifecycle configuration.
/// </summary>
public sealed class AssetSuffixCleanupService : IAssetSuffixCleanupService
{
    internal const int RetentionDays = 7;
    internal const string ExpireRuleId = "mehrak-asset-cleanup-expire-7d";
    internal const string DeleteMarkerRuleId = "mehrak-asset-cleanup-delete-markers";
    internal const string DefaultBackupPrefix = "asset-cleanup";
    internal const long MaxSingleCopyBytes = 5L * 1024 * 1024 * 1024;
    internal const int MaxS3KeyBytes = 1024;

    private readonly IAmazonS3 m_S3;
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<AssetSuffixCleanupService> m_Logger;
    private readonly TimeProvider m_TimeProvider;
    private readonly string m_SourceBucket;
    private readonly string m_BackupBucket;
    private readonly string m_BackupPrefix;
    private readonly string m_AttachmentBucket;
    private readonly string m_PortraitBucket;

    public AssetSuffixCleanupService(
        IAmazonS3 s3,
        IImageRepository imageRepository,
        IOptions<S3StorageConfig> storageConfig,
        IOptions<AssetCleanupConfig> cleanupConfig,
        IOptions<AttachmentStorageConfig> attachmentConfig,
        IOptions<UserPortraitStorageConfig> portraitConfig,
        ILogger<AssetSuffixCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        m_S3 = s3;
        m_ImageRepository = imageRepository;
        m_Logger = logger;
        m_TimeProvider = timeProvider ?? TimeProvider.System;
        m_SourceBucket = storageConfig.Value.Bucket?.Trim() ?? "";
        m_BackupBucket = cleanupConfig.Value.BackupBucket?.Trim() ?? "";
        m_BackupPrefix = NormalizeBackupPrefix(cleanupConfig.Value.BackupPrefix);
        m_AttachmentBucket = attachmentConfig.Value.Bucket?.Trim() ?? "";
        m_PortraitBucket = portraitConfig.Value.Bucket?.Trim() ?? "";
    }

    public async Task<AssetSuffixCleanupResult> CleanupBySuffixAsync(
        AssetSuffixCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        var suffix = ValidateSuffix(request.Suffix);

        ValidateSourceConfiguration();

        if (!request.DryRun)
            ValidateExecuteConfiguration();

        var matches = await ListMatchingKeysAsync(suffix, cancellationToken).ConfigureAwait(false);

        if (request.DryRun)
        {
            var dryRunKeys = matches
                .Select(match => new AssetSuffixCleanupKeyResult(match.Key, AssetSuffixCleanupStatuses.Matched))
                .ToList();

            return new AssetSuffixCleanupResult(
                DryRun: true,
                Suffix: suffix,
                MatchedCount: matches.Count,
                DeletedCount: 0,
                FailedCount: 0,
                BackupLocation: null,
                Keys: dryRunKeys);
        }

        if (matches.Count == 0)
        {
            return new AssetSuffixCleanupResult(
                DryRun: false,
                Suffix: suffix,
                MatchedCount: 0,
                DeletedCount: 0,
                FailedCount: 0,
                BackupLocation: null,
                Keys: []);
        }

        var runPrefix = CreateRunPrefix();
        var backupLocation = $"s3://{m_BackupBucket}/{runPrefix.TrimEnd('/')}";

        // Establish retention before copying or deleting anything. Both failures are fatal for
        // the whole run so an unverified backup never becomes the only copy of a deleted asset.
        await EnsureBackupBucketAsync(cancellationToken).ConfigureAwait(false);
        await ApplyBackupLifecycleAsync(cancellationToken).ConfigureAwait(false);

        var keyResults = new List<AssetSuffixCleanupKeyResult>(matches.Count);
        var deletedCount = 0;
        var failedCount = 0;

        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var backupKey = runPrefix + match.Key;

            if (Encoding.UTF8.GetByteCount(backupKey) > MaxS3KeyBytes)
            {
                failedCount++;
                keyResults.Add(Failure(match.Key, AssetCleanupErrorCodes.BackupKeyTooLong,
                    "The backup key would exceed the storage key length limit; the source object was not changed."));
                continue;
            }

            if (match.Size is > MaxSingleCopyBytes)
            {
                failedCount++;
                keyResults.Add(Failure(match.Key, AssetCleanupErrorCodes.SourceTooLarge,
                    "The source object exceeds the single-copy limit; the source object was not changed."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(match.ETag))
            {
                failedCount++;
                keyResults.Add(Failure(match.Key, AssetCleanupErrorCodes.SourceMetadataIncomplete,
                    "The source object did not report an ETag, so the backup and deletion could not be made conditional; the source object was not changed."));
                continue;
            }

            var backupCreated = false;
            try
            {
                await CopyToBackupAsync(match, backupKey, cancellationToken).ConfigureAwait(false);
                backupCreated = true;
                await VerifyBackupAsync(match, backupKey, cancellationToken).ConfigureAwait(false);
                await DeleteSourceAsync(match, cancellationToken).ConfigureAwait(false);
                InvalidateExistsCache(match.Key);

                deletedCount++;
                keyResults.Add(new AssetSuffixCleanupKeyResult(
                    match.Key,
                    AssetSuffixCleanupStatuses.Deleted,
                    BackupKey: backupKey));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AssetCleanupKeyException ex)
            {
                m_Logger.LogWarning(ex, "Asset cleanup failed for {Key} with {ErrorCode}", match.Key, ex.Code);
                failedCount++;
                keyResults.Add(new AssetSuffixCleanupKeyResult(
                    match.Key,
                    AssetSuffixCleanupStatuses.Failed,
                    BackupKey: backupCreated ? backupKey : null,
                    ErrorCode: ex.Code,
                    Error: ex.Message));
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Asset cleanup failed unexpectedly for {Key}", match.Key);
                failedCount++;
                keyResults.Add(new AssetSuffixCleanupKeyResult(
                    match.Key,
                    AssetSuffixCleanupStatuses.Failed,
                    BackupKey: backupCreated ? backupKey : null,
                    ErrorCode: AssetCleanupErrorCodes.UnexpectedError,
                    Error: "The object could not be processed; the source object was not deleted."));
            }
        }

        m_Logger.LogInformation(
            "Asset cleanup run completed for suffix {Suffix}: {Matched} matched, {Deleted} deleted, {Failed} failed, backup location {BackupLocation}",
            suffix, matches.Count, deletedCount, failedCount, backupLocation);

        return new AssetSuffixCleanupResult(
            DryRun: false,
            Suffix: suffix,
            MatchedCount: matches.Count,
            DeletedCount: deletedCount,
            FailedCount: failedCount,
            BackupLocation: backupLocation,
            Keys: keyResults);
    }

    private async Task<List<AssetCandidate>> ListMatchingKeysAsync(string suffix, CancellationToken cancellationToken)
    {
        var matches = new List<AssetCandidate>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ListObjectsV2Response response;
            try
            {
                response = await m_S3.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = m_SourceBucket,
                        ContinuationToken = continuationToken,
                        MaxKeys = 1000
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Asset bucket listing failed for suffix {Suffix}", suffix);
                throw new AssetCleanupException(
                    AssetCleanupErrorCodes.ListingFailed,
                    "The asset bucket listing failed; no objects were changed.",
                    ex);
            }

            if (response is null)
            {
                throw new AssetCleanupException(
                    AssetCleanupErrorCodes.ListingFailed,
                    "The asset bucket listing returned no response; no objects were changed.");
            }

            if (response.S3Objects is { } objects)
            {
                foreach (var item in objects)
                {
                    if (string.IsNullOrEmpty(item.Key) ||
                        !item.Key.EndsWith(suffix, StringComparison.Ordinal) ||
                        !seenKeys.Add(item.Key))
                    {
                        continue;
                    }

                    matches.Add(new AssetCandidate(item.Key, item.ETag, item.Size));
                }
            }

            if (response.IsTruncated != true)
                break;

            var nextToken = response.NextContinuationToken;
            if (string.IsNullOrWhiteSpace(nextToken))
            {
                throw new AssetCleanupException(
                    AssetCleanupErrorCodes.ListingTokenMissing,
                    "The asset bucket listing was truncated without a continuation token; no objects were changed.");
            }

            if (!seenTokens.Add(nextToken))
            {
                throw new AssetCleanupException(
                    AssetCleanupErrorCodes.ListingTokenRepeated,
                    "The asset bucket listing repeated a continuation token; no objects were changed.");
            }

            continuationToken = nextToken;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return matches;
    }

    private async Task EnsureBackupBucketAsync(CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            await m_S3.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = m_BackupBucket,
                    MaxKeys = 1
                },
                cancellationToken).ConfigureAwait(false);
            exists = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Backup bucket {Bucket} could not be checked", m_BackupBucket);
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.BackupBucketUnavailable,
                "The backup bucket could not be checked; no objects were changed.",
                ex);
        }

        if (exists)
            return;

        try
        {
            await m_S3.PutBucketAsync(
                new PutBucketRequest { BucketName = m_BackupBucket },
                cancellationToken).ConfigureAwait(false);
            m_Logger.LogInformation("Created asset backup bucket {Bucket}", m_BackupBucket);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception ex) when (
            ex.StatusCode == HttpStatusCode.Conflict ||
            ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            m_Logger.LogDebug(ex, "Backup bucket {Bucket} already exists", m_BackupBucket);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Backup bucket {Bucket} could not be created", m_BackupBucket);
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.BackupBucketUnavailable,
                "The backup bucket could not be created; no objects were changed.",
                ex);
        }
    }

    private async Task ApplyBackupLifecycleAsync(CancellationToken cancellationToken)
    {
        var existingRules = new List<LifecycleRule>();

        try
        {
            var response = await m_S3.GetLifecycleConfigurationAsync(
                new GetLifecycleConfigurationRequest { BucketName = m_BackupBucket },
                cancellationToken).ConfigureAwait(false);

            if (response?.Configuration?.Rules is { } configurationRules)
                existingRules.AddRange(configurationRules);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode == "NoSuchLifecycleConfiguration" ||
            ex.StatusCode == HttpStatusCode.NotFound)
        {
            // No lifecycle configuration exists yet.
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Backup bucket {Bucket} lifecycle could not be read", m_BackupBucket);
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.LifecycleUnavailable,
                "The backup bucket lifecycle could not be read; no objects were changed.",
                ex);
        }

        var ownedPrefix = $"{m_BackupPrefix}/";
        ValidateExistingLifecycleRules(existingRules, ownedPrefix);

        var expirePrefixFilter = new LifecycleFilter
        {
            LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = ownedPrefix }
        };
        var deleteMarkerPrefixFilter = new LifecycleFilter
        {
            LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = ownedPrefix }
        };

        var rulesToWrite = existingRules
            .Where(rule => rule.Id is not ExpireRuleId and not DeleteMarkerRuleId)
            .ToList();

        rulesToWrite.Add(new LifecycleRule
        {
            Id = ExpireRuleId,
            Status = LifecycleRuleStatus.Enabled,
            Filter = expirePrefixFilter,
            Expiration = new LifecycleRuleExpiration { Days = RetentionDays },
            NoncurrentVersionExpiration = new LifecycleRuleNoncurrentVersionExpiration { NoncurrentDays = RetentionDays }
        });

        rulesToWrite.Add(new LifecycleRule
        {
            Id = DeleteMarkerRuleId,
            Status = LifecycleRuleStatus.Enabled,
            Filter = deleteMarkerPrefixFilter,
            Expiration = new LifecycleRuleExpiration { ExpiredObjectDeleteMarker = true }
        });

        try
        {
            await m_S3.PutLifecycleConfigurationAsync(
                new PutLifecycleConfigurationRequest
                {
                    BucketName = m_BackupBucket,
                    Configuration = new LifecycleConfiguration { Rules = rulesToWrite }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Backup bucket {Bucket} lifecycle could not be applied", m_BackupBucket);
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.LifecycleRejected,
                "The seven-day backup lifecycle could not be applied; no objects were changed.",
                ex);
        }
    }

    private async Task CopyToBackupAsync(AssetCandidate candidate, string backupKey, CancellationToken cancellationToken)
    {
        try
        {
            await m_S3.CopyObjectAsync(
                new CopyObjectRequest
                {
                    SourceBucket = m_SourceBucket,
                    SourceKey = candidate.Key,
                    DestinationBucket = m_BackupBucket,
                    DestinationKey = backupKey,
                    ETagToMatch = candidate.ETag,
                    MetadataDirective = S3MetadataDirective.COPY
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception ex) when (
            ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.SourceModified,
                "The source object changed after it was listed; it was not copied or deleted.",
                ex);
        }
        catch (Exception ex)
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.CopyFailed,
                "The backup copy failed; the source object was not deleted.",
                ex);
        }
    }

    private async Task VerifyBackupAsync(AssetCandidate candidate, string backupKey, CancellationToken cancellationToken)
    {
        GetObjectMetadataResponse? metadata;
        try
        {
            metadata = await m_S3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = m_BackupBucket,
                    Key = backupKey
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.BackupVerificationFailed,
                "The backup copy could not be verified; the source object was not deleted.",
                ex);
        }

        var sourceETag = NormalizeETag(candidate.ETag);
        var backupETag = NormalizeETag(metadata?.ETag);

        if (metadata is null ||
            (int)metadata.HttpStatusCode is < 200 or >= 300 ||
            sourceETag.Length == 0 ||
            !string.Equals(sourceETag, backupETag, StringComparison.Ordinal))
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.BackupVerificationFailed,
                "The backup copy did not match the listed source ETag; the source object was not deleted.");
        }

        if (candidate.Size.HasValue && metadata.ContentLength != candidate.Size.Value)
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.BackupVerificationFailed,
                "The backup copy did not match the listed source size; the source object was not deleted.");
        }
    }

    private async Task DeleteSourceAsync(AssetCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            await m_S3.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = m_SourceBucket,
                    Key = candidate.Key,
                    IfMatch = candidate.ETag
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The source is already absent; the verified backup remains the only copy.
            m_Logger.LogInformation("Source object {Key} was already absent after backup verification", candidate.Key);
        }
        catch (AmazonS3Exception ex) when (
            ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.SourceModified,
                "The source object changed after it was backed up; the current source object was preserved.",
                ex);
        }
        catch (Exception ex)
        {
            throw new AssetCleanupKeyException(
                AssetCleanupErrorCodes.DeleteFailed,
                "The source object could not be deleted; the backup was retained.",
                ex);
        }
    }

    private void InvalidateExistsCache(string key)
    {
        try
        {
            m_ImageRepository.InvalidateCache(key);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Local existence cache could not be invalidated for {Key}", key);
        }
    }

    private void ValidateSourceConfiguration()
    {
        if (string.IsNullOrWhiteSpace(m_SourceBucket))
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.SourceBucketUnconfigured,
                "Storage:Bucket must be configured before assets can be cleaned up.");

        if (IsSameBucket(m_SourceBucket, m_AttachmentBucket) ||
            IsSameBucket(m_SourceBucket, m_PortraitBucket))
        {
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.SourceBucketConflict,
                "Storage:Bucket must be different from the attachment and upload buckets.");
        }
    }

    private void ValidateExistingLifecycleRules(IReadOnlyList<LifecycleRule> existingRules, string ownedPrefix)
    {
        foreach (var rule in existingRules)
        {
            if ((rule.Id is ExpireRuleId or DeleteMarkerRuleId) &&
                (!TryGetExactRulePrefix(rule, out var existingPrefix) ||
                 !string.Equals(existingPrefix, ownedPrefix, StringComparison.Ordinal)))
            {
                throw new AssetCleanupException(
                    AssetCleanupErrorCodes.LifecycleConflict,
                    "An existing maintenance lifecycle rule targets a different backup prefix; no objects were changed.");
            }

            if (rule.Status == LifecycleRuleStatus.Disabled)
                continue;

            if (CanShortenRetention(rule, ownedPrefix))
            {
                throw new AssetCleanupException(
                    AssetCleanupErrorCodes.LifecycleConflict,
                    "An existing enabled lifecycle rule could expire backups before the seven-day retention; no objects were changed.");
            }
        }
    }

    private bool CanShortenRetention(LifecycleRule rule, string ownedPrefix)
    {
        if (!RuleOverlapsOwnedPrefix(rule, ownedPrefix))
            return false;

        if (rule.Expiration is { } expiration)
        {
            if (expiration.Days is { } days && days < RetentionDays)
                return true;

            if (expiration.Date is { } date &&
                NormalizeUtc(date) <= m_TimeProvider.GetUtcNow().UtcDateTime.AddDays(RetentionDays))
            {
                return true;
            }
        }

        return rule.NoncurrentVersionExpiration?.NoncurrentDays is { } noncurrentDays &&
               noncurrentDays < RetentionDays;
    }

#pragma warning disable CS0618 // Existing lifecycle configurations can still use the legacy prefix form.
    private static bool RuleOverlapsOwnedPrefix(LifecycleRule rule, string ownedPrefix)
    {
        var hasDeclaredScope = false;
        var overlaps = false;

        if (rule.Filter is { } filter)
        {
            if (filter.And is not null || filter.Tag is not null ||
                filter.ObjectSizeGreaterThan is not null || filter.ObjectSizeLessThan is not null)
            {
                return true;
            }

            var predicate = filter.LifecycleFilterPredicate;
            if (predicate is LifecyclePrefixPredicate prefixPredicate)
            {
                hasDeclaredScope = true;
                overlaps |= PrefixesOverlap(prefixPredicate.Prefix, ownedPrefix);
            }
            else if (predicate is not null)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(filter.Prefix))
            {
                hasDeclaredScope = true;
                overlaps |= PrefixesOverlap(filter.Prefix, ownedPrefix);
            }

            if (!hasDeclaredScope)
                return true;
        }

        if (!string.IsNullOrEmpty(rule.Prefix))
        {
            hasDeclaredScope = true;
            overlaps |= PrefixesOverlap(rule.Prefix, ownedPrefix);
        }

        return !hasDeclaredScope || overlaps;
    }

    private static bool TryGetExactRulePrefix(LifecycleRule rule, out string prefix)
    {
        prefix = "";
        var hasDeclaredScope = false;
        var candidate = "";

        if (!string.IsNullOrEmpty(rule.Prefix))
        {
            hasDeclaredScope = true;
            candidate = rule.Prefix;
        }

        if (rule.Filter is { } filter)
        {
            if (filter.And is not null || filter.Tag is not null ||
                filter.ObjectSizeGreaterThan is not null || filter.ObjectSizeLessThan is not null)
            {
                return false;
            }

            var predicate = filter.LifecycleFilterPredicate;
            if (predicate is LifecyclePrefixPredicate prefixPredicate)
            {
                var predicatePrefix = prefixPredicate.Prefix ?? "";
                if (predicatePrefix.Length == 0)
                    return false;

                if (hasDeclaredScope && !string.Equals(candidate, predicatePrefix, StringComparison.Ordinal))
                    return false;

                hasDeclaredScope = true;
                candidate = predicatePrefix;
            }
            else if (predicate is not null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(filter.Prefix))
            {
                if (hasDeclaredScope && !string.Equals(candidate, filter.Prefix, StringComparison.Ordinal))
                    return false;

                hasDeclaredScope = true;
                candidate = filter.Prefix;
            }
            else if (predicate is null)
            {
                return false;
            }
        }

        if (!hasDeclaredScope)
            return false;

        prefix = candidate;
        return true;
    }
#pragma warning restore CS0618

    private static bool PrefixesOverlap(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return true;

        return left.StartsWith(right, StringComparison.Ordinal) ||
               right.StartsWith(left, StringComparison.Ordinal);
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private void ValidateExecuteConfiguration()
    {
        if (string.IsNullOrWhiteSpace(m_BackupBucket))
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.BackupBucketUnconfigured,
                "AssetCleanup:BackupBucket must be configured before cleanup can execute.");

        if (m_BackupPrefix.Any(char.IsControl) || m_BackupPrefix.Length > 512)
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidBackupPrefix,
                "AssetCleanup:BackupPrefix contains unsupported characters.");

        if (IsSameBucket(m_BackupBucket, m_SourceBucket) ||
            IsSameBucket(m_BackupBucket, m_AttachmentBucket) ||
            IsSameBucket(m_BackupBucket, m_PortraitBucket))
        {
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.BackupBucketConflict,
                "AssetCleanup:BackupBucket must be different from the source, attachment, and upload buckets.");
        }
    }

    private static bool IsSameBucket(string left, string right) =>
        !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ValidateSuffix(string? suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidSuffix,
                "A non-whitespace suffix is required.");

        if (suffix.Any(char.IsControl))
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidSuffix,
                "The suffix must not contain control characters.");

        if (suffix.Length > MaxS3KeyBytes)
            throw new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidSuffix,
                "The suffix must not exceed the storage key length limit.");

        return suffix;
    }

    private static string NormalizeBackupPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return DefaultBackupPrefix;

        var normalized = prefix.Trim().Trim('/');
        return normalized.Length == 0 ? DefaultBackupPrefix : normalized;
    }

    private static string NormalizeETag(string? etag) => (etag ?? "").Trim().Trim('"');

    private string CreateRunPrefix()
    {
        var timestamp = m_TimeProvider.GetUtcNow()
            .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return $"{m_BackupPrefix}/{timestamp}-{Guid.NewGuid():N}/";
    }

    private static AssetSuffixCleanupKeyResult Failure(string key, string code, string error) =>
        new(key, AssetSuffixCleanupStatuses.Failed, ErrorCode: code, Error: error);

    private sealed record AssetCandidate(string Key, string? ETag, long? Size);

    private sealed class AssetCleanupKeyException : Exception
    {
        public AssetCleanupKeyException(string code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
