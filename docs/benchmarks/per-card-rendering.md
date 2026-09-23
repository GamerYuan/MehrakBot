# Selective per-card rendering optimizations

Baseline: `685502ee`. Follow-up branch: `perf/per-card-rendering`.

This supersedes performance attribution from the earlier [whole-group experiment](card-rendering.md). Its non-interleaved runs showed substantial movement on unchanged cards. This follow-up selects changes per card using adjacent, order-balanced comparisons, rather than choosing the lowest historical number.

## What is retained

Only **Genshin Character, HSR Character and ZZZ Character** opt into the new bounded drawing helpers. Genshin also bounds its portrait-only layer; its full-card Overlay layer is unchanged. Bounds preserve isolation, text/shadow/stroke extents, wrapping, transforms and overflow. Unsupported decorations/non-finite bounds fall back to unbounded isolation.

Existing public helper signatures and unbounded defaults remain. Internal explicitly named `DrawBounded...` entry points share rendering logic, and only the three selected card services call them. HI3, all charlists, endgame cards, ZZZ Assault and Tower do not opt in. The existing Defense/Theater background caches from the baseline commit remain unchanged after a separate matched recheck.

### Final three-card artifact confirmation

After removing the provisional Assault/Tower opt-ins, the exact resulting production code was rebuilt and rechecked on the 13 fixtures belonging to the three retained cards:

| Card | Fixtures per run | Baseline mean (ms) | Candidate mean (ms) | Median paired reduction | Winning pairs | Material fixture regressions |
|---|---:|---:|---:|---:|---:|---:|
| Genshin Character | 5 | 546.42 | 491.36 | **8.75%** | 5/5 | 0 |
| HSR Character | 5 | 516.37 | 456.94 | **11.97%** | 5/5 | 0 |
| ZZZ Character | 3 | 223.02 | 200.60 | **10.95%** | 5/5 | 0 |

Percentages are the **median of five paired percentage reductions**, not the percentage difference between the two grand means. Corpus/order changes affect absolute timings and measured gains; these numbers must not be mixed with the 73-case experiment below or presented as live production/API latency.

## Acceptance policy fixed before measurement

For each card:

1. Run five adjacent baseline/candidate pairs, alternating AB, BA, AB, BA, AB.
2. Use identical fixtures and one untimed warmup before each measured case, with fresh fixture instances and input data.
3. Calculate each pair's reduction from that card's equal-weight fixture means.
4. Retain only a **median reduction >=5%** and a positive reduction in **at least four of five pairs**.
5. Reject the card if any fixture has a median slowdown greater than 5% and is slower in at least four pairs.
6. Require unchanged-golden correctness checks to pass for both warmup and measurement. No favorable-case filtering or rerunning failed gates until they pass.

The five pairs are a practical selection rule, not a statistical confidence interval. An identical-binary HSR-charlist A/A control showed 3.58% median movement (below the 5% floor), illustrating why small effects are not accepted automatically.

## Candidate screening and verification

The first bounded-layer probe explicitly enabled all applicable card call sites. Non-winning call sites were then removed. The five provisional winners were tested again in the complete 73-case corpus; Assault and Tower failed that verification and were removed. A final 13-case paired confirmation measured the exact three-card artifact above.

| Card | Initial bounded probe median | Initial wins | Later result / decision |
|---|---:|---:|---|
| Genshin Abyss | 5.05% | 3/5 | Reject: inconsistent |
| Genshin Character | 22.27% | 5/5 | 73-case verification 17.13%; exact three-card confirmation 8.75%; retain |
| Genshin Charlist | 3.75% | 4/5 | Reject: below floor |
| Genshin Stygian | 2.48% | 3/5 | Reject |
| Genshin Theater | 4.17% | 4/5 | Reject bounded layers; keep existing background cache |
| HI3 Character | 7.87% | 3/5 | Reject: inconsistent |
| HSR Anomaly | -0.36% | 2/5 | Reject; one material fixture regression |
| HSR Apocalyptic Shadow | 1.53% | 3/5 | Reject |
| HSR Character | 17.70% | 5/5 | 73-case verification 15.78%; exact three-card confirmation 11.97%; retain |
| HSR Charlist | 13.02% | 3/5 | Reject: inconsistent |
| HSR Memory of Chaos | 2.79% | 4/5 | Reject: below floor |
| HSR Pure Fiction | 2.43% | 3/5 | Reject |
| ZZZ Tower | 6.86% | 4/5 | Verification -1.71%, 2/5 wins; removed |
| ZZZ Character | 26.49% | 5/5 | 73-case verification 26.31%; exact three-card confirmation 10.95%; retain |
| ZZZ Charlist | 4.30% | 4/5 | Reject: below floor |
| ZZZ Assault | 8.64% | 4/5 | Verification 3.97%, 4/5 wins; removed |
| ZZZ Defense | 3.35% | 4/5 | Reject bounded layers; keep existing background cache |

The HSR-charlist allocation/load-overlap candidate was also reapplied and independently rechecked: **1.99% median**, 4/5 wins, no material fixture regression. It was reverted again. Application-level lightcone preparation deduplication is outside the measured `GetCardAsync` boundary; no command-level benefit is claimed.

The old asset-waterfall group was screened out without another implementation/measurement pass: its previously modified cards showed at most 4.17% and did not identify a better candidate than the bounded-character paths. This is not a new paired proof that IO overlap can never help, especially with remote storage.

### Existing background caches: matched recheck

Both sides use the **same binary and initialization**. A test-only scoped flag makes derived test services perform the legacy per-request transforms instead of returning prepared clones:

| Card | Legacy request mean (ms) | Cached request mean (ms) | Median paired reduction | Wins | Decision |
|---|---:|---:|---:|---:|---|
| Genshin Theater | 406.46 | 324.28 | **20.36%** | 5/5 | Keep existing cache |
| ZZZ Defense | 68.31 | 63.18 | **5.47%** | 5/5 | Keep existing cache |

There were no material fixture regressions. Theater's existing unprepared-background fallback performs its legacy resize/crop/blur. Defense's test subclass clones the original texture and performs the exact legacy resize inside timed `CreateBackground`; both modes preload that original texture outside timing. This isolates **per-request transformation cost**, not the complete historical implementation, startup cost, or an uncached process's memory footprint. Production has no benchmark environment switches. Theater's roughly 69 MiB cache and startup preprocessing tradeoff remain.

## Measurement controls and remaining uncertainty

- .NET 10 Release, seeded real local LocalStack/S3, serial NUnit execution. No live game API, credentials, injected IO latency or external account access.
- Explicit test-only runner discovers exactly 73 non-explicit golden test cases and invokes their fixture lifecycle. Each case gets a fresh warmup fixture and a fresh measured fixture. Original assertions remain active.
- `AsyncLocal` metric scopes suppress warmup records and preserve original case identities. The existing timer includes image reads, rendering, JPEG encoding and per-request disposal; initialization, assertions and fixture output writes are excluded.
- Baseline and candidate builds use matching benchmark harness/fixture code. Candidate-specific bounded-helper unit tests are omitted from the baseline build; the measured golden corpus is identical.
- Only Application and Application.Tests DLL/PDB files are snapshotted/swapped. Dependencies, assets and configuration stay in place; private configuration/licenses are not copied. SHA256 hashes, pair order and process exit codes are recorded. Binaries and process environment are restored in `finally`; a source-matching build precedes final validation.
- An interrupted first verification batch was marked invalid and excluded in full. The complete five-pair batch was restarted in a new directory; no partial results were spliced together.
- Controls still fluctuate. In the complete verification, unchanged Abyss, Theater and Apocalyptic Shadow had some fixture-level slowdown flags. No opt-ins were enabled for them, and no speed improvement or zero-noise guarantee is claimed for controls. This is another reason to report paired medians and retain only repeated gains.
- Reducing the corpus changes JIT, allocation/GC and other execution conditions. The smaller exact-artifact confirmation intentionally reports its own results rather than reusing larger earlier percentages.

## Reproduce

The new scripts require PowerShell 7. Build two source variants with the same test harness and fixtures. Snapshot only these files from `MehrakBot/Services/Application/Mehrak.Application.Tests/bin/Release/net10.0/` after each Release build:

- `Mehrak.Application.dll` and `.pdb`
- `Mehrak.Application.Tests.dll` and `.pdb`

From the repository root:

```powershell
./scripts/benchmark-card-pairs.ps1 `
    -BaselineDirectory <baseline-snapshot> `
    -CandidateDirectory <candidate-snapshot> `
    -OutputDirectory <new-results-directory> `
    -Filter 'GenshinCharacterCardServiceTests;HsrCharacterCardServiceTests;ZzzCharacterCardServiceTests'

./scripts/summarize-card-pairs.ps1 -Directory <results-directory>
```

Omit `-Filter` for all 73 cases. For the matched cache control, use the same current snapshot for both variants, filter to `GenshinTheaterCardServiceTests;ZzzDefenseCardServiceTests`, and add `-BaselineLegacyBackgrounds`. The scripts reject incomplete batches and mismatching case identities. They do not build source or regenerate goldens.

Raw data/manifests are local under `.pi/benchmarks/per-card/`: `aa-control`, `bounded-all-pairs`, `hsr-charlist-pairs`, `background-pairs`, `final-selection-restarted`, and `accepted-three-pairs`. The interrupted `final-selection-pairs` directory is explicitly invalid.

## Validation

- Full Application suite: **484 passed** on the final three-card source.
- Scoped `dotnet format --verify-no-changes` and `git diff --check`: passed.
- Paired runner exercised against actual fixtures; summary logic additionally checked with synthetic passing, low-gain, inconsistent, fixture-regression and invalid-batch cases.
- Independent named Sol review: PASS, no actionable P0/P1/P2 findings. All workflow calls used their primary named routes; no availability fallbacks were needed.
- Goldens/perceptual thresholds unchanged. No dependencies, migrations, live-API tests, pushes or PR updates.
