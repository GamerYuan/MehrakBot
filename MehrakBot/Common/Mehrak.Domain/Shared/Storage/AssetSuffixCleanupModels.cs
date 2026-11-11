namespace Mehrak.Domain.Shared.Storage;

public sealed record AssetSuffixCleanupRequest(string Suffix, bool DryRun = true);

public static class AssetSuffixCleanupStatuses
{
    public const string Matched = "matched";
    public const string Deleted = "deleted";
    public const string Failed = "failed";
}

public static class AssetCleanupErrorCodes
{
    public const string InvalidSuffix = "invalid_suffix";
    public const string SourceBucketUnconfigured = "source_bucket_unconfigured";
    public const string SourceBucketConflict = "source_bucket_conflict";
    public const string BackupBucketUnconfigured = "backup_bucket_unconfigured";
    public const string InvalidBackupPrefix = "invalid_backup_prefix";
    public const string BackupBucketConflict = "backup_bucket_conflict";
    public const string BackupBucketUnavailable = "backup_bucket_unavailable";
    public const string LifecycleUnavailable = "lifecycle_unavailable";
    public const string LifecycleRejected = "lifecycle_rejected";
    public const string LifecycleConflict = "lifecycle_conflict";
    public const string ListingFailed = "listing_failed";
    public const string ListingTokenMissing = "listing_token_missing";
    public const string ListingTokenRepeated = "listing_token_repeated";
    public const string SourceMetadataIncomplete = "source_metadata_incomplete";
    public const string BackupKeyTooLong = "backup_key_too_long";
    public const string SourceTooLarge = "source_too_large";
    public const string CopyFailed = "copy_failed";
    public const string BackupVerificationFailed = "backup_verification_failed";
    public const string SourceModified = "source_modified";
    public const string DeleteFailed = "delete_failed";
    public const string UnexpectedError = "unexpected_error";
}

public sealed record AssetSuffixCleanupKeyResult(
    string Key,
    string Status,
    string? BackupKey = null,
    string? ErrorCode = null,
    string? Error = null);

public sealed record AssetSuffixCleanupResult(
    bool DryRun,
    string Suffix,
    int MatchedCount,
    int DeletedCount,
    int FailedCount,
    string? BackupLocation,
    IReadOnlyList<AssetSuffixCleanupKeyResult> Keys);

public sealed class AssetCleanupException : Exception
{
    public AssetCleanupException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
