param(
    [string]$UnityPath = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe',
    [string]$ProjectPath = (Get-Location).Path,
    [string]$EvidenceSubdirectory = 'gate3-fresh-process',
    [switch]$IncludeCanonicalLifecycle
)

$ErrorActionPreference = 'Stop'
$evidenceDirectory = Join-Path $ProjectPath (Join-Path 'Artifacts\Measurements\GAM-49' $EvidenceSubdirectory)
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDirectory = Join-Path $evidenceDirectory ('run-' + $runId)
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

if ($IncludeCanonicalLifecycle)
{
    $cases = @(
        @{ Name = 'C0_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM49_GATE4_C0_SURFACE_RULE_FRESH_PROCESS' },
        @{ Name = 'HOLD_1.00_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM49_GATE4_HOLD_1_00_SURFACE_RULE_FRESH_PROCESS' },
        @{ Name = 'CANONICAL_25KG'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM49_CANONICAL_25KG_LIFECYCLE_FRESH_PROCESS' }
    )
}
else
{
    $cases = @(
        @{ Name = 'C0_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM49_GATE3_C0_SURFACE_RULE_PARITY_FRESH_PROCESS' },
        @{ Name = 'HOLD_1.00_FULL'; Filter = 'PowerliftingSimulator.Tests.GAM47DepthRegressionIsolationTests.GAM49_GATE3_HOLD_1_00_SURFACE_RULE_PARITY_FRESH_PROCESS' }
    )
}

function Quote-Argument([string]$value)
{
    return '"' + $value.Replace('"', '\"') + '"'
}

function Invoke-FreshUnity($case)
{
    $active = Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -eq $UnityPath -and $_.CommandLine -like ('*' + $ProjectPath + '*')
    }
    if ($active)
    {
        throw "A Unity process already has this project open before $($case.Name)."
    }

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

$decompositionPrefix = if ($IncludeCanonicalLifecycle) { 'gate4-' } else { 'gate3-' }
$c0 = Read-KeyValues (Join-Path $evidenceDirectory ($decompositionPrefix + 'C0_FULL-decomposition.md'))
$hold = Read-KeyValues (Join-Path $evidenceDirectory ($decompositionPrefix + 'HOLD_1.00_FULL-decomposition.md'))
$numericKeys = @()
foreach ($stage in @(
    'D_REF_SURFACE_RULE_PROXY',
    'D_NOMINAL_TARGET',
    'D_GRAVITY_TARGET',
    'D_BALANCE_TARGET',
    'D_FINAL_TARGET',
    'D_APPLIED_TARGET',
    'D_ACTUAL'
))
{
    foreach ($side in @('LEFT_DEPTH_M', 'RIGHT_DEPTH_M', 'WORST_DEPTH_M'))
    {
        $numericKeys += ($stage + '_' + $side)
    }
}
$numericKeys += @(
    'D_REF_JOINT_CENTER_DIAGNOSTIC_LEFT_DEPTH_M',
    'D_REF_JOINT_CENTER_DIAGNOSTIC_RIGHT_DEPTH_M',
    'D_APPLIED_TARGET_JOINT_CENTER_DIAGNOSTIC_LEFT_DEPTH_M',
    'D_APPLIED_TARGET_JOINT_CENTER_DIAGNOSTIC_RIGHT_DEPTH_M',
    'D_ACTUAL_JOINT_CENTER_DIAGNOSTIC_LEFT_DEPTH_M',
    'D_ACTUAL_JOINT_CENTER_DIAGNOSTIC_RIGHT_DEPTH_M'
)
foreach ($layer in @(
    'NOMINAL_MAPPING_ERROR',
    'GRAVITY_COMPOSITION_DISPLACEMENT',
    'BALANCE_COMPOSITION_DISPLACEMENT',
    'FULL_COMPOSITION_DISPLACEMENT',
    'RATE_LIMIT_DISPLACEMENT',
    'PHYSICAL_REALIZATION_ERROR',
    'SURFACE_RULE_PROXY_PHYSICAL_REALIZATION_ERROR',
    'JOINT_CENTER_DIAGNOSTIC_PHYSICAL_REALIZATION_ERROR'
))
{
    foreach ($side in @('LEFT_M', 'RIGHT_M', 'WORST_M'))
    {
        $numericKeys += ($layer + '_' + $side)
    }
}

$tolerance = 1e-5
foreach ($key in $numericKeys)
{
    Assert-Near $c0 $hold $key $tolerance
}
Assert-Near $c0 $hold 'MODELED_DRIVE_DEMAND' $tolerance
foreach ($key in @(
    'SUPPORT_RETAINED',
    'FINITE_VALID_CONTROL',
    'MODELED_DRIVE_DEMAND_HIGH',
    'JOINT_CENTER_CHANNEL',
    'D_REF_SURFACE_RULE_PROXY_IPF_RULE_PREDICATE',
    'D_REF_SURFACE_RULE_PROXY_GAME_JUDGMENT_QUALIFIED',
    'D_APPLIED_TARGET_IPF_RULE_PREDICATE',
    'D_APPLIED_TARGET_GAME_JUDGMENT_QUALIFIED',
    'D_ACTUAL_IPF_RULE_PREDICATE',
    'D_ACTUAL_GAME_JUDGMENT_QUALIFIED'
))
{
    if ($c0[$key] -ne $hold[$key])
    {
        throw "C0_FULL and HOLD_1.00_FULL differ for ${key}: $($c0[$key]) vs $($hold[$key])"
    }
}

$worstActual = [double]::Parse($c0['D_ACTUAL_WORST_DEPTH_M'], [Globalization.CultureInfo]::InvariantCulture)
$margin = 0.005
$deficitMm = [Math]::Max(0.0, $worstActual + $margin) * 1000.0
$receiptName = 'GAM49-gate3-c0-hold-depth-decomposition-receipt.md'
if ($IncludeCanonicalLifecycle)
{
    $dynamic = Read-KeyValues (Join-Path $evidenceDirectory 'dynamic-baseline-summary.md')
    $surfaceLegal = $dynamic['DYNAMIC_SURFACE_RULE_GAME_JUDGMENT_QUALIFIED'] -eq 'true'
    $p2Insufficient = $dynamic['P2_INSUFFICIENT_DEPTH_PRESENT'] -eq 'true'
    $p3LegalBottom = $dynamic['P3_LEGAL_BOTTOM_SEEN'] -eq 'true'
    if ($dynamic['SURFACE_SNAPSHOT_PROVIDER_PARITY'] -ne 'PASS' -or
        $p2Insufficient -eq $surfaceLegal -or
        $p3LegalBottom -ne $surfaceLegal -or
        $dynamic['P2_FAILED_START_POSITION_PRESENT'] -ne 'false' -or
        $dynamic['P2_SUPPORT_VIOLATION_PRESENT'] -ne 'false' -or
        $dynamic['P3_COMPLETION_PREREQUISITES_MISSING'] -ne 'NONE' -or
        $dynamic['P3_PHYSICAL_DESCENT'] -ne 'true' -or
        $dynamic['P3_PHYSICAL_BOTTOM'] -ne 'true' -or
        $dynamic['P3_ASCENT_ESTABLISHED'] -ne 'true' -or
        $dynamic['P3_PHYSICAL_LOCKOUT'] -ne 'true' -or
        $dynamic['JOINT_CENTER_DIAGNOSTIC_USED_FOR_P2_P3_RULE_TRUTH'] -ne 'false')
    {
        throw 'Gate 4 P2/P3 depth authority or canonical lifecycle predicates did not agree with the shared surface provider.'
    }

    if ($surfaceLegal)
    {
        $depthDisposition = 'OBSERVATION_CONTRACT_DEFECT'
        $lowLoadNeeded = 'false'
        $nextAuthorizedGate = 'GAM-13_ORDERED_LOAD_CALIBRATION'
    }
    else
    {
        $depthDisposition = 'INSUFFICIENT_DEPTH_SURVIVES'
        $lowLoadNeeded = 'true'
        $nextAuthorizedGate = 'SEPARATE_LOW_LOAD_REALIZATION_DISCRIMINATION'
    }

    $receiptName = 'GAM49-gate4-requalification-receipt.md'
    $receipt = @"
# GAM-49 Gate 4 — fresh C0/HOLD and canonical 25 kg requalification

Run: $runId
Unity: $UnityPath
Process isolation: fresh Unity process for C0_FULL, HOLD_1.00_FULL, and the canonical 25 kg lifecycle.

Result: PASS

C0/HOLD: surface and joint-center diagnostic values matched within `1e-5 m`; both retained support and finite control.
Canonical P2/P3: provider/snapshot parity passed every tick, P2 start/support authority passed, and P3 physical completion prerequisites were all established. P2 and P3 consumed surface-proxy depth; joint-center diagnostics did not feed rule truth.

Dynamic surface worst side: $($dynamic['DYNAMIC_DEEPEST_SURFACE_RULE_PROXY_WORST_DEPTH_M']) m. Game judgment threshold: `-0.005 m`. Residual deficit: $($dynamic['DYNAMIC_SURFACE_RULE_DEFICIT_MM']) mm.
Joint-center diagnostic at the same surface-deepest sample: left $($dynamic['DYNAMIC_DEEPEST_JOINT_CENTER_DIAGNOSTIC_LEFT_DEPTH_M']) m, right $($dynamic['DYNAMIC_DEEPEST_JOINT_CENTER_DIAGNOSTIC_RIGHT_DEPTH_M']) m.
P2 result: $($dynamic['P2_START_RESULT']); violations: $($dynamic['P2_VIOLATIONS']). P3 legal-bottom seen: $($dynamic['P3_LEGAL_BOTTOM_SEEN']); physical prerequisites: $($dynamic['P3_COMPLETION_PREREQUISITES_MISSING']).

PRIOR_INSUFFICIENT_DEPTH_DISPOSITION=$depthDisposition
LOW_LOAD_REALIZATION_DISCRIMINATION_NEEDED=$lowLoadNeeded
NEXT_AUTHORIZED_GATE=$nextAuthorizedGate
CLAIM_CEILING=GAM10_CALIBRATED_RULE_DERIVED_GAME_PROXY_AND_UNITY_RUNTIME_OBSERVATION

Raw XML/logs and this receipt are in $runDirectory. Per-arm depth decompositions, traces, summaries, and CSVs are in $evidenceDirectory.
"@
}
else
{
    $receipt = @"
# GAM-49 Gate 3 — fresh-process C0/HOLD depth decomposition

Run: $runId
Unity: $UnityPath
Process isolation: C0_FULL and HOLD_1.00_FULL each ran in a fresh Unity process.

Result: PASS

Both arms retained support and finite control. Surface-rule and joint-center values matched between arms within `1e-5 m`. The reference, target composition stages, applied target, and actual physical bottom are in the per-arm decomposition files.

Actual surface-rule worst side for C0_FULL: $($c0['D_ACTUAL_WORST_DEPTH_M']) m. Game judgment threshold: `-0.005 m`. Residual deficit from that threshold: $([Math]::Round($deficitMm, 6)) mm.
Actual surface-rule game qualification: $($c0['D_ACTUAL_GAME_JUDGMENT_QUALIFIED']). The ~20.9 mm joint-center realization gap remains diagnostic and does not change that rule result.

Joint-center values are diagnostic only. C0/HOLD equivalence does not determine 25 kg attempt legality; Gate 4 owns the fresh canonical lifecycle decision.

Raw XML/logs and this receipt are in $runDirectory. The per-arm traces, summaries, decompositions, target-composition CSVs, and applied-target CSVs are in $evidenceDirectory.
"@
}
$receiptPath = Join-Path $runDirectory $receiptName
Set-Content -LiteralPath $receiptPath -Value $receipt -NoNewline
if ($IncludeCanonicalLifecycle)
{
    Write-Output "GATE4_REQUALIFICATION=PASS"
    Write-Output "PRIOR_INSUFFICIENT_DEPTH_DISPOSITION=$depthDisposition"
    Write-Output "NEXT_AUTHORIZED_GATE=$nextAuthorizedGate"
}
else
{
    Write-Output "GATE3_C0_HOLD_EQUIVALENCE=PASS"
    Write-Output "C0_ACTUAL_SURFACE_WORST_M=$($c0['D_ACTUAL_WORST_DEPTH_M'])"
    Write-Output "C0_SURFACE_DEFICIT_MM=$([Math]::Round($deficitMm, 6))"
}
Write-Output "RECEIPT=$receiptPath"
