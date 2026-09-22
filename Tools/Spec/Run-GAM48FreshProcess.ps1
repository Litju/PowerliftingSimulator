param(
    [string]$UnityPath = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe',
    [string]$ProjectPath = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $ProjectPath 'Artifacts\Measurements\GAM-48\fresh-process'
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDirectory = Join-Path $outputDirectory ('run-' + $runId)
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

$cases = @(
    @{ Name = 'BASELINE_R1'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_DYNAMIC_BASELINE_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-47\dynamic-baseline-summary.md' },
    @{ Name = 'BASELINE_R2'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_DYNAMIC_BASELINE_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-47\dynamic-baseline-summary.md' },
    @{ Name = 'HOLD_0.00_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_HOLD_0_00_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\HOLD_0.00_FULL-summary.md' },
    @{ Name = 'HOLD_0.25_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_HOLD_0_25_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\HOLD_0.25_FULL-summary.md' },
    @{ Name = 'HOLD_0.55_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_HOLD_0_55_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\HOLD_0.55_FULL-summary.md' },
    @{ Name = 'HOLD_0.80_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_HOLD_0_80_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\HOLD_0.80_FULL-summary.md' },
    @{ Name = 'HOLD_1.00_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_HOLD_1_00_FULL_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\HOLD_1.00_FULL-summary.md' },
    @{ Name = 'C0_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_C0_FULL_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\C0_FULL-summary.md' },
    @{ Name = 'C1_NO_DYNAMIC_BALANCE'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_C1_NO_DYNAMIC_BALANCE_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\C1_NO_DYNAMIC_BALANCE-summary.md' },
    @{ Name = 'C2_NOMINAL_ONLY'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM48_C2_NOMINAL_ONLY_FRESH_PROCESS'; Copy = 'Artifacts\Measurements\GAM-48\fresh-process\C2_NOMINAL_ONLY-summary.md' }
)

function Quote-Argument([string]$value)
{
    return '"' + $value.Replace('"', '\"') + '"'
}

function Invoke-FreshUnity($case)
{
    $resultPath = Join-Path $runDirectory ($case.Name + '.xml')
    $logPath = Join-Path $runDirectory ($case.Name + '.log')
    $arguments = @(
        '-batchmode',
        '-nographics',
        '-projectPath', $ProjectPath,
        '-runTests',
        '-testPlatform', 'playmode',
        '-testFilter', $case.Filter,
        '-testResults', $resultPath,
        '-logFile', $logPath
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $UnityPath
    $startInfo.WorkingDirectory = $ProjectPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = ($arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process.WaitForExit(900000))
    {
        $process.Kill()
        throw "Unity process timed out for $($case.Name) (PID $($process.Id))."
    }

    if (-not (Test-Path -LiteralPath $resultPath))
    {
        throw "Unity did not produce a result XML for $($case.Name). See $logPath."
    }

    [xml]$result = Get-Content -Raw -LiteralPath $resultPath
    if ($result.'test-run'.result -ne 'Passed' -or
        [int]$result.'test-run'.failed -ne 0 -or
        [int]$result.'test-run'.passed -ne 1)
    {
        throw "Fresh Unity case $($case.Name) failed. See $resultPath and $logPath."
    }

    $sourcePath = Join-Path $ProjectPath $case.Copy
    if (-not (Test-Path -LiteralPath $sourcePath))
    {
        throw "Expected evidence for $($case.Name) was not written: $sourcePath"
    }
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $runDirectory ($case.Name + '-summary.md'))
}

function Read-KeyValues([string]$path)
{
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $path)
    {
        if ($line -match '^([^=]+)=(.*)$')
        {
            $values[$matches[1]] = $matches[2]
        }
    }
    return $values
}

function Assert-Near([hashtable]$left, [hashtable]$right, [string]$key, [double]$tolerance)
{
    $leftValue = [double]::Parse($left[$key], [Globalization.CultureInfo]::InvariantCulture)
    $rightValue = [double]::Parse($right[$key], [Globalization.CultureInfo]::InvariantCulture)
    if ([Math]::Abs($leftValue - $rightValue) -gt $tolerance)
    {
        throw "$key differs by $([Math]::Abs($leftValue - $rightValue)): $leftValue vs $rightValue"
    }
}

foreach ($case in $cases)
{
    Write-Output "RUNNING_FRESH_PROCESS=$($case.Name)"
    Invoke-FreshUnity $case
}

$hold = Read-KeyValues (Join-Path $runDirectory 'HOLD_1.00_FULL-summary.md')
$c0 = Read-KeyValues (Join-Path $runDirectory 'C0_FULL-summary.md')
$numericKeys = @(
    'SETTLED_MEAN_WORST_DEPTH_M',
    'SETTLED_MIN_WORST_DEPTH_M',
    'SETTLED_MAX_WORST_DEPTH_M',
    'DEEPEST_WORST_DEPTH_M',
    'DEEPEST_SQ',
    'BOTTOM_BAR_VELOCITY_MPS',
    'BOTTOM_PELVIS_VELOCITY_MPS'
)
$tolerance = 1e-5
foreach ($key in $numericKeys)
{
    Assert-Near $hold $c0 $key $tolerance
}
foreach ($key in @('PHASE', 'SUPPORT_RETAINED', 'FINITE_VALID_CONTROL', 'LEGAL', 'DEEPEST_TICK'))
{
    if ($hold[$key] -ne $c0[$key])
    {
        throw "HOLD_1.00_FULL and C0_FULL differ for ${key}: $($hold[$key]) vs $($c0[$key])"
    }
}

$baseline1 = Read-KeyValues (Join-Path $runDirectory 'BASELINE_R1-summary.md')
$baseline2 = Read-KeyValues (Join-Path $runDirectory 'BASELINE_R2-summary.md')
foreach ($key in @('DYNAMIC_DEEPEST_LEFT_DEPTH', 'DYNAMIC_DEEPEST_RIGHT_DEPTH', 'DYNAMIC_DEEPEST_DEPTH', 'DYNAMIC_DEPTH_DEFICIT', 'DYNAMIC_SQ_AT_DEEPEST'))
{
    Assert-Near $baseline1 $baseline2 $key $tolerance
}
foreach ($key in @('DYNAMIC_DEEPEST_TICK', 'P2_START_RESULT', 'P2_VIOLATIONS', 'P3_PHYSICAL_DESCENT', 'P3_PHYSICAL_BOTTOM', 'P3_LEGAL_BOTTOM', 'P3_ASCENT_ESTABLISHED', 'P3_PHYSICAL_LOCKOUT', 'P3_MISSING_STAGE'))
{
    if ($baseline1[$key] -ne $baseline2[$key])
    {
        throw "Fresh baseline repeats differ for ${key}: $($baseline1[$key]) vs $($baseline2[$key])"
    }
}

$receiptPath = Join-Path $outputDirectory 'GAM48-gate3-equivalence-receipt.md'
$receipt = @"
# GAM-48 Gate 3 — fresh-process equivalence receipt

Run: $runId
Unity: $UnityPath
Process isolation: one Unity process per arm; no scene-reload substitution.

## Hard gate

`HOLD_1.00_FULL == C0_FULL`: PASS

Compared numeric fields: $($numericKeys -join ', ')
Absolute tolerance: `1e-5`

Compared categorical fields: `PHASE`, `SUPPORT_RETAINED`, `FINITE_VALID_CONTROL`, `LEGAL`, `DEEPEST_TICK`.

Canonical baseline fresh-process repeatability: PASS for two independent
processes, including numeric tolerance `1e-5` and categorical lifecycle/P3
classification equality.

The raw XML, logs, and per-arm summaries are retained in $runDirectory.
"@
Set-Content -LiteralPath $receiptPath -Value $receipt -NoNewline
Write-Output "GATE3_EQUIVALENCE=PASS"
Write-Output "RECEIPT=$receiptPath"
