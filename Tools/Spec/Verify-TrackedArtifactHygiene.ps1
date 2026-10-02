[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$tracked = @(git -C $RepositoryRoot ls-files -- Artifacts)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate tracked Artifacts files.'
}

$maximumBytes = 131072
$violations = [Collections.Generic.List[string]]::new()
foreach ($path in $tracked) {
    $normalized = $path -replace '\\', '/'
    $extension = [IO.Path]::GetExtension($normalized).ToLowerInvariant()
    if ($normalized -match '(?i)^Artifacts/.*/(raw|runs|sweeps|run-[^/]*)/') {
        $violations.Add("generated run directory: $normalized")
        continue
    }
    if ($extension -in @('.csv', '.xml', '.log', '.png', '.gz', '.bin', '.dump', '.trace')) {
        $violations.Add("generated evidence extension $extension`: $normalized")
        continue
    }

    $file = Join-Path $RepositoryRoot ($normalized -replace '/', '\')
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        $violations.Add("tracked artifact is missing from the worktree: $normalized")
        continue
    }
    if ((Get-Item -LiteralPath $file).Length -gt $maximumBytes) {
        $violations.Add("tracked artifact exceeds $maximumBytes bytes: $normalized")
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error "ARTIFACT_HYGIENE_ERROR=$_" }
    throw "Tracked artifact hygiene failed with $($violations.Count) violation(s)."
}

Write-Output "TRACKED_ARTIFACTS=$($tracked.Count)"
Write-Output "MAX_TRACKED_ARTIFACT_BYTES=$maximumBytes"
Write-Output 'ARTIFACT_HYGIENE=PASS'
