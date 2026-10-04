[CmdletBinding()]
param(
    # Isolated = engine benchmarks B01-B11; Athlete = B12/B13 (+ oracle);
    # Squat = B14 full mechanics with lockout extension and state hashes,
    # one fresh Unity process per load and repetition; All = every tier.
    [ValidateSet('Isolated', 'Athlete', 'Squat', 'All')]
    [string]$Tier = 'Isolated',
    # Optional NUnit -testFilter (overrides the tier's category; not used by Squat).
    [string]$TestFilter = '',
    [string[]]$LoadsKg = @('25', '60', '140'),
    [int]$Repeats = 1,
    [int]$LockoutExtensionTicks = 300,
    # Extra environment variables for the Unity process.
    [hashtable]$Environment = @{},
    [string]$Label = '',
    # Separate bench root for sweeps/experiments, so they never replace the
    # baseline rows the failure matrix is built from: <sha>/<Scope>.
    [string]$Scope = '',
    # Experiment only: run with the PhysX solver type patched in
    # DynamicsManager.asset for this invocation (restored afterwards).
    [ValidateSet('', 'PGS', 'TGS')]
    [string]$SolverType = '',
    # Experiment only: other DynamicsManager keys patched for this run, e.g. @{ m_FrictionType = '2' }.
    [hashtable]$DynamicsOverrides = @{},
    [string]$UnityExecutable = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe',
    [int]$TimeoutMinutes = 60,
    [switch]$NoCompare
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (!(Test-Path -LiteralPath $UnityExecutable)) { throw "Unity executable not found: $UnityExecutable" }

$sha = (git -C $projectRoot rev-parse --short=7 HEAD).Trim()
# Physics-relevant inputs only: every batch run rewrites ProjectSettings.asset
# (scripting defines), which must not mislabel a clean commit as dirty.
$dirty = [bool](git -C $projectRoot status --porcelain -- Assets Packages ProjectSettings/DynamicsManager.asset ProjectSettings/TimeManager.asset ProjectSettings/ProjectVersion.txt)
$shaDir = if ($dirty) { "$sha-dirty" } else { $sha }
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')
if ($Label) { $runId = "$runId-$Label" }
$benchRoot = Join-Path $projectRoot "Artifacts/Benchmarks/Physics/$shaDir"
if ($Scope) { $benchRoot = Join-Path $benchRoot $Scope }
$runRoot = Join-Path $benchRoot "runs/$runId"
$rawDir = Join-Path $runRoot 'raw'
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

function Invoke-UnityTests {
    param([string]$OutDir, [string[]]$Selector, [hashtable]$Env)
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $saved = @{}
    foreach ($key in $Env.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, [string]$Env[$key])
    }
    $testResults = Join-Path $OutDir 'test-results.xml'
    $arguments = @('-batchmode', '-nographics', '-projectPath', $projectRoot, '-runTests', '-testPlatform', 'playmode',
        '-testResults', $testResults, '-logFile', (Join-Path $OutDir 'unity.log')) + $Selector
    try {
        $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -PassThru -WindowStyle Hidden
        if (!$process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
            Stop-Process -Id $process.Id -Force
            throw "Unity benchmark run exceeded $TimeoutMinutes minutes."
        }
        $exit = $process.ExitCode
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
    $result = [ordered]@{ exit = $exit; result = 'NO_XML'; total = 0; passed = 0; failed = 0 }
    if (Test-Path -LiteralPath $testResults) {
        $xml = [xml](Get-Content -LiteralPath $testResults -Raw)
        $result.result = $xml.'test-run'.result
        $result.total = [int]$xml.'test-run'.total
        $result.passed = [int]$xml.'test-run'.passed
        $result.failed = [int]$xml.'test-run'.failed
    }
    return $result
}

$dynamicsPath = Join-Path $projectRoot 'ProjectSettings/DynamicsManager.asset'
$dynamicsOriginal = $null
if ($SolverType) {
    $DynamicsOverrides['m_SolverType'] = if ($SolverType -eq 'TGS') { '1' } else { '0' }
    $Environment['PHYSICS_BENCHMARK_SOLVER_LABEL'] = $SolverType
}
if ($DynamicsOverrides.Count -gt 0) {
    $dynamicsOriginal = [IO.File]::ReadAllText($dynamicsPath)
    $patched = $dynamicsOriginal
    foreach ($key in $DynamicsOverrides.Keys) {
        $pattern = [regex]::Escape($key) + ': [^\r\n]*'
        if ($patched -notmatch $pattern) { throw "DynamicsManager.asset has no $key." }
        $patched = $patched -replace $pattern, "$key`: $($DynamicsOverrides[$key])"
    }
    [IO.File]::WriteAllText($dynamicsPath, $patched)
}

$started = Get-Date
try {
$summary = [ordered]@{
    run_id = $runId; tier = $Tier; git_sha = $sha; working_tree_dirty = $dirty
    started_utc = $started.ToUniversalTime().ToString('o'); environment = $Environment; invocations = @()
}

if ($Tier -ne 'Squat') {
    $category = switch ($Tier) {
        'Isolated' { 'PhysicsBenchmarkIsolated' }
        'Athlete' { 'PhysicsBenchmarkAthlete' }
        'All' { 'PhysicsBenchmark' }
    }
    $env = @{} + $Environment
    $env['PHYSICS_BENCHMARK_RAW_DIR'] = $rawDir
    $selector = if ($TestFilter) { @('-testFilter', $TestFilter) } else { @('-testCategory', $category) }
    $r = Invoke-UnityTests -OutDir $runRoot -Selector $selector -Env $env
    $summary.invocations += [ordered]@{ selector = ($selector -join ' '); result = $r }
    Write-Host ("PHYSICS_BENCHMARK_RUN tier={0} result={1} passed={2}/{3} exit={4} dir={5}" -f
        $Tier, $r.result, $r.passed, $r.total, $r.exit, $runRoot)
    if ($Tier -in @('Athlete', 'All')) {
        python (Join-Path $PSScriptRoot 'PhysicsOracle.py') --raw-dir $rawDir
    }
}

if ($Tier -in @('Squat', 'All')) {
    $squatRoot = Join-Path $runRoot 'squat'
    foreach ($load in $LoadsKg) {
        for ($rep = 1; $rep -le $Repeats; $rep++) {
            $dir = Join-Path $squatRoot ('{0:000}kg/rep{1:00}' -f [int]$load, $rep)
            $env = @{} + $Environment
            $env['GAM13_V2_LOAD_KG'] = $load
            $env['GAM13_V2_TRACE_PATH'] = Join-Path $dir 'qualification-trace.csv'
            $env['GAM13_V2_ACTUATOR_TRACE_PATH'] = Join-Path $dir 'actuator-diagnostics.csv'
            $env['GAM13_V2_PHYSICS_CONTRACT_PATH'] = Join-Path $dir 'runtime-physics-contract.json'
            $env['GAM50_LOCKOUT_EXTENSION_TICKS'] = [string]$LockoutExtensionTicks
            $env['GAM50_STATE_HASH_PATH'] = Join-Path $dir 'state-hashes.csv'
            $selector = @('-testFilter', 'GAM13V2SquatMechanicsQualificationTests.GAM13_V2_PHYSICAL_SQUAT_MECHANICS')
            $r = Invoke-UnityTests -OutDir $dir -Selector $selector -Env $env
            $summary.invocations += [ordered]@{ load = $load; rep = $rep; result = $r }
            Write-Host ("PHYSICS_BENCHMARK_SQUAT load={0} rep={1} result={2} exit={3}" -f $load, $rep, $r.result, $r.exit)
        }
    }
    python (Join-Path $PSScriptRoot 'SquatBenchmark.py') --squat-root $squatRoot --raw-dir $rawDir
}

}
finally {
    if ($null -ne $dynamicsOriginal) { [IO.File]::WriteAllText($dynamicsPath, $dynamicsOriginal) }
}

$summary.finished_utc = (Get-Date).ToUniversalTime().ToString('o')
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runRoot 'run.json') -Encoding utf8

$receipt = Get-ChildItem -LiteralPath $rawDir -Filter 'runtime-receipt-140kg.json' -ErrorAction SilentlyContinue | Select-Object -First 1
$manifestArgs = @('manifest', '--bench-root', $benchRoot)
if ($receipt) { $manifestArgs += @('--receipt', $receipt.FullName) }
if ($receipt -or !(Test-Path -LiteralPath (Join-Path $benchRoot 'manifest.json'))) {
    python (Join-Path $PSScriptRoot 'Compare-PhysicsBenchmark.py') @manifestArgs
}

if (!$NoCompare) {
    python (Join-Path $PSScriptRoot 'Compare-PhysicsBenchmark.py') aggregate --bench-root $benchRoot
}
