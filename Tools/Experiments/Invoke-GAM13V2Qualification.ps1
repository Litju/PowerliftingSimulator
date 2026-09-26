[CmdletBinding()]
param(
    [ValidateSet('Standing', 'Lifecycle')]
    [string]$Mode = 'Standing',
    [string]$UnityExecutable = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe',
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss')
)

$projectRoot = (Get-Location).Path
if (!(Test-Path -LiteralPath $UnityExecutable)) {
    throw "Unity 6000.3.22f1 executable not found: $UnityExecutable"
}

$testFilter = if ($Mode -eq 'Standing') { 'GAM13V2StandingQualificationTests' } else { 'GAM13V2LifecycleQualificationTests' }
$artifactRoot = Join-Path $projectRoot "Artifacts/Measurements/GAM-13/v2-$($Mode.ToLowerInvariant())/$RunId"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$previousLoad = $env:GAM13_V2_LOAD_KG
$previousTrace = $env:GAM13_V2_TRACE_PATH
$unityVersion = (Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -TotalCount 1).Split(':')[1].Trim()
$results = [System.Collections.Generic.List[object]]::new()

try {
    foreach ($loadKg in @(25, 60, 140, 170, 300)) {
        $loadDirectory = Join-Path $artifactRoot ('{0:000}kg' -f $loadKg)
        New-Item -ItemType Directory -Path $loadDirectory -Force | Out-Null
        $tracePath = Join-Path $loadDirectory 'qualification-trace.csv'
        $testResults = Join-Path $loadDirectory 'test-results.xml'
        $logPath = Join-Path $loadDirectory 'unity.log'

        $env:GAM13_V2_LOAD_KG = [string]$loadKg
        $env:GAM13_V2_TRACE_PATH = $tracePath
        $arguments = @(
            '-batchmode', '-nographics',
            '-projectPath', $projectRoot,
            '-runTests', '-testPlatform', 'playmode',
            '-testFilter', $testFilter,
            '-testResults', $testResults,
            '-logFile', $logPath
        )
        $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -PassThru -WindowStyle Hidden
        $deadline = [DateTime]::UtcNow.AddMinutes(20)
        $xml = $null
        while ([DateTime]::UtcNow -lt $deadline) {
            if (Test-Path -LiteralPath $testResults) {
                try {
                    $candidate = [xml](Get-Content -LiteralPath $testResults -Raw)
                    if ($candidate.'test-run'.result -in @('Passed', 'Failed', 'Failed(Child)')) {
                        $xml = $candidate
                        break
                    }
                }
                catch { }
            }
            if (!(Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { break }
            Start-Sleep -Seconds 2
        }

        $live = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($null -ne $xml -and $live) {
            try { Wait-Process -Id $process.Id -Timeout 15 -ErrorAction Stop }
            catch {
                $live = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
                if ($live -and [IO.Path]::GetFullPath($live.Path) -eq [IO.Path]::GetFullPath($UnityExecutable)) {
                    Stop-Process -Id $process.Id -Force
                }
            }
        }

        $process.Refresh()
        $exitCode = if ($process.HasExited) { $process.ExitCode } else { -1 }
        $passed = $null -ne $xml -and $xml.'test-run'.result -eq 'Passed' -and
            [int]$xml.'test-run'.total -gt 0 -and [int]$xml.'test-run'.failed -eq 0 -and
            $exitCode -eq 0 -and (Test-Path -LiteralPath $tracePath)
        $summary = [PSCustomObject]@{
            Mode = $Mode
            LoadKg = $loadKg
            UnityPid = $process.Id
            TestResult = if ($xml) { $xml.'test-run'.result } else { 'NO_XML' }
            Tests = if ($xml) { [int]$xml.'test-run'.total } else { 0 }
            Passed = if ($xml) { [int]$xml.'test-run'.passed } else { 0 }
            Failed = if ($xml) { [int]$xml.'test-run'.failed } else { 1 }
            ProcessExit = $exitCode
            Qualification = if ($passed) { 'PASS' } else { 'FAIL' }
            Trace = $tracePath
        }
        $results.Add($summary)

        @(
            '# GAM-13 V2 qualification receipt',
            '',
            "MODE=$Mode",
            "LOAD_KG=$loadKg",
            "UNITY_VERSION=$unityVersion",
            "UNITY_PID=$($process.Id)",
            'FRESH_PROCESS=true',
            "TEST_FILTER=$testFilter",
            "TEST_RESULT=$($summary.TestResult)",
            "TESTS=$($summary.Tests)",
            "PASSED=$($summary.Passed)",
            "FAILED=$($summary.Failed)",
            "PROCESS_EXIT=$exitCode",
            "QUALIFICATION=$($summary.Qualification)",
            "TRACE=$tracePath"
        ) | Set-Content -LiteralPath (Join-Path $loadDirectory 'runner-receipt.md') -Encoding utf8
    }
}
finally {
    $env:GAM13_V2_LOAD_KG = $previousLoad
    $env:GAM13_V2_TRACE_PATH = $previousTrace
}

$results | Format-Table -AutoSize
if ($results | Where-Object { $_.Qualification -ne 'PASS' }) { exit 1 }
