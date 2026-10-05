$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PhysicsBenchmarkPaths.psm1') -Force

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sha = (git -C $projectRoot rev-parse --short=7 HEAD).Trim()
$physicsDirty = [bool](git -C $projectRoot status --porcelain -- Assets Packages ProjectSettings/DynamicsManager.asset ProjectSettings/TimeManager.asset ProjectSettings/ProjectVersion.txt)
$shaDirectory = if ($physicsDirty) { "$sha-dirty" } else { $sha }
$benchRoot = Join-Path (Join-Path $projectRoot 'Artifacts\Benchmarks\Physics') $shaDirectory
$runToken = '20261005-012345-0123456789ab'
$contract = Get-PhysicsBenchmarkPathContract
$labels = @(
    'baseline',
    ('verbose-' + ('x' * 1024)),
    'label with spaces',
    'punctuation <>:"/\|?* and trailing. ',
    ('same-prefix-' + ('x' * 1024) + 'first'),
    ('same-prefix-' + ('x' * 1024) + 'second')
)
$components = [Collections.Generic.List[string]]::new()
$longestPath = ''
$invalidCharacters = [IO.Path]::GetInvalidFileNameChars()

foreach ($label in $labels) {
    $component = Get-PhysicsBenchmarkRunPathComponent -RunToken $runToken -Label $label
    if ($component.Length -gt $contract.MaxRunPathComponentLength) { throw "Component too long: $($component.Length)" }
    if ($component.IndexOfAny($invalidCharacters) -ge 0) { throw "Component contains an invalid filename character: $component" }
    if ($component.EndsWith('.') -or $component.EndsWith(' ')) { throw "Component has a trailing dot or space: $component" }

    $rawDirectory = Join-Path (Join-Path $benchRoot 'runs') (Join-Path $component 'raw')
    $path = Assert-PhysicsBenchmarkPathBudget -RawDirectory $rawDirectory
    if ($path.Length -gt $longestPath.Length) { $longestPath = $path }
    $components.Add($component)
}

if (-not $components[0].Contains('baseline')) { throw 'Short labels should remain readable.' }
$similarPrefixOffset = $runToken.Length + 1
$similarPrefixA = $components[4].Substring($similarPrefixOffset, 8)
$similarPrefixB = $components[5].Substring($similarPrefixOffset, 8)
if ($similarPrefixA -ne $similarPrefixB) { throw 'The regression labels must share the sanitized prefix.' }
if ($components[4] -eq $components[5]) { throw 'Similar long labels collided.' }
if ($components[1] -ne (Get-PhysicsBenchmarkRunPathComponent -RunToken $runToken -Label $labels[1])) {
    throw 'The same run context and label did not produce a deterministic component.'
}

"PATH_CONTRACT_PASS labels=$($labels.Count) max_absolute_path=$($longestPath.Length)/$($contract.SafeWindowsPathLength) path=$longestPath"
