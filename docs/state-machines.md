# State machines and lifecycle contracts

This guide locates lifecycle ownership across processes. The 42 families below overlap: a request can participate in authentication, dispatch, rendering and storage at once. States in this guide describe existing tasks, typed outcomes and durable records, not a requirement to add mutable status fields or a universal FSM framework. Framework internals and the separately maintained Vue frontend are outside this inventory.

## Contract boundaries

- Preserve domain `Complete`/`Empty`/`NotUnlocked`, API `Available`/`Empty`/`Locked`, and partial item outcomes; an empty successful response is not a transport failure. See [CommandOutcomes](../MehrakBot/Common/Mehrak.Domain/Command/Models/CommandOutcomes.cs) and [GameApiResponse](../MehrakBot/Common/Mehrak.GameApi/Shared/Types/GameApiResponse.cs).
- Notes resource recovery, expeditions, store/temple progress and endgame cycle facts are **upstream snapshots**, not locally advancing scheduled workflows. Unknown upstream statuses must not be silently reclassified as success.
- Keep input ordering and failure precedence. Character batches retain indexed generated/failed/not-attempted items; any latched timeout wins the envelope even when cards completed. Redemption is sequential after normalization/deduplication; timeout preserves earlier results/status updates and marks later codes not attempted, but still fails the envelope. Check-in fans out in game-record order; a timeout fails the envelope only when there are no successful/already-checked-in items. Caller cancellation is distinct from a local timeout and need not retain the same partial ledger.
- Never blindly retry redemption, check-in, ambiguous storage/DB commits or committed-DB/cache-failed mutations. Reconcile at the owning boundary first. HTTP resilience is not a transaction or an exactly-once guarantee.
- Keep credentials and PII out of transition diagnostics: no passphrases, tokens, cookies, credential-bearing URLs/bodies, user profile payloads or raw upstream errors. Use allowlisted reason codes and non-sensitive operation identifiers. This is a diagnostic design constraint, not a claim that every existing log is already sanitized.
- Do not unify distinct policies: zero active portraits is allowed; admitted upload attempts consume quota even if later validation/classification/storage fails; Bot profile deletion defaults to delete-all; Bot remembers server before dispatch while Dashboard remembers it only after a successful domain result.

## Inventory by owner

Paths link to concrete owners. Each row gives states, advancing events/guards, effects and recovery. Read the owner before changing a contract; nearby game-specific implementations may differ.

### Identity, admission and presentation

| Family | States, events/guards, effects and recovery | Owner |
|---|---|---|
| 1. Login/session | Anonymous → OAuth challenge/callback → session; missing/expired/revoked session rejects principal. Re-login recovers. | [AuthController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Auth/AuthController.cs), [DashboardCookieEvents](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Shared/Auth/DashboardCookieEvents.cs) |
| 2. Authorization | Reader/game contributor/superadmin/root permissions constrain operations; principal is rebuilt from current permissions, not a permanent login-time grant. Denied writes have no mutation effect. | [GameWriteAuthorization](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Shared/Auth/GameWriteAuthorization.cs), [UserController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/User/UserController.cs) |
| 3. Profile collection | Absent → validated add → numbered profiles; delete reindexes under per-user mutation coordination; default Bot delete removes all and can leave an empty collection. | [ProfileCommandModule](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Modules/ProfileCommandModule.cs), [UserProfileDeletionExtensions](../MehrakBot/Common/Mehrak.Infrastructure/User/Extensions/UserProfileDeletionExtensions.cs) |
| 4. Credential revision | Validate → encrypt → commit → revoke unlocks; legacy upgrade uses compare-and-swap. Revision mismatch refuses stale in-flight authentication; retry with current credentials. | [AuthModalModule](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Modules/AuthModalModule.cs), [UserProfileCredentialExtensions](../MehrakBot/Common/Mehrak.Infrastructure/User/Extensions/UserProfileCredentialExtensions.cs) |
| 5. Bot unlock/rendezvous | Live revision-bound ticket → defer; stale/missing → modal pending → submission or timeout → quota/decrypt/revision check → ticket. Wrong passphrase retains reservation; success/corruption releases it. | [AuthenticationMiddlewareService](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Services/AuthenticationMiddlewareService.cs) |
| 6. Dashboard unlock | Locked → passphrase required → reservation → success/invalid/corrupt/rate-limited. TTL ticket is bound to session and credential revision; stale ticket requires unlock again. | [DashboardProfileAuthenticationService](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Shared/Auth/DashboardProfileAuthenticationService.cs) |
| 7. Request/attempt admission | Allowed/rejected: HTTP IP/login windows, command GCRA, passphrase reservations and portrait attempt slots are separate policies. Expiry/release differs; rejection does not imply an upstream attempt. | [Dashboard Program](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Program.cs), [CommandRateLimitService](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Services/RateLimit/CommandRateLimitService.cs), [PassphraseAttemptRateLimiter](../MehrakBot/Common/Mehrak.Infrastructure/Shared/PassphraseAttemptRateLimiter.cs), [PortraitUploadRateLimitService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/PortraitUploadRateLimitService.cs) |
| 8. Discord acknowledgement/presentation | Initial response → modal or deferred acknowledgement → followup. Private/public presentation stays Bot-owned; downloaded streams live through send, then dispose. Remove is owner-only. See transition table below. | [AuthenticationResult](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Abstractions/IAuthenticationMiddlewareService.cs), [CommandExecutorService](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Services/CommandExecutorService.cs), [RemoveCharacterCardModule](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Modules/RemoveCharacterCardModule.cs) |
| 9. Remembered server | Unset → explicit server → preference commit. Bot commits before dispatch; Dashboard only after successful domain result. Missing first-use server asks for selection; failure timing is deliberately not unified. | [CommandExecutorService](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Services/CommandExecutorService.cs), [DashboardApplicationExecutorService](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Shared/Services/DashboardApplicationExecutorService.cs) |

### Application execution and API adaptation

| Family | States, events/guards, effects and recovery | Owner |
|---|---|---|
| 10. Queue/worker completion | Submitted → queued/dropped → executing → result/cancelled/faulted. Bounded channel limits concurrency; stop closes admission and cancels/drains pending completions. | [CommandDispatcher](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/CommandDispatcher.cs) |
| 11. Transport/result adaptation | gRPC wait → typed result or transport cancellation/error. Dashboard validation/unlock/transport success is distinct from the enclosed domain success; supported card empty states map to HTTP 200/null storage key. | [GrpcApplicationService](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/GrpcApplicationService.cs), [DashboardApplicationExecutorService](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Shared/Services/DashboardApplicationExecutorService.cs) |
| 12. Exception/image repair | Caller cancellation/local timeout/auth/unknown map separately. Missing-image repair can rerun a command up to three attempts; this is not a general mutation retry policy. | [BaseApplicationService](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/BaseApplicationService.cs) |
| 13. Character batch | Resolve input → bounded render slots → generated/failed/not-attempted; timeout latch prevents later slots starting and retains ordered facts. See precedence above. | [CharacterBatchProcessor](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/CharacterBatchProcessor.cs), [GenshinCharacterApplicationService](../MehrakBot/Services/Application/Mehrak.Application/Genshin/Character/GenshinCharacterApplicationService.cs) |
| 14. Redemption | Normalize/dedupe or load saved codes → sequential attempts → semantic per-code ledger → saved code statuses. Delay separates attempts; timeout retains partial results, cancellation differs. No blind mutation retry. | [CodeRedeemApplicationService](../MehrakBot/Services/Application/Mehrak.Application/CodeRedeem/CodeRedeemApplicationService.cs), [CodeRedeemApiService](../MehrakBot/Common/Mehrak.GameApi/CodeRedeem/CodeRedeemApiService.cs) |
| 15. Daily check-in | Already today → short circuit; otherwise records → fan-out → ordered aggregation. Mark profile day only when all items succeeded/already checked in; failed local save does not undo upstream check-ins. | [DailyCheckInService](../MehrakBot/Services/Application/Mehrak.Application/DailyCheckIn/DailyCheckInService.cs), [DailyCheckInApiService](../MehrakBot/Common/Mehrak.GameApi/DailyCheckIn/DailyCheckInApiService.cs) |
| 16. Speculative primary work | Cached UID permits primary request alongside profile validation; absent UID serializes profile lookup then primary. Validation still gates returned result; a warm cache is not fresh credential validation. | [BaseApplicationService](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/BaseApplicationService.cs) |
| 17. Upstream request | Credential checks → HTTP send → status/envelope/auth/retcode/data checks → semantic response. Linked budget distinguishes caller cancellation from timeout; unknown responses remain failures. | [HoYoLabApi](../MehrakBot/Common/Mehrak.GameApi/Shared/HoYoLabApi.cs) |
| 18. API cache fill/pagination | Hit/miss/malformed/incomplete/bypass → guarded fetch/merge → publish. Warm hits can precede credential validation; aggregate pagination publishes only after all pages succeed. Recover via miss/refetch, not partial aggregate publication. | [GameRoleApiService](../MehrakBot/Common/Mehrak.GameApi/GameRole/GameRoleApiService.cs), [GenshinCharacterApiService](../MehrakBot/Common/Mehrak.GameApi/Genshin/GenshinCharacterApiService.cs), [ZzzCharacterEntryPageApiService](../MehrakBot/Common/Mehrak.GameApi/Zzz/ZzzCharacterEntryPageApiService.cs) |

### Images and rendering

| Family | States, events/guards, effects and recovery | Owner |
|---|---|---|
| 19. Image update | Present → true; missing → download → optional process → upload result. Acquired responses/streams are locally owned until disposal; false is not an exception. See detailed table. | [ImageUpdaterService](../MehrakBot/Common/Mehrak.GameApi/Shared/ImageUpdaterService.cs) |
| 20. Card fingerprint/cache | Effective inputs + renderer version → fingerprint → hit or render/store. User-to-stock fallback changes fingerprint. Bump renderer version when output changes; a hit avoids regeneration. | [BaseApplicationService](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/BaseApplicationService.cs), [GenshinCharacterApplicationService](../MehrakBot/Services/Application/Mehrak.Application/Genshin/Character/GenshinCharacterApplicationService.cs) |
| 21. Portrait selection | Active metadata → user stream/config, or stock stream/config. Null user download falls back to stock; metadata selection alone is not byte availability. | [PortraitResolutionHelper](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/PortraitResolutionHelper.cs) |
| 22. ZZZ portrait search | Requested base/outfit slots → reference/locales/candidates → dedupe/match → full/partial/timeout result. Local matching budget may return partial discoveries; caller cancellation propagates. | [ZzzPortraitResolver](../MehrakBot/Services/Application/Mehrak.Application/Zzz/Character/ZzzPortraitResolver.cs) |
| 23. Renderer readiness | Uninitialized → static resources loading → ready/failure; singleton resources disposed by owning service, not request bags. Initialization failure stops dependent startup. | [AsyncInitializationHostedService](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/AsyncInitializationHostedService.cs), [GenshinArcanaCardService](../MehrakBot/Services/Application/Mehrak.Application/Genshin/Arcana/GenshinArcanaCardService.cs), [GenshinTheaterCardService](../MehrakBot/Services/Application/Mehrak.Application/Genshin/Theater/GenshinTheaterCardService.cs) |
| 24. Render request ownership | Allocate/clone → render → encode JPEG → rewind → caller-owned stream. Failure disposes output; request bag disposes temporary assets on every exit. Never put shared singleton images in the request bag. | [CardServiceBase](../MehrakBot/Services/Application/Mehrak.Application/Shared/Renderers/CardServiceBase.cs) |

### Persistence, projections and portrait saga

| Family | States, events/guards, effects and recovery | Owner |
|---|---|---|
| 25. Character projection/alias transactions | Character mutation: lock → DB commit → refresh/retry projection or committed-but-cache-unavailable; cache failure is not permission to repeat mutation. Alias mutation: normalize/validate conflicts → per-game lock → DB commit; reads query DB directly, with no separate Redis alias projection. | [CharacterCacheService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/CharacterCacheService.cs), [AliasService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/AliasService.cs) |
| 26. Global portrait/stat config | Cache miss → DB read → sliding entry; mutation commits DB then publishes/invalidates. Portrait config coordinates individual and aggregate fills/publication with a per-game DB lock and fresh reload, not detached captured entities. Stat config is a separate cache policy. | [CharacterPortraitConfigService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/CharacterPortraitConfigService.cs), [CharacterStatService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/CharacterStatService.cs) |
| 27. User portrait upload saga | Validate/attempt quota/classify → group lock → S3 PUT → metadata commit. Ambiguous commit gets independent DB reconciliation; an uncommitted object gets best-effort deletion intent rather than unsafe immediate compensation. A crash/DB outage can still leave an orphan. | [UserPortraitsController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Character/UserPortraitsController.cs), [UserPortraitService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/UserPortraitService.cs) |
| 28. Portrait activation/deletion | Inactive ↔ active under group lock; at most one active, **zero allowed**. Metadata/config deletion and storage cleanup intent commit together; deletion does not require synchronous S3 success. | [UserPortraitService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/UserPortraitService.cs), [CharacterDbContext](../MehrakBot/Common/Mehrak.Infrastructure/Character/CharacterDbContext.cs) |
| 29. Deletion outbox | Durable pending → least-recent-attempt selection → S3 delete → remove intent; failures remain pending with attempt metadata. Fair retry, not dead-letter/backoff. See detailed table. | [UserPortraitDeletionProcessor](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/UserPortraitDeletionProcessor.cs), [UserPortraitDeletionHostedService](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/UserPortraitDeletionHostedService.cs) |

### Storage, caches and sessions

| Family | States, events/guards, effects and recovery | Owner |
|---|---|---|
| 30. Attachment provisioning/ownership | Unconfigured/setup attempted/degraded; best-effort setup does not guarantee availability. PUT then GET yields caller-owned download stream; store inputs stay caller-owned. Missing objects differ from infrastructure faults. | [AttachmentStorageInitializer](../MehrakBot/Common/Mehrak.Infrastructure/Shared/Storage/AttachmentStorageInitializer.cs), [AttachmentStorageService](../MehrakBot/Common/Mehrak.Infrastructure/Shared/Storage/AttachmentStorageService.cs) |
| 31. Attachment expiration | Complete version listing → stale eligible key → historical versions deleted → exact listed current version deleted. Incomplete listing or missing version IDs prevents unsafe cleanup; next scan retries. | [AttachmentExpirationBackgroundService](../MehrakBot/Common/Mehrak.Infrastructure/Shared/Storage/AttachmentExpirationBackgroundService.cs) |
| 32. Asset cleanup batch | Validate → complete listing → dry run OR retention setup → conditional backup → verify → conditional delete → local invalidate. Guard failures retain source; inspect per-key results before retry. | [AssetPrefixCleanupService](../MehrakBot/Common/Mehrak.Infrastructure/Shared/Storage/AssetPrefixCleanupService.cs), [asset cleanup guide](asset-cleanup.md) |
| 33. Asset existence cache | Unknown → present/absent → ten-minute expiry; writes/deletes update local state. Invalidation is process-local, not fleet-wide; other processes can remain stale. | [ImageRepository](../MehrakBot/Common/Mehrak.Infrastructure/Shared/Storage/ImageRepository.cs) |
| 34. Sensitive unlock cache | Process-local live entry → expiry/stale/type mismatch → miss. No Redis plaintext credential fallback; separate shared revocation watermark constrains unlock reuse. Process loss requires unlocking again. | [RedisCacheService](../MehrakBot/Common/Mehrak.Infrastructure/Shared/Cache/RedisCacheService.cs), [AuthenticationMiddlewareService](../MehrakBot/Services/Bot/Mehrak.Bot/Shared/Services/AuthenticationMiddlewareService.cs) |
| 35. Session cleanup/validation claim | Valid → expired → grace period → DB cleanup. Daily token-validation timestamp reserves an attempt, not proof upstream validation completed; expired sessions cannot be refreshed into validity. | [DashboardSessionService](../MehrakBot/Common/Mehrak.Infrastructure/Auth/Services/DashboardSessionService.cs), [SessionCleanupService](../MehrakBot/Common/Mehrak.Infrastructure/Auth/Services/SessionCleanupService.cs) |

### Native processing and operations

| Family | States, events/guards, effects and recovery | Owner |
|---|---|---|
| 36. Native classification | Model readiness → byte/header/pixel/single-frame budget → bounded admission → decode/postcheck/inference → result. Over-budget input rejects before inference; native synchronous work is not made RPC-cancellable by a client token. Dashboard additionally accepts actual PNG/JPEG only. | [classification RPC](../MehrakBot/Services/ImageProcessor/Mehrak.ImageProcessor/Shared/Services/GrpcImageProcessorService.cs), [budget contract tests](../MehrakBot/Services/ImageProcessor/Mehrak.ImageProcessor.Tests/Grpc/NsfwClassifierBoundsTests.cs), [upload validation](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Character/UserPortraitsController.cs) |
| 37. Native matching/weapon processing | Decode → align/match/refine → match/no-match or output/no-output. Native buffers are request-owned; no-output is an explicit result, not publication. | [PortraitImageMatcher](../MehrakBot/Services/ImageProcessor/Mehrak.ImageProcessor/Shared/Services/PortraitImageMatcher.cs), [GenshinWeaponImageProcessor](../MehrakBot/Services/ImageProcessor/Mehrak.ImageProcessor/Shared/Services/GenshinWeaponImageProcessor.cs) |
| 38. Host/startup/migration | Infrastructure ready → one-shot migration completion → dependent services. Content/model initialization can fail fast; attachment setup is best effort. Migration failure blocks dependent Aspire services. | [AppHost](../MehrakBot/Mehrak.AppHost/Program.cs), [migration Worker](../MehrakBot/Services/MigrationService/Mehrak.MigrationService/Worker.cs), [architecture](architecture.md#startup-ownership-and-database-migrations) |
| 39. Content administration/freshness | Authorized validated CRUD → commit/failure; release-note cache invalidation is best effort. Codes add/remove return 204 on save success and 500 on caught DB save failure; list administration is not upstream redemption. | [DocumentationController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Documentation/DocumentationController.cs), [ReleaseNotesController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/ReleaseNote/ReleaseNotesController.cs), [CodesController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/CodeRedeem/CodesController.cs) |
| 40. Weapon preview/publication | Base icon required → uploaded images processed → aligned preview/no result. Separate validated ascended-icon overwrite publishes and invalidates local cache; preview alone never publishes. | [GenshinWeaponsController](../MehrakBot/Services/Dashboard/Mehrak.Dashboard/Genshin/GenshinWeaponsController.cs) |
| 41. HoYoLAB embed | URL/post ID parse → deferred public interaction → retrieval/render → output/error. Parsing failure exits before retrieval; presentation remains Bot-owned. | [HylEmbedModule](../MehrakBot/Services/Bot/Mehrak.Bot/HylEmbed/HylEmbedModule.cs) |
| 42. Health/resilience/operations | Ready/dependency failure, HTTP retries/circuit transitions and expected-failure telemetry are separate from command completion. CI validation, tag image builds and release archives have independent success/failure boundaries; image builds include MigrationService. | [ServiceDefaults](../MehrakBot/Services/Mehrak.ServiceDefaults/Extensions.cs), [dispatcher telemetry](../MehrakBot/Services/Application/Mehrak.Application/Shared/Services/CommandDispatcher.cs), [validation](../.github/workflows/validation.yml), [build](../.github/workflows/build.yml), [release](../.github/workflows/release.yml) |

## Discord acknowledgement

The owner returns an `AuthenticationResponsePhase` with the interaction context that may be used next. A modal response acknowledges the original interaction; a submitted modal is a **different** interaction that must itself be acknowledged.

| State/event or guard | Effect and next legal response | Recovery |
|---|---|---|
| Initial validation fails, or user/profile absent before modal | Initial ephemeral response; pre-modal `NotFound` carries `InitialResponse`. | Correct input/add profile, then new invocation. |
| Live cached unlock | Defer original interaction ephemerally; success carries `Followup`. | Executor sends result/error as followup. |
| Missing/stale unlock | Send modal on original interaction; await rendezvous. | Timeout returns `Timeout`; executor does not try a second initial response or send via absent modal context. |
| Modal submitted | Defer modal interaction ephemerally before quota/decrypt/revision work. | Use submitted context for followups. |
| Quota/decrypt/revision failure after defer | `Failure` carries `Followup`. | Ephemeral followup; retry only after correcting cause/quota expiry. |
| Profile removed while modal work was in flight | `NotFoundAfterAcknowledgement` carries `Followup`, not `InitialResponse`. | Ephemeral followup; no duplicate acknowledgement. |
| Authenticated dispatch completes | Bot renders outcome; followup visibility follows command/presentation rules, not wire flags. | Streams disposed after send/failure; owner-only Remove affects message, not stored result. |

## Image ownership and save result

`ImageUpdaterService` owns HTTP responses, source streams and processor output; repository upload consumes bytes but does not take ownership. Multi-image downloads remain sequential and each response is registered for cleanup before acquiring the next.

| State/event or guard | Effect/result | Ownership/recovery |
|---|---|---|
| Repository says present | Return `true` without download. | Existence-cache staleness is a separate contract. |
| Missing single-image URL / unsuccessful HTTP status | Return `false`. | Dispose acquired response/streams; no upload. |
| Download succeeds, no processing | Rewind source and return repository upload boolean. | Source/response disposed on exit, including upload failure. |
| Processing yields null sentinel/empty output | Return `false`; no upload. | Dispose processed/source resources. |
| Processing yields bytes (single or multi) | Rewind processed stream; return upload boolean, including `false`. | Dispose output, acquired streams and all acquired responses. |
| Later multi-download fails/throws/cancels | Do not report success. | Cleanup includes responses acquired before the failure. |
| Caller cancellation | Rethrow cancellation. | Cleanup still runs. |
| Linked local timeout | Return `false`. | Cleanup still runs; other transport/storage exceptions are not generally converted to `false`. |

A boolean reports this helper's result, not end-to-end command success. In particular, Genshin's multi-image update caller has its own fallback policy and does not promote every rejected upload to a command failure.

## Committed DB and cache projection

PostgreSQL is authoritative; Redis is a projection. For portrait config, individual and aggregate cache misses, DB mutations and post-commit publication participate in the same per-game `portrait-config:{game}` advisory-lock namespace. [CharacterDbLock](../MehrakBot/Common/Mehrak.Infrastructure/Character/Services/CharacterDbLock.cs) uses a PostgreSQL transaction-scoped advisory lock; it is a **no-op for other providers**. SQLite/in-memory provider tests alone cannot establish cross-process serialization.

| State/event or guard | Effect/next state | Recovery/limit |
|---|---|---|
| Individual/aggregate cache hit | Return cached config without lock or DB read. | May be stale while publication is pending or unavailable. |
| Miss (or malformed individual entry) | Begin transaction, acquire per-game lock, read current DB and fill cache under lock; malformed individual entry is removed. | Individual getter catches failures and returns null; aggregate read can throw. Do not assume identical corruption handling. |
| Valid upsert; character mapping exists | Lock, reload/create/update entity, save and commit DB. | Validation/missing mapping returns false; DB-update conflict has a bounded three-attempt retry, not arbitrary external mutation retries. |
| DB committed → publication | Acquire a new transaction/lock, reload current DB (never publish the write's captured entity), invalidate aggregate then publish individual with one-hour sliding expiry. | A later writer may already have committed; fresh reload prevents an older captured value replacing it. |
| Publication fails after commit | Log projection failure; upsert still returns true. | DB change remains committed. No atomic DB/cache transaction and no durable refresh intent. |
| Crash/outage between commit and projection | DB committed, cache potentially old or partly invalidated. | Successful later publication or eventual miss/refill can recover; hot sliding entries are not a bounded-staleness guarantee. Do not replay mutation just to repair cache. |

CharacterCacheService separates DB commit from projection refresh and exposes cache-unavailable failures; its retry/invalidation details are not interchangeable with portrait-config's boolean return. AliasService commits mutations under a per-game DB lock and queries aliases directly from the DB; it has no separate Redis alias projection or post-commit cache refresh. Global stat configuration has its own cache behavior and is not covered by the portrait-config ordering protocol.

## Portrait deletion pending and retry

The metadata transaction removes portrait/config rows and enqueues deletion intent together. Dashboard's hosted worker processes durable intent; S3 cleanup is not part of that DB transaction.

| State/event or guard | Effect/next state | Recovery/limit |
|---|---|---|
| Metadata deletion commits | Portrait no longer selected; cleanup row is pending. | Storage object may still exist until worker succeeds. |
| Batch selection | Select up to 100 by `LastAttemptAtUtc ?? CreatedAtUtc`, then creation time and ID. | Least-recent-attempt ordering lets later rows progress past repeatedly failing rows when failure bookkeeping succeeds. |
| Requested intent already absent | Return true. | Missing-row success supports repeated/concurrent cleanup calls. |
| S3 delete throws or returns status >= 300 | Record incremented attempts, last-attempt UTC and bounded error; return false, keep pending. | Retry on later worker pass; no dead-letter, discard threshold or exponential backoff. |
| S3 succeeds, DB intent removal commits | Remove intent; return true. | Complete. |
| S3 succeeds, DB removal fails | Record failure if possible; keep pending, return false. | Repeat S3 delete then DB cleanup; storage success alone is not completion. |
| Caller cancellation | Propagate cancellation. | Durable row remains unless removal already committed. |
| Failure recording itself fails | Log bookkeeping failure. | Row remains eligible; fairness is not guaranteed when attempt timestamps cannot be persisted. |

This is at-least-once cleanup, not an exclusive worker claim or exactly-once S3 deletion. Error text is bounded but not a substitute for sanitization; do not add credentials or PII to deletion errors.

## DX and AX: finding contracts and checking changes

AX means **agent experience**: discover the real owner, typed contract, effect boundary and focused check before editing. Use this inventory to narrow scope, inspect the linked source and existing regression fixture, and avoid inventing a parallel lifecycle representation. Keep state/effect/recovery documentation with its owner when behavior changes.

The following are targeted commands, not a record of tests run. Run from `MehrakBot/`; choose the affected slice. They do not apply migrations or start deployed services. `UserPortraitPostgreSqlConcurrencyTests` is deliberately excluded because its setup calls `MigrateAsync`; do not run that fixture under a no-migrations constraint. The dedicated `CharacterPortraitConfigPostgreSqlCacheTests` and `UserPortraitDeletionProcessorTests` use `EnsureCreatedAsync` instead. Integration fixtures can start disposable Testcontainers and require Docker; PostgreSQL concurrency checks are necessary for advisory-lock claims. Application rendering also needs assets/LFS/license; native tests need OpenCV/ONNX prerequisites. Verify nonzero matching test discovery and inspect failures rather than treating a filtered no-op as success.

```powershell
# Acknowledgement and presentation
 dotnet test Services/Bot/Mehrak.Bot.Tests/Mehrak.Bot.Tests.csproj --filter "FullyQualifiedName~AuthenticationMiddlewareServiceTests|FullyQualifiedName~CommandExecutorPresentationTests"
# Image save result and acquisition cleanup
 dotnet test Common/Mehrak.GameApi.Tests/Mehrak.GameApi.Tests.csproj --filter "FullyQualifiedName~ImageUpdaterServiceTests"
# Code-list persistence response (not upstream redemption)
 dotnet test Services/Dashboard/Mehrak.Dashboard.Tests/Mehrak.Dashboard.Tests.csproj --filter "FullyQualifiedName~CodesControllerTests"
# Individual/aggregate cache ordering, deletion recovery and PostgreSQL concurrency
 dotnet test Common/Mehrak.Infrastructure.Tests/Mehrak.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CharacterPortraitConfigServiceTests|FullyQualifiedName~CharacterPortraitConfigPostgreSqlCacheTests|FullyQualifiedName~UserPortraitDeletionProcessorTests|FullyQualifiedName~UserPortraitServiceTests"
# Ordered partial outcomes and distinct timeout precedence
 dotnet test Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj --filter "FullyQualifiedName~CharacterBatchProcessorTests|FullyQualifiedName~CodeRedeemApplicationServiceTests|FullyQualifiedName~DailyCheckInServiceTests"
# Typed domain/wire semantics
 dotnet test Common/Mehrak.Domain.Tests/Mehrak.Domain.Tests.csproj --filter "FullyQualifiedName~CommandResultMappingTests"
# Public native input budgets
 dotnet test Services/ImageProcessor/Mehrak.ImageProcessor.Tests/Mehrak.ImageProcessor.Tests.csproj --filter "FullyQualifiedName~NsfwClassifierBoundsTests"
```

For docs-only edits, check source/path links and `git diff --check`; do not run infrastructure or rendering suites merely to validate prose. Dashboard REST supports cards and content/profile administration, not notes/check-in/redemption execution parity. Browser testing requires a separately hosted frontend and verified login; never infer frontend behavior from this backend inventory.
