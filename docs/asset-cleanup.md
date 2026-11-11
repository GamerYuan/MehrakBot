# Asset suffix cleanup

`POST /assets/cleanup-by-suffix` is a superadmin-only Dashboard maintenance endpoint for removing
asset objects whose **full storage key** ends with a literal suffix. It always reads the fixed asset
bucket from `Storage:Bucket`; attachments and user uploads are never source buckets.

## Request

```json
{
  "suffix": "_bg.png",
  "dryRun": true
}
```

- `suffix` is required, must not be whitespace-only, must not contain control characters, and is
  matched with `string.EndsWith(..., StringComparison.Ordinal)`. Matching is literal and
  case-sensitive; `pn` does not match `_bg.png`, and `_BG.png` does not match `_bg.png`.
- `dryRun` defaults to `true`. A dry run lists every matching key across all listing pages and
  performs no bucket setup, lifecycle write, copy, delete, or local cache invalidation.
- The request cannot select a source or backup bucket.

Dry-run response:

```json
{
  "dryRun": true,
  "suffix": "_bg.png",
  "matchedCount": 2,
  "deletedCount": 0,
  "failedCount": 0,
  "backupLocation": null,
  "keys": [
    { "key": "genshin/arcana_1001_bg.png", "status": "matched", "backupKey": null, "errorCode": null, "error": null },
    { "key": "genshin/arcana_1002_bg.png", "status": "matched", "backupKey": null, "errorCode": null, "error": null }
  ]
}
```

Execute response (`dryRun: false`) uses the same shape with per-key `deleted` or `failed` statuses,
counts, and a backup location such as `s3://mehrak-asset-backups/asset-cleanup/20260102T030405Z-<run>/`.
A partial run is explicit: failed keys retain their source object and carry a stable `errorCode`.
The endpoint returns HTTP 200 after a completed run even when some keys failed; inspect `failedCount`
and the per-key statuses.

## Configuration

```json
{
  "AssetCleanup": {
    "BackupBucket": "mehrak-asset-backups",
    "BackupPrefix": "asset-cleanup"
  }
}
```

`Storage:Bucket` must be different (case-insensitive comparison) from `AttachmentStorage:Bucket`
and `UserPortraitStorage:Bucket`; a source collision fails closed before listing, including on a
dry run. This keeps the endpoint assets-only.

`AssetCleanup:BackupBucket` is required for execution and has no default. It must be different
(case-insensitive comparison) from `Storage:Bucket`, `AttachmentStorage:Bucket`, and
`UserPortraitStorage:Bucket`; an empty, missing, or colliding value fails closed before listing.
`AssetCleanup:BackupPrefix` defaults to `asset-cleanup`. The backup bucket may be created lazily on
the first execute run.

## Execution safety

1. The full source listing is paginated before any mutation. A listing error, cancellation, a
   truncated page without a continuation token, or a repeated continuation token aborts the run
   with zero writes.
2. Zero matching keys is a no-op: no bucket setup and no lifecycle write.
3. The backup bucket is checked and, if absent, created. Failures abort before any copy.
4. Backup retention is written before any source deletion. Existing lifecycle rules in the backup
   bucket are read and preserved; only the two maintenance rules are inserted or replaced:
   - current versions expire after 7 days, and noncurrent versions after 7 days;
   - expired delete markers are removed.
   Both rules are scoped to `AssetCleanup:BackupPrefix/`. `PutLifecycleConfiguration` replaces the
   whole configuration, so the read-modify-write is required to avoid dropping unrelated rules.
   Lifecycle read or write failures abort before any copy.

   Existing enabled rules that overlap the owned prefix must not shorten retention. A rule is
   rejected with `lifecycle_conflict` when it overlaps and expires current versions in less than
   seven days, uses an absolute expiration date within seven days, or expires noncurrent versions
   in less than seven days. Prefix filters and legacy `Prefix` values are compared literally. Tag,
   size, `And`, unfiltered, and otherwise unrecognized scopes are treated as overlapping because
   disjointness cannot be proven. Disabled rules are ignored and all safe unrelated rules are
   preserved unchanged. An existing rule using one of the maintenance rule IDs must target exactly
   `AssetCleanup:BackupPrefix/`; changing the configured prefix fails closed instead of removing the
   previous prefix's retention. The conflict check happens before the lifecycle write, any copy, or
   any source deletion.
5. For each key, the source is copied into a unique run folder that preserves the original key. The
   copy is conditional on the listed ETag (`x-amz-copy-source-if-match`), the backup is verified
   with a HEAD for the normalized ETag and, when reported, the source size, and only then is the
   source deleted with an `If-Match` condition. The local Dashboard existence cache is invalidated
   only after the source deletion is confirmed (including an already-absent source).
6. A copy failure, verification mismatch, concurrent source overwrite, or delete failure leaves the
   source object in place and returns a per-key failure. Objects larger than the 5 GiB single-copy
   limit fail safely without multipart scaffolding. A backup copy is deliberately retained when
   verification or deletion fails so it can be inspected.

Already-processed keys are not rolled back if cancellation occurs after mutations begin. New run
folders keep backups unique; there is no cross-instance run lock, so concurrent execute requests can
process the same keys, with conditional copy/delete protecting against overwriting source content.

## Retention and provider caveats

The 7-day lifecycle is an eligibility window, not an exact 168-hour timer. S3-style lifecycle
processing is asynchronous and normally runs about once per day, so physical deletion can occur
later than seven days.

The backup bucket is expected to be versioned by the storage backend. On a versioned bucket the
current-version expiration introduces a delete marker after at least seven days; the previous
version then becomes noncurrent and is removed after its own seven-day noncurrent window, so the
physical purge can take longer than seven days. The delete-marker rule removes markers that have no
remaining noncurrent versions. On an unversioned bucket only the current-version rule applies.

These properties are not guaranteed by response codes alone:

- CopyObject fidelity, conditional copy/delete header enforcement, and lifecycle enforcement by
  the SeaweedFS S3 gateway used in production are not established by existing tests. A successful
  lifecycle `PUT` proves only that the backend accepted the configuration, not that it will expire
  objects. A backend that silently ignores conditional headers cannot be detected from the SDK
  response.
- Verify CopyObject, conditional deletes, and lifecycle expiry against the actual target backend
  before relying on this endpoint for destructive cleanup. If a backend rejects conditional
  operations, the per-key failure path preserves the source; do not replace it with an
  unconditional fallback.
- Execution is application-guarded. The deployment's S3 identity has broad permissions, so the
  endpoint's fixed source bucket, source exclusion, and backup-bucket separation checks are the
  effective safety boundary.

The Dashboard only invalidates its own in-memory existence cache. Other processes (Application,
Bot) may continue to treat a deleted key as present for up to 10 minutes (the cache TTL).
Application re-uploads disk-backed assets at startup when its existence cache reports them missing,
so deleted asset files that still exist on disk can reappear after an Application restart. Remove
the corresponding disk assets if the deletion must be permanent.

## Restore

Backups are ordinary objects under the run folder. To restore, copy
`s3://<BackupBucket>/<run>/<original-key>` back to `s3://<Storage:Bucket>/<original-key>` with the
storage provider's S3 API or the superadmin filer proxy. Keep the backup copy until the restored
asset has been verified.

## Error codes

Whole-request failures: `invalid_suffix` (400), `source_bucket_unconfigured`,
`source_bucket_conflict`, `backup_bucket_unconfigured`, `invalid_backup_prefix`,
`backup_bucket_conflict`, `lifecycle_conflict` (409), and `backup_bucket_unavailable`,
`lifecycle_unavailable`, `lifecycle_rejected`, `listing_failed`, `listing_token_missing`,
`listing_token_repeated` (502).

Per-key failures: `source_metadata_incomplete`, `backup_key_too_long`, `source_too_large`,
`copy_failed`, `backup_verification_failed`, `source_modified`, `delete_failed`, and
`unexpected_error`.
