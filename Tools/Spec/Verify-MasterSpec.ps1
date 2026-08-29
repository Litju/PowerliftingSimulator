[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
    $RepositoryRoot = (Resolve-Path (Join-Path $scriptDirectory '..\..')).Path
}

function Stop-WithError {
    param([string]$Reason)

    Write-Error "MASTER_SPEC_ERROR=$Reason"
    exit 1
}

function Normalize-ManifestPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        Stop-WithError 'empty manifest path'
    }

    $normalized = $Path -replace '\\', '/'
    if ($normalized.StartsWith('/') -or $normalized -match '(^|/)\.\.(/|$)') {
        Stop-WithError "unsafe manifest path: $Path"
    }

    return $normalized
}

try {
    $specRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'docs\master-spec\POWERLIFTING_SIMULATOR_MASTER_SPEC_V1'))
    if (-not (Test-Path -LiteralPath $specRoot -PathType Container)) {
        Stop-WithError "master spec directory missing: $specRoot"
    }

    $manifestPath = Join-Path $specRoot 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Stop-WithError "manifest missing: $manifestPath"
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        Stop-WithError "manifest is not valid JSON: $($_.Exception.Message)"
    }

    $canonicalPaths = @{}
    $artifacts = @($manifest.artifacts)
    if ($artifacts.Count -eq 0) {
        Stop-WithError 'manifest has no artifacts'
    }

    foreach ($artifact in $artifacts) {
        $relative = Normalize-ManifestPath $artifact.path
        if ($canonicalPaths.ContainsKey($relative)) {
            Stop-WithError "duplicate canonical path: $relative"
        }

        $canonicalPaths[$relative] = $true
        $filePath = Join-Path $specRoot ($relative -replace '/', '\')
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            Stop-WithError "canonical file missing: $relative"
        }

        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $expectedHash = ([string]$artifact.sha256).ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            Stop-WithError "hash mismatch: $relative expected $expectedHash actual $actualHash"
        }
    }

    $manifestExpected = 'manifest.json'
    $expectedFiles = @{}
    foreach ($path in $canonicalPaths.Keys) {
        $expectedFiles[$path] = $true
    }
    $expectedFiles[$manifestExpected] = $true

    $actualFiles = @(
        Get-ChildItem -LiteralPath $specRoot -Recurse -File | ForEach-Object {
            (($_.FullName.Substring($specRoot.Length)) -replace '^[\\/]+', '') -replace '\\', '/'
        }
    )

    $unexpected = @($actualFiles | Where-Object { -not $expectedFiles.ContainsKey($_) })
    if ($unexpected.Count -gt 0) {
        Stop-WithError "unexpected canonical files: $($unexpected -join ', ')"
    }

    if ($actualFiles.Count -ne $expectedFiles.Count) {
        Stop-WithError "file count mismatch expected $($expectedFiles.Count) actual $($actualFiles.Count)"
    }

    foreach ($artifact in $artifacts) {
        foreach ($dependency in @($artifact.dependencies)) {
            $dependencyPath = Normalize-ManifestPath $dependency
            if (-not $canonicalPaths.ContainsKey($dependencyPath)) {
                Stop-WithError "unresolved dependency: $($artifact.path) -> $dependency"
            }
        }
    }

    foreach ($edge in @($manifest.dependency_edges)) {
        $from = Normalize-ManifestPath $edge.from
        $to = Normalize-ManifestPath $edge.to
        if (-not $canonicalPaths.ContainsKey($from) -or -not $canonicalPaths.ContainsKey($to)) {
            Stop-WithError "unresolved dependency edge: $($edge.from) -> $($edge.to)"
        }
    }

    Write-Output "MASTER_SPEC_FILES=$($actualFiles.Count)"
    Write-Output 'HASHES=PASS'
    Write-Output 'DEPENDENCIES=PASS'
    Write-Output 'STATUS=PASS'
    exit 0
}
catch {
    Stop-WithError $_.Exception.Message
}
