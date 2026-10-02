param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('A0','A1','A2','B0','B1','B2','B3','B4')]
    [string]$Arm,
    [string]$RunId = 'run-20260926'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$unity = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe'
$tests = @{
    A0 = 'GAM13_B1_A0_25KG_HELD_BOTTOM_FRESH_PROCESS'
    A1 = 'GAM13_B1_A1_60KG_HELD_BOTTOM_FRESH_PROCESS'
    A2 = 'GAM13_B1_A2_60KG_DYNAMIC_FRESH_PROCESS'
    B0 = 'GAM13_B1_B0_60KG_PRODUCTION_START_FRESH_PROCESS'
    B1 = 'GAM13_B1_B1_140KG_PRODUCTION_START_FRESH_PROCESS'
    B2 = 'GAM13_B1_B2_140KG_S0_START_FRESH_PROCESS'
    B3 = 'GAM13_B1_B3_140KG_B0_START_FRESH_PROCESS'
    B4 = 'GAM13_B1_B4_140KG_S0_B0_START_FRESH_PROCESS'
}

if (-not (Test-Path -LiteralPath $unity)) { throw "Required Unity 6000.3.22f1 executable not found: $unity" }
$armDirectory = Join-Path $projectRoot "Artifacts\Measurements\GAM-13\phase-b1\$RunId\$Arm"
if (Test-Path -LiteralPath $armDirectory) { throw "Refusing to reuse an arm directory: $armDirectory" }
New-Item -ItemType Directory -Path $armDirectory | Out-Null
$xmlPath = Join-Path $armDirectory 'test-results.xml'
$logPath = Join-Path $armDirectory 'unity.log'
$receiptPath = Join-Path $armDirectory 'runner-receipt.md'
$testName = $tests[$Arm]
$arguments = @(
    '-batchmode', '-nographics', '-runTests', '-testPlatform', 'playmode',
    '-testFilter', $testName, '-testResults', $xmlPath, '-logFile', $logPath,
    '-projectPath', $projectRoot
)
$oldOutput = $env:GAM13_B1_OUTPUT_DIR
$env:GAM13_B1_OUTPUT_DIR = $armDirectory
try {
    $process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -WindowStyle Hidden
} finally {
    $env:GAM13_B1_OUTPUT_DIR = $oldOutput
}

$deadline = [DateTime]::UtcNow.AddMinutes(30)
$testRun = $null
$exited = $false
$stoppedAfterXml = $false
while ([DateTime]::UtcNow -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) { $exited = $true; break }
    if (Test-Path -LiteralPath $xmlPath) {
        try {
            [xml]$candidateXml = Get-Content -LiteralPath $xmlPath -Raw
            $testRun = $candidateXml.SelectSingleNode('/test-run')
        } catch {
            $testRun = $null
        }
        if ($null -ne $testRun) { break }
    }
    Start-Sleep -Seconds 2
}

if ($null -eq $testRun -and (Test-Path -LiteralPath $xmlPath)) {
    try {
        [xml]$candidateXml = Get-Content -LiteralPath $xmlPath -Raw
        $testRun = $candidateXml.SelectSingleNode('/test-run')
    } catch {
        $testRun = $null
    }
}
if ($null -eq $testRun) {
    if (-not $process.HasExited) {
        throw "Unity PID $($process.Id) did not finish and has not written complete test XML. Process left running for inspection."
    }
    throw "Unity PID $($process.Id) exited without complete Test Framework XML."
}
if (-not $process.HasExited) {
    Start-Sleep -Seconds 20
    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $stoppedAfterXml = $true
    }
}
$result = $testRun.GetAttribute('result')
$exitCode = if ($stoppedAfterXml) { 'stopped-after-fresh-xml' } else { $process.ExitCode }
$version = (Get-Item -LiteralPath $unity).VersionInfo.ProductVersion
$receipt = @(
    '# GAM-13 Phase B1 fresh-process receipt'
    ''
    "ARM=$Arm"
    "TEST_FILTER=$testName"
    "UNITY_EXECUTABLE=$unity"
    "UNITY_VERSION=$version"
    "UNITY_PID=$($process.Id)"
    "TEST_RESULT=$result"
    "PROCESS_EXIT=$exitCode"
    "XML=test-results.xml"
    "LOG=unity.log"
    "BASELINE=7f29d73424e37254e3b924d50d01cda46b0aa0ae"
    "UTC_FINISHED=$([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ssZ'))"
)
Set-Content -LiteralPath $receiptPath -Value $receipt -Encoding UTF8
Write-Output ($receipt -join [Environment]::NewLine)
if ($result -ne 'Passed') { throw "GAM-13 B1 arm $Arm did not pass its Unity test: $result" }
