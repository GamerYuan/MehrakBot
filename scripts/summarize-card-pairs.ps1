#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$directoryPath = (Resolve-Path -LiteralPath $Directory).Path
$manifest = [IO.File]::ReadAllText((Join-Path $directoryPath 'manifest.json')) | ConvertFrom-Json
if ($manifest.status -ne 'complete' -or $manifest.pairs -ne 5 -or $manifest.runs.Count -ne 10) {
    throw 'Only a complete five-pair batch can be evaluated'
}
function Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -ne 5) { throw 'Expected five paired observations' }
    return $sorted[2]
}
$byPair = @{}
$identities = $null
foreach ($pair in 1..5) {
    foreach ($variant in @('baseline', 'candidate')) {
        $rows = @([IO.File]::ReadAllLines((Join-Path $directoryPath "$pair-$variant.jsonl")) | ForEach-Object { $_ | ConvertFrom-Json })
        if ($rows.Count -eq 0 -or @($rows.test | Sort-Object -Unique).Count -ne $rows.Count) { throw 'Missing or duplicate samples' }
        if ($null -eq $identities) { $identities = @($rows.test | Sort-Object) }
        if (Compare-Object $identities @($rows.test | Sort-Object)) { throw 'Sample identities differ' }
        foreach ($row in $rows) {
            if ($row.elapsedMs -le 0 -or -not [double]::IsFinite([double]$row.elapsedMs)) { throw 'Invalid duration' }
        }
        $byPair["$pair-$variant"] = $rows
    }
}
$results = foreach ($group in ($byPair['1-baseline'] | Group-Object cardType | Sort-Object Name)) {
    $card = $group.Name
    $caseIds = @($group.Group.test)
    $pairs = foreach ($pair in 1..5) {
        $before = @($byPair["$pair-baseline"] | Where-Object { $caseIds -contains $_.test })
        $after = @($byPair["$pair-candidate"] | Where-Object { $caseIds -contains $_.test })
        if (@($before | Where-Object cardType -ne $card).Count -gt 0 -or @($after | Where-Object cardType -ne $card).Count -gt 0) { throw 'Card type mapping changed' }
        $b = ($before.elapsedMs | Measure-Object -Average).Average
        $a = ($after.elapsedMs | Measure-Object -Average).Average
        [pscustomobject]@{ pair = $pair; baselineMs = $b; candidateMs = $a; reductionPercent = 100 * (1 - $a / $b) }
    }
    $fixtures = foreach ($id in $caseIds) {
        $changes = @(foreach ($pair in 1..5) {
            $b = ($byPair["$pair-baseline"] | Where-Object test -eq $id).elapsedMs
            $a = ($byPair["$pair-candidate"] | Where-Object test -eq $id).elapsedMs
            100 * (1 - $a / $b)
        })
        $median = Median $changes
        $losses = @($changes | Where-Object { $_ -lt 0 }).Count
        [pscustomobject]@{ test = $id; medianReductionPercent = $median; losingPairs = $losses; materialRegression = ($median -lt -5 -and $losses -ge 4) }
    }
    $median = Median $pairs.reductionPercent
    $wins = @($pairs | Where-Object reductionPercent -gt 0).Count
    $regressions = @($fixtures | Where-Object materialRegression).Count
    [pscustomobject]@{
        card = $card; fixturesPerRun = $caseIds.Count
        baselineMeanMs = ($pairs.baselineMs | Measure-Object -Average).Average
        candidateMeanMs = ($pairs.candidateMs | Measure-Object -Average).Average
        medianReductionPercent = $median; winningPairs = $wins; materialFixtureRegressions = $regressions
        passesGate = ($median -ge 5 -and $wins -ge 4 -and $regressions -eq 0)
        pairs = @($pairs); fixtures = @($fixtures)
    }
}
$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $directoryPath 'summary.json') -Encoding utf8
$results | Select-Object card, fixturesPerRun, @{n='BeforeMs';e={[math]::Round($_.baselineMeanMs,2)}}, @{n='AfterMs';e={[math]::Round($_.candidateMeanMs,2)}}, @{n='MedianGain%';e={[math]::Round($_.medianReductionPercent,2)}}, winningPairs, materialFixtureRegressions, passesGate | Format-Table -AutoSize
