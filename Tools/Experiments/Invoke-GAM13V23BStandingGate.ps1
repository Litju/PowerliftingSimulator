[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(0, 25)]
    [float]$LoadKg,
    [string]$UnityExecutable = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe',
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss')
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Get-Location).Path
$expectedUnityVersion = '6000.3.22f1'
$projectUnityVersion = (Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -TotalCount 1).Split(':')[1].Trim()
if ($projectUnityVersion -ne $expectedUnityVersion) {
    throw "GAM-13 V2-3B requires Unity $expectedUnityVersion, project declares $projectUnityVersion."
}
if (!(Test-Path -LiteralPath $UnityExecutable)) {
    throw "Unity executable not found: $UnityExecutable"
}

$loadName = '{0:000}kg' -f $LoadKg
$outputDirectory = Join-Path $projectRoot "Artifacts/Measurements/GAM-13/v2-3b-substrate/standing/$loadName/$RunId"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$tracePath = Join-Path $outputDirectory 'qualification-trace.csv'
$testResultsPath = Join-Path $outputDirectory 'test-results.xml'
$logPath = Join-Path $outputDirectory 'unity.log'
$receiptPath = Join-Path $outputDirectory 'runner-receipt.md'

$previousLoad = $env:GAM13_V2_LOAD_KG
$previousTrace = $env:GAM13_V2_TRACE_PATH
try {
    $env:GAM13_V2_LOAD_KG = $LoadKg.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:GAM13_V2_TRACE_PATH = $tracePath
    $arguments = @(
        '-batchmode', '-nographics',
        '-projectPath', $projectRoot,
        '-runTests', '-testPlatform', 'playmode',
        '-testFilter', 'GAM13V2StandingQualificationTests',
        '-testResults', $testResultsPath,
        '-logFile', $logPath
    )
    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddMinutes(20)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $testResultsPath) {
            try {
                [xml]$candidate = Get-Content -LiteralPath $testResultsPath -Raw
                if ($candidate.'test-run'.result -in @('Passed', 'Failed', 'Failed(Child)')) {
                    try { Wait-Process -Id $process.Id -Timeout 15 -ErrorAction Stop }
                    catch { }
                    break
                }
            }
            catch { }
        }
        if (!(Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Seconds 2
    }

    $process.Refresh()
    if (!$process.HasExited) {
        Stop-Process -Id $process.Id -Force -Confirm:$false -ErrorAction SilentlyContinue
        $process.Refresh()
    }
    $testResult = 'NO_XML'
    $testCount = 0
    $passed = $false
    if (Test-Path -LiteralPath $testResultsPath) {
        [xml]$xml = Get-Content -LiteralPath $testResultsPath -Raw
        $testResult = $xml.'test-run'.result
        $testCount = [int]$xml.'test-run'.total
        $passed = $testResult -eq 'Passed' -and [int]$xml.'test-run'.failed -eq 0 -and
            $process.HasExited -and $process.ExitCode -eq 0 -and (Test-Path -LiteralPath $tracePath)
    }
    @(
        '# GAM-13 V2-3B standing gate receipt',
        '',
        "LOAD_KG=$LoadKg",
        "UNITY_VERSION=$projectUnityVersion",
        "UNITY_PID=$($process.Id)",
        'FRESH_PROCESS=true',
        "TEST_RESULT=$testResult",
        "TESTS=$testCount",
        "PROCESS_EXIT=$(if ($process.HasExited) { $process.ExitCode } else { -1 })",
        "QUALIFICATION=$(if ($passed) { 'PASS' } else { 'FAIL' })",
        "TRACE=$tracePath"
    ) | Set-Content -LiteralPath $receiptPath -Encoding utf8

    if (!$passed) {
        throw "GAM-13 V2-3B standing gate failed for $LoadKg kg; inspect $receiptPath and $logPath."
    }
}
finally {
    if ($null -eq $previousLoad) { Remove-Item Env:GAM13_V2_LOAD_KG -ErrorAction SilentlyContinue }
    else { $env:GAM13_V2_LOAD_KG = $previousLoad }
    if ($null -eq $previousTrace) { Remove-Item Env:GAM13_V2_TRACE_PATH -ErrorAction SilentlyContinue }
    else { $env:GAM13_V2_TRACE_PATH = $previousTrace }
}
