# Genshin charlist: cold Application benchmark

## Result

Retained the optimizations: the three-run arithmetic mean improved **33.44%** for Application execution and **65.35%** for CardService, exceeding the agreed **10% total-time retention threshold**.

| Run | Baseline total (ms) | Optimized total (ms) | Baseline card (ms) | Optimized card (ms) |
| --- | ---: | ---: | ---: | ---: |
| 1 | 13670.3 | 11054.5 | 5910.6 | 2084.8 |
| 2 | 11570.3 | 5832.2 | 6354.2 | 2165.0 |
| 3 | 10553.5 | 6939.0 | 5904.3 | 2046.5 |
| **Mean** | **11931.37** | **7941.90** | **6056.37** | **2098.77** |

Reduction = `(baseline mean - optimized mean) / baseline mean * 100`.

Baseline production source: `ed77c8ae`. The same corrected benchmark harness was used before and after runtime changes on `perf/genshin-charlist`.

## Changes measured

- Bound character and footer compositing layers to their drawn regions instead of the entire card.
- Start independent weapon and avatar image loads together, joining both groups before disposal.
- Deduplicate avatar/base-weapon preparation and in-flight ascended-art existence checks.
- Check for existing ascended artwork before wiki retrieval after resolving level-40 weapon ascension per character.
- Allocate the Genshin charlist background at its final grid dimensions. Other renderers retain their existing background factory behavior.

No cross-request asset cache or JPEG encoder changes were introduced.

## Method and scope

- Windows development machine, Intel Core i7-13700HX (16 cores / 24 logical processors), .NET SDK 10.0.401, Release build.
- Docker Desktop engine 29.6.2; LocalStack image `sha256:21fe0a67fe7993a5b0082a29bfc94fce5a15b6622c87b0d98f9df2882a9afca3`.
- Three sequential, separate test-process invocations per phase. Each creates and disposes its own LocalStack container and fresh local caches/database.
- Only seven required static element icons are seeded. Each timed run asserts **zero initial dynamic avatar/weapon assets**; the generated attachment is also absent.
- Real HoYoLAB profile/list/detail and wiki requests, real image downloads, the production OpenCV weapon processor via loopback HTTP/2 gRPC, and real S3 image/attachment storage.
- `ExecuteAsync` timing includes dynamic asset preparation, card generation and attachment upload. It excludes container provisioning, static initialization and post-run verification.
- CardService timing includes image retrieval/decoding, rendering, JPEG encoding and cleanup.
- SQLite substitutes for the user database and a fresh in-memory cache substitutes for Redis. Character autocomplete upsert is mocked. NSFW classification is not involved; its test-host dependency throws if accidentally called.
- This measures the **Application flow**, not Bot authentication, dispatcher queueing, Bot-to-Application transport, Bot attachment download or Discord delivery.

All six runs had matching roster fingerprints: **101 characters, 62 distinct weapons**, no level-40 weapons, 101 stored avatars, 62 base-weapon assets and 55 ascended assets. Two of the 57 expected ascended assets consistently used the existing base-image fallback. All outputs were 6,816,825 bytes; equal sizes alone do **not** establish byte-for-byte image identity. Golden-image tests provide separate visual regression coverage.

Level-40 behavior is covered by mocked regression tests (including mixed ascension with the same weapon ID), not by this account's benchmark.

Total-time variance is substantial because of external requests: optimized totals ranged from 5.832 to 11.055 seconds. Three runs are a practical retention check, **not statistical proof or a production latency guarantee**. Upstream/CDN and operating-system caches are not reset. One harness preflight failed on an empty S3 listing before reaching the real API; it was corrected and excluded from the three successful baseline measurements.

## Reproduce

Prerequisites: Docker running, the repository's local license/assets, and valid credentials in the existing ignored `appsettings.test.json` for the Application test project. The benchmark reads credentials inside the test; do not place them in command lines or logs. Server is fixed to Asia.

From `MehrakBot/`:

```powershell
dotnet build Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj -c Release
$previous = $env:MEHRAK_CHARLIST_BENCHMARK
try {
    $env:MEHRAK_CHARLIST_BENCHMARK = '1'
    foreach ($run in 1..3) {
        dotnet test Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj `
            -c Release --no-build `
            --filter 'FullyQualifiedName=Mehrak.Application.Tests.Genshin.CharList.GenshinCharListApplicationServiceTests.IntegrationTest_WithRealApi_FullFlow' `
            --logger 'console;verbosity=normal'
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed; do not count this run.' }
    }
}
finally {
    $env:MEHRAK_CHARLIST_BENCHMARK = $previous
}
```

Each run emits a `CHARLIST_BENCHMARK` JSON line with timings and safe workload/outcome counts. Compare workload fingerprints and fallback counts before comparing means. Do not enable this cold-seeding mode for the ordinary golden-image suite.

## Validation

- Release build succeeded.
- 51 focused charlist tests passed across Genshin, HSR and ZZZ.
- Full Application suite: 425 tests passed; explicit live tests and golden regeneration were not selected.
- Nine Genshin weapon-processor tests passed.
- Scoped `dotnet format --verify-no-changes` and `git diff --check` passed. Tooling reported an NU1510 package-pruning warning and a format workspace-load warning.
- Separate independent `review-sol-high` review approved the integrated changes with no required fixes. Raw local evidence is under `.pi/benchmarks/charlist/` and is not committed.
