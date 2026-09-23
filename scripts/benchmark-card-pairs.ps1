#Requires -Version 7.0
# Run from any directory. Snapshots contain ONLY the four named DLL/PDB files;
# dependencies, fixture assets and private configuration stay in the normal test output.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Filter = '',
    [switch]$BaselineLegacyBackgrounds
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'MehrakBot'
$project = 'Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj'
$testOutput = Join-Path $solution 'Services/Application/Mehrak.Application.Tests/bin/Release/net10.0'
$baseline = (Resolve-Path -LiteralPath $BaselineDirectory).Path
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$files = @('Mehrak.Application.dll', 'Mehrak.Application.pdb', 'Mehrak.Application.Tests.dll', 'Mehrak.Application.Tests.pdb')
if (Test-Path -LiteralPath $output) { throw "Output directory must be new: $output" }
foreach ($directory in @($baseline, $candidate, $testOutput)) {
    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath (Join-Path $directory $file) -PathType Leaf)) {
            throw "Missing snapshot or test output file: $directory/$file"
        }
    }
}
$hashes = @{}
foreach ($name in @('baseline', 'candidate')) {
    $directory = if ($name -eq 'baseline') { $baseline } else { $candidate }
    $hashes[$name] = @($files | ForEach-Object {
        @{ file = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $directory $_) -Algorithm SHA256).Hash }
    })
}
New-Item -ItemType Directory -Path $output | Out-Null
$backup = Join-Path $output 'original-binaries'
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $testOutput $file) -Destination $backup }
$names = @('MEHRAK_CARD_BENCHMARK_OUTPUT', 'MEHRAK_CARD_BENCHMARK_FILTER', 'MEHRAK_CARD_BENCHMARK_LEGACY_BACKGROUNDS')
$oldEnvironment = @{}
foreach ($name in $names) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$manifest = [ordered]@{
    baseline = $baseline; candidate = $candidate; filter = $Filter
    baselineLegacyBackgrounds = [bool]$BaselineLegacyBackgrounds
    pairs = 5; hashes = $hashes; runs = @(); status = 'running'
}
$manifestPath = Join-Path $output 'manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Push-Location $solution
try {
    $expectedIdentities = $null
    foreach ($pair in 1..5) {
        $order = if ($pair % 2 -eq 1) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($variant in $order) {
            $source = if ($variant -eq 'baseline') { $baseline } else { $candidate }
            foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $testOutput $file) }
            foreach ($hash in $hashes[$variant]) {
                if ((Get-FileHash -LiteralPath (Join-Path $testOutput $hash.file)).Hash -ne $hash.sha256) {
                    throw "Snapshot hash mismatch for $variant/$($hash.file)"
                }
            }
            $samplePath = Join-Path $output "$pair-$variant.jsonl"
            $logPath = Join-Path $output "$pair-$variant.log"
            [Environment]::SetEnvironmentVariable($names[0], $samplePath, 'Process')
            [Environment]::SetEnvironmentVariable($names[1], $Filter, 'Process')
            $legacy = if ($variant -eq 'baseline' -and $BaselineLegacyBackgrounds) { '1' } else { $null }
            [Environment]::SetEnvironmentVariable($names[2], $legacy, 'Process')
            $start = [DateTimeOffset]::UtcNow
            dotnet test $project -c Release --no-build --no-restore --filter 'FullyQualifiedName=Mehrak.Application.Tests.Benchmarks.CardFixtureBenchmarkTests.RunGoldenCardFixtures_WithWarmup_RecordsMeasuredSamples' -- NUnit.NumberOfTestWorkers=0 *> $logPath
            $exitCode = $LASTEXITCODE
            $manifest.runs += @{ pair = $pair; variant = $variant; startedUtc = $start.ToString('O'); endedUtc = [DateTimeOffset]::UtcNow.ToString('O'); exitCode = $exitCode }
            $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
            if ($exitCode -ne 0) { throw "Pair $pair $variant failed; inspect $logPath. Entire batch is invalid." }
            if (-not (Test-Path -LiteralPath $samplePath)) { throw "No samples for pair $pair $variant" }
            $samples = @([IO.File]::ReadAllLines($samplePath) | ForEach-Object { $_ | ConvertFrom-Json })
            $identities = @($samples.test)
            if ($samples.Count -eq 0 -or @($identities | Sort-Object -Unique).Count -ne $samples.Count) { throw 'Missing or duplicate case identities' }
            foreach ($sample in $samples) {
                if ($sample.elapsedMs -le 0 -or -not [double]::IsFinite([double]$sample.elapsedMs)) { throw 'Invalid elapsed duration' }
            }
            if ($null -eq $expectedIdentities) { $expectedIdentities = $identities }
            elseif (($expectedIdentities -join "`n") -cne ($identities -join "`n")) { throw 'Case identities/order differ across variants or pairs' }
            Write-Output "Pair $pair ${variant}: $($samples.Count) measured cases passed"
        }
    }
    $manifest.status = 'complete'
} catch {
    $manifest.status = 'invalid'
    $manifest['error'] = $_.Exception.Message
    throw
} finally {
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $backup $file) -Destination (Join-Path $testOutput $file) }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') }
    Pop-Location
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
