# Remaining card-rendering optimizations

Baseline production revision: `ae5be081` (the Genshin charlist optimization).
Follow-up branch: `perf/card-rendering`.

## Decision and scope

Each optimization group was implemented, checked against unchanged goldens, and measured in three fresh test processes. The retention threshold was a **10% reduction in the declared group's mean `GetCardAsync` duration**. Groups below the threshold were reverted, including their candidate-specific tests. The stronger perceptual comparator and opt-in timing harness remain independently useful test improvements.

| Group | Fixed fixture set | Samples before / after | Before mean (ms) | After mean (ms) | Reduction | Decision |
|---|---|---:|---:|---:|---:|---|
| A: HSR charlist allocation, overlapping reads, lightcone preparation deduplication | HSR charlist, 1 case | 3 / 3 | 447.036 | 406.252 | 9.12% | Reverted |
| B: independent asset-loading phases and failure-safe joins | 43 cases across 9 card types | 129 / 129 | 682.755 | 677.177 | 0.82% | Reverted |
| C: bounded shared helper, portrait and rank layers | All 73 card cases | 219 / 219 | 525.555 | 488.724 | 7.01% | Reverted |
| D: ZZZ Defense prepared background | 5 Defense cases | 15 / 15 | 115.476 | 79.298 | **31.33%** | **Retained** |
| E: Genshin Theater prepared variants | 10 Theater cases | 30 / 30 | 834.689 | 355.126 | **57.45%** | **Retained** |

B's predefined timing set: Genshin Abyss, Stygian, Theater and Character; HSR Character, Pure Fiction and Apocalyptic Shadow; ZZZ Character; HI3 Character. Its implementation also touched inherited HSR Memory behavior and ZZZ charlist failure joining; these were validated but were not in B's predefined timing aggregate. All B changes were reverted.

C did improve individual character means: Genshin 24.87%, HSR 12.93%, ZZZ 21.29%; HI3 regressed 7.98%. Its broad, predefined aggregate nevertheless missed the gate. The group was not redefined after observing these results. These narrower opportunities can be reconsidered under a separately agreed retention policy.

### Per-process group means (ms)

| Group | Before runs 1 / 2 / 3 | After runs 1 / 2 / 3 |
|---|---|---|
| A | 442.134 / 454.658 / 444.316 | 331.261 / 447.580 / 439.916 |
| B | 689.113 / 684.814 / 674.338 | 671.973 / 669.730 / 689.829 |
| C | 527.554 / 523.898 / 525.213 | 481.638 / 497.364 / 487.170 |
| D | 115.606 / 106.130 / 124.694 | 76.066 / 66.079 / 95.748 |
| E | 842.560 / 840.175 / 821.333 | 383.662 / 398.309 / 283.405 |

## Method and limitations

- Windows, Intel Core i7-13700HX, .NET 10 Release build, Docker/LocalStack S3.
- Existing fixture data and real local S3 reads/decode/render/JPEG encode; no live HoYoLAB requests, credentials or artificial network delays.
- The same 73 golden-image cases ran in the same serial fixture order in each process (`NUnit.NumberOfTestWorkers=0`). Each process seeded its own test storage through the existing fixture setup. This is **not** a cold real-API benchmark or a production latency prediction.
- Each golden case constructs and initializes a service before its first render. Initialization, fixture seeding, assertions and output-file writes are outside the existing `ObserveCardGenerationDuration` timer. Image reads, rendering, JPEG encoding and per-request disposal are inside it.
- No additional warm-up render was introduced. JIT/cache/order effects and ordinary timing variation remain; three samples are not a statistical confidence bound. The same complete test order was retained for every phase rather than selectively rerunning favorable cases.
- Every case has equal weight in its declared group. Group means are averaged across all three processes. Reduction is `100 * (1 - afterMean / beforeMean)`.
- A, B and C were measured independently against the original production baseline and reverted before the next candidate. D and E affect separate card types and were measured together against that baseline.
- A's application-service preparation deduplication is **outside** the measured card-service boundary; these figures cannot establish its command-level benefit. Likewise, local S3 makes IO-overlap savings less representative of higher-latency deployments.
- Raw JSONL, logs, fixture output snapshots and reverted candidate patches are retained locally under `.pi/benchmarks/cards/`, not committed. Goldens were never regenerated.

## Retained runtime behavior

### ZZZ Defense

The exact existing textured-background resize is now performed once during static initialization: 1000×1080, Crop, Bicubic, and the existing source-centered `CenterCoordinates`. Each request clones the prepared image. No texture, drawing coordinates, rank blending or asset-loading behavior changes.

### Genshin Theater

Initialization prepares five immutable backgrounds for the supported difficulties, retaining the original operation order:

1. Brightness 0.35 on the source.
2. Resize to the required height when necessary, using the existing default sampler.
3. Center-crop to 1900×height.
4. Gaussian blur with sigma 10.

Each request clones its difficulty's variant. Dimensions, attribution position and drawing behavior remain unchanged. Direct rendering with an unprepared background still applies the legacy transform. The variant collection is fixed-size, cloning/disposal is synchronized, and service disposal releases the variants. Its initialization alias is also singleton-scoped: the existing scoped alias would otherwise dispose this disposable singleton when the temporary startup scope ended. A real service-container startup regression test verifies both post-startup rendering and root-provider disposal.

**Tradeoff:** preprocessing moves to startup rather than disappearing. Five retained variants add approximately **69 MiB** of pixel memory. The quoted improvement excludes initialization cost and does not imply a faster one-shot initialize-and-render operation. Existing non-variant static resource lifecycle is unchanged.

## Perceptual correctness checks

`IsImage.IdenticalTo` remains a compatibility alias; new tests can use the more accurate name `IsImage.PerceptuallyEquivalentTo`.

The old 8×8 grayscale AverageHash has been replaced with:

- A 32×32 luminance sample and 63 non-DC low-frequency DCT hash bits; default minimum similarity **82%**.
- Independent premultiplied-RGBA color and structural-edge comparisons: **94% global color**, **90% global structure**.
- A **96% localized-detail** floor over the four worst analysis regions, so small material defects are not simply averaged away across the whole card.
- Bidirectional neighborhood matching within two analysis pixels, accommodating modest local shifts instead of demanding exact pixels.
- Exact dimensions after orientation; alpha-aware features; diagnostic component scores and differing source-coordinate regions.

The explicit threshold argument controls only DCT similarity, not the independent guardrails. The old 98% AverageHash threshold is not numerically comparable to the new default.

Analysis is bounded to about 131,072 pixels and a 1024-pixel maximum dimension. Consequently, two analysis pixels represent different source-pixel distances on different card sizes. This is intentionally tolerant, not a proof that every tiny missing glyph will be caught, particularly on tall cards. Tightening it to catch every single-pixel change would conflict with the requested layout-shift tolerance.

Calibration tests cover identical images, stream positions, small global/local shifts, moderate JPEG/antialiasing variation, equal-luminance hue changes, missing material artwork, clipped labels, substantial displacement and dimension mismatches. All 73 existing golden cases passed before any production edits and throughout retained optimization validation. Expected images and assertion thresholds were not changed to hide rendering failures.

## Final validation and review

- Release build and full Application suite: **453 passed**. Explicit live-API and golden-regeneration tests were not run.
- Scoped `dotnet format --verify-no-changes` and `git diff --check`: passed.
- The first independent review found the startup-scope disposal defect described above. The container regression reproduced the failure before the registration fix and passed afterward; the complete suite was rerun.
- Independent re-review approved the corrected patch with no actionable P0/P1/P2 findings.
- No dependency changes or migrations.

## Reproduce fixture timing

Run from `MehrakBot/`, with Docker, assets and the normal build prerequisites available. `CardBenchmarkMetrics.Create()` is a no-op unless the output variable is set. It writes one JSON object per completed timer with only `test`, `cardType` and `elapsedMs`; it does not record game payloads or credentials.

```powershell
dotnet build Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj -c Release
$outputDirectory = Join-Path $env:TEMP ('mehrak-card-timings-' + [guid]::NewGuid())
New-Item -ItemType Directory $outputDirectory | Out-Null
try {
    foreach ($run in 1..3) {
        $env:MEHRAK_CARD_BENCHMARK_OUTPUT = Join-Path $outputDirectory "run-$run.jsonl"
        dotnet test Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj `
            -c Release --no-build `
            --filter '(Name~MatchesGoldenImage|Name~ShouldMatchGoldenImage)' `
            -- NUnit.NumberOfTestWorkers=0
        if ($LASTEXITCODE -ne 0) { throw "Run $run failed; do not use its timings" }
    }
} finally {
    Remove-Item Env:MEHRAK_CARD_BENCHMARK_OUTPUT -ErrorAction SilentlyContinue
}
```

Use a fresh output path for every process: capture appends rather than overwrites. Verify 73 unique case identities per file before comparing revisions, and keep the same test instrumentation/comparator on both revisions. Never run explicit golden-generation or live-API tests for this benchmark.
