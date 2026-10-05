# Keep 20 characters below classic MAX_PATH; the preflight includes the longest B13 oracle file.
$script:SafeWindowsPathLength = 240
$script:MaxRunPathComponentLength = 50
# B13's settled no-balance oracle export is the current longest raw evidence filename.
$script:LongestRawRelativePath = Join-Path 'oracle' 'B13_300kg_s1.00_no_balance_feedback.settled.json'

function Get-PhysicsBenchmarkRunPathComponent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunToken,
        [AllowEmptyString()]
        [string]$Label = ''
    )

    if ([string]::IsNullOrEmpty($Label)) { return $RunToken }

    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($Label))
    }
    finally {
        $hasher.Dispose()
    }
    $digest = [BitConverter]::ToString($hashBytes).Replace('-', '').Substring(0, 12).ToLowerInvariant()

    $prefix = [regex]::Replace($Label, '[<>:"/\\|?*\x00-\x1f\s]+', '-')
    $trimChars = [char[]]@(' ', '.', '-')
    $prefix = $prefix.TrimEnd($trimChars)
    if ([string]::IsNullOrWhiteSpace($prefix)) { $prefix = 'label' }
    if ($prefix.Length -gt 8) { $prefix = $prefix.Substring(0, 8) }
    $prefix = $prefix.TrimEnd($trimChars)
    if ([string]::IsNullOrWhiteSpace($prefix)) { $prefix = 'label' }

    $component = "$RunToken-$prefix-$digest"
    if ($component.Length -gt $script:MaxRunPathComponentLength) {
        throw "Benchmark run path component exceeded $script:MaxRunPathComponentLength characters."
    }
    return $component
}

function Assert-PhysicsBenchmarkPathBudget {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RawDirectory)

    $rawRoot = [IO.Path]::GetFullPath($RawDirectory)
    $longestPath = [IO.Path]::GetFullPath((Join-Path $rawRoot $script:LongestRawRelativePath))
    if ($longestPath.Length -gt $script:SafeWindowsPathLength) {
        throw "Benchmark evidence path is $($longestPath.Length) characters; the safe Windows budget is $script:SafeWindowsPathLength`: $longestPath"
    }
    return $longestPath
}

function Get-PhysicsBenchmarkPathContract {
    [pscustomobject]@{
        SafeWindowsPathLength = $script:SafeWindowsPathLength
        MaxRunPathComponentLength = $script:MaxRunPathComponentLength
        LongestRawRelativePath = $script:LongestRawRelativePath
    }
}

Export-ModuleMember -Function Get-PhysicsBenchmarkRunPathComponent, Assert-PhysicsBenchmarkPathBudget, Get-PhysicsBenchmarkPathContract
