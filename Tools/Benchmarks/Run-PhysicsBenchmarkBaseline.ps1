[CmdletBinding()]
param(
    [string[]]$SquatLoadsKg = @('25', '60', '140', '170', '300'),
    [string[]]$RepeatLoadsKg = @('25', '140'),
    [int]$RepeatCount = 2,
    [string]$Label = 'baseline'
)

# Full V1 benchmark at the current commit: every tier, sequentially, into
# Artifacts/Benchmarks/Physics/<sha>/ (one Unity process per invocation).
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Run-PhysicsBenchmark.ps1'
& $runner -Tier Isolated -Label "$Label-isolated" -NoCompare
& $runner -Tier Athlete -Label "$Label-athlete" -NoCompare -TimeoutMinutes 120
$single = @($SquatLoadsKg | Where-Object { $RepeatLoadsKg -notcontains $_ })
if ($single.Count -gt 0) { & $runner -Tier Squat -LoadsKg $single -Label "$Label-squat" -NoCompare }
& $runner -Tier Squat -LoadsKg $RepeatLoadsKg -Repeats $RepeatCount -Label "$Label-squat-repeat"
