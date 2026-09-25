param(
    [string]$UnityPath = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe',
    [string]$ProjectPath = (Get-Location).Path,
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss')
)

$ErrorActionPreference = 'Stop'
$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$UnityPath = [System.IO.Path]::GetFullPath($UnityPath)
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity executable not found: $UnityPath" }

$branch = (& git -C $ProjectPath branch --show-current).Trim()
$baseSha = (& git -C $ProjectPath rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the production baseline commit.' }
if ($branch -ne 'work/gam-13-squat-load-calibration') { throw "Unexpected branch: $branch" }

$productionPaths = @(
    'Assets/Scripts/Athlete/PhysicalAthleteRig.cs',
    'Assets/Scripts/Athlete/PoweredJointController.cs',
    'Assets/Scripts/Equipment/PhysicalBarbell.cs',
    'Assets/Scripts/Squat/SquatAttemptLifecycle.cs',
    'Assets/Scripts/Squat/SquatDepthGeometry.cs',
    'Assets/Scripts/Squat/SquatFailureDetector.cs',
    'Assets/Scripts/Squat/SquatLoadResponseAnalyzer.cs',
    'Assets/Scripts/Squat/Unity/SquatDepthLandmarkProvider.cs',
    'Assets/Scripts/Squat/Unity/SquatPhysicalAdapter.cs',
    'Assets/Scripts/Squat/Unity/SquatPhysicalPrototypeController.cs',
    'Assets/Scripts/Squat/Unity/SquatPhysicalTargetForwardKinematics.cs',
    'Assets/Scenes/Prototype/SquatPhysicalPrototype.unity',
    'ProjectSettings/DynamicsManager.asset',
    'ProjectSettings/TimeManager.asset'
)
$productionDiff = & git -C $ProjectPath diff --exit-code -- $productionPaths 2>&1
if ($LASTEXITCODE -ne 0) { throw "Production configuration differs from baseline: $($productionDiff -join ' ')" }
$stagedProductionDiff = & git -C $ProjectPath diff --cached --exit-code -- $productionPaths 2>&1
if ($LASTEXITCODE -ne 0) { throw "Staged production configuration differs from baseline: $($stagedProductionDiff -join ' ')" }

$active = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -eq $UnityPath -and $_.CommandLine -like ('*' + $ProjectPath + '*')
}
if ($active) { throw "Unity already has this project open (PID $($active[0].ProcessId)); use a fresh process after it exits." }

$runDirectory = Join-Path $ProjectPath "Artifacts\Measurements\GAM-13\phase-a-untouched\run-$RunId"
if (Test-Path -LiteralPath $runDirectory) { throw "Run directory already exists: $runDirectory" }
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

$fingerprintPaths = $productionPaths + @(
    'ProjectSettings/ProjectSettings.asset',
    'Assets/Scripts/Squat/Unity/SquatReferenceKinematics.cs',
    'Assets/Scripts/Squat/Unity/SquatObservationCollector.cs',
    'Assets/Tests/PlayMode/GAM13SquatLoadCalibrationHarness.cs',
    'Assets/Tests/PlayMode/GAM13SquatLoadCalibrationTests.cs',
    'Assets/Tests/PlayMode/GAM13UntouchedScreeningTrace.cs'
)
$fingerprints = foreach ($relativePath in $fingerprintPaths) {
    $absolutePath = Join-Path $ProjectPath $relativePath
    [pscustomobject]@{
        path = $relativePath
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $absolutePath).Hash.ToLowerInvariant()
    }
}
$screeningConfigPaths = $productionPaths + 'ProjectSettings/ProjectSettings.asset'
$screeningConfigHashes = @{}
foreach ($relativePath in $screeningConfigPaths) {
    $absolutePath = Join-Path $ProjectPath $relativePath
    $screeningConfigHashes[$relativePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath $absolutePath).Hash.ToLowerInvariant()
}
$unityExecutableInfo = Get-Item -LiteralPath $UnityPath
$unityExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $UnityPath).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schema = 'GAM13_PHASE_A_RUN_MANIFEST_V1'
    runId = $RunId
    startedUtc = [DateTime]::UtcNow.ToString('o')
    branch = $branch
    productionBaselineSha = $baseSha
    worktree = $ProjectPath
    unityExecutable = $UnityPath
    unityExecutableVersion = $unityExecutableInfo.VersionInfo.FileVersion
    unityExecutableSha256 = $unityExecutableSha256
    unityVersion = '6000.3.22f1'
    loadsKg = @(25, 60, 140, 170, 300)
    separateFreshProcesses = $true
    productionConfigurationDiff = 'NONE'
    screeningConfigurationFingerprints = $screeningConfigHashes
    sourceFingerprints = @($fingerprints)
    processIds = @()
}
$manifestPath = Join-Path $runDirectory 'run-manifest.json'
ConvertTo-Json -InputObject $manifest -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

function Quote-Argument([string]$value) {
    return '"' + $value.Replace('"', '\"') + '"'
}

$processIds = [System.Collections.Generic.List[int]]::new()
foreach ($loadKg in @(25, 60, 140, 170, 300)) {
    $loadName = '{0:D3}kg' -f $loadKg
    $caseDirectory = Join-Path $runDirectory $loadName
    New-Item -ItemType Directory -Path $caseDirectory | Out-Null
    $artifactPath = Join-Path $caseDirectory ("GAM13-phase-a-$loadName.csv")
    $resultPath = Join-Path $caseDirectory 'test-results.xml'
    $logPath = Join-Path $caseDirectory 'unity.log'
    $filter = 'PowerliftingSimulator.Tests.GAM13SquatLoadCalibrationTests.GAM13_PHASE_A_SINGLE_LOAD_FRESH_PROCESS'
    $arguments = @(
        '-batchmode',
        '-nographics',
        '-projectPath', $ProjectPath,
        '-runTests',
        '-testPlatform', 'playmode',
        '-testFilter', $filter,
        '-testResults', $resultPath,
        '-logFile', $logPath
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $UnityPath
    $startInfo.WorkingDirectory = $ProjectPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = ($arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
    $startInfo.EnvironmentVariables['GAM13_PHASE_A_LOAD_KG'] = $loadKg.ToString([Globalization.CultureInfo]::InvariantCulture)
    $startInfo.EnvironmentVariables['GAM13_PHASE_A_ARTIFACT_PATH'] = $artifactPath
    $startInfo.EnvironmentVariables['GAM13_PHASE_A_RUN_ID'] = $RunId
    $startInfo.EnvironmentVariables['GAM13_PHASE_A_BASE_SHA'] = $baseSha
    $startInfo.EnvironmentVariables['GAM13_PHASE_A_UNITY_EXE'] = $UnityPath

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "Could not start Unity for $loadName." }
    $processIds.Add($process.Id)
    Write-Output "RUNNING_FRESH_UNITY_PROCESS load_kg=$loadKg pid=$($process.Id)"
    if (-not $process.WaitForExit(900000)) {
        $process.Kill()
        throw "Unity process timed out for $loadName (PID $($process.Id))."
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Unity process failed for $loadName with exit code $($process.ExitCode). See $logPath." }
    if (-not (Test-Path -LiteralPath $resultPath)) { throw "Unity produced no test XML for $loadName. See $logPath." }
    if (-not (Test-Path -LiteralPath $artifactPath)) { throw "Unity produced no trace artifact for $loadName. See $logPath." }

    [xml]$testResult = Get-Content -Raw -LiteralPath $resultPath
    if ($testResult.'test-run'.result -ne 'Passed' -or
        [int]$testResult.'test-run'.failed -ne 0 -or
        [int]$testResult.'test-run'.passed -ne 1) {
        throw "Fresh process screening failed for $loadName. See $resultPath and $logPath."
    }

    $artifactRows = @(Import-Csv -LiteralPath $artifactPath)
    if ($artifactRows.Count -lt 1 -or
        @($artifactRows | Select-Object -ExpandProperty load_kg -Unique).Count -ne 1 -or
        [double]$artifactRows[0].load_kg -ne [double]$loadKg) {
        throw "Trace artifact schema/load validation failed for $loadName."
    }
    foreach ($relativePath in $screeningConfigPaths) {
        $absolutePath = Join-Path $ProjectPath $relativePath
        $currentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $absolutePath).Hash.ToLowerInvariant()
        if ($currentHash -ne $screeningConfigHashes[$relativePath]) {
            throw "Screening configuration changed during ${loadName}: $relativePath"
        }
    }
    $process.Dispose()
}

if (($processIds | Select-Object -Unique).Count -ne 5) { throw 'The five load probes did not use five unique Unity process IDs.' }
$manifest.processIds = @($processIds)
$manifest.completedUtc = [DateTime]::UtcNow.ToString('o')
ConvertTo-Json -InputObject $manifest -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "PHASE_A_SCREENING_COMPLETE=$runDirectory"
