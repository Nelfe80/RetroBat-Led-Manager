param(
    [string]$Label = "snapshot",
    [string[]]$Files = @(
        "Directory.Build.props",
        "LedManager.sln",
        "docs\LOGS.md",
        "docs\00_POINT_PROJET_WORKFLOW.md",
        "docs\01_DEVOPS_VERSIONING.md",
        "LedManager.ini",
        "PicoCommandSender.ini",
        "default.mem.effects.json"
    ),
    [string[]]$Tests = @(),
    [string]$Notes = "",
    [switch]$Release
)

$ErrorActionPreference = "Stop"

function ConvertTo-NormalizedList {
    param(
        [string[]]$Values,
        [switch]$SplitComma,
        [switch]$SplitTestCommands
    )

    $result = @()
    foreach ($entry in $Values) {
        if ($null -eq $entry) {
            continue
        }

        $parts = @($entry)
        if ($SplitComma) {
            $parts = $entry -split ","
        } elseif ($SplitTestCommands) {
            $parts = $entry -split '(?i),(?=\s*(?:dotnet|powershell|pwsh|cmd|py|python|Get-|Set-|New-|Copy-|Move-|Remove-|Stop-|Start-|\.\\|src\\|tests\\|[A-Za-z]:\\))'
        }

        foreach ($part in $parts) {
            $candidate = $part.Trim()
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                $result += $candidate
            }
        }
    }

    return $result
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionRoot = Join-Path $repoRoot ".versioning"
$commitsRoot = Join-Path $versionRoot "commits"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeLabel = ($Label -replace '[^A-Za-z0-9._-]', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($safeLabel)) {
    $safeLabel = "snapshot"
}

$commitId = "$timestamp-$safeLabel"
$commitDir = Join-Path $commitsRoot $commitId

New-Item -ItemType Directory -Force -Path $commitDir | Out-Null

$normalizedFiles = ConvertTo-NormalizedList -Values $Files -SplitComma
if ($normalizedFiles.Count -eq 0) {
    $normalizedFiles = @(
        "Directory.Build.props",
        "LedManager.sln",
        "docs\LOGS.md",
        "docs\00_POINT_PROJET_WORKFLOW.md",
        "docs\01_DEVOPS_VERSIONING.md"
    )
}

$normalizedTests = ConvertTo-NormalizedList -Values $Tests -SplitTestCommands
$captured = @()
$missing = @()

foreach ($file in $normalizedFiles) {
    if ([string]::IsNullOrWhiteSpace($file)) {
        continue
    }

    $relativePath = $file.Trim()
    $sourcePath = Join-Path $repoRoot $relativePath

    if (-not (Test-Path -LiteralPath $sourcePath)) {
        Write-Warning "Fichier introuvable ignore: $relativePath"
        $missing += $relativePath
        continue
    }

    $destinationPath = Join-Path $commitDir $relativePath
    $destinationDir = Split-Path -Parent $destinationPath
    if ($destinationDir) {
        New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
    }

    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force

    $item = Get-Item -LiteralPath $sourcePath
    $hash = Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256
    $captured += [ordered]@{
        path = $relativePath
        bytes = $item.Length
        last_write_time = $item.LastWriteTime.ToString("o")
        sha256 = $hash.Hash
    }
}

$manifest = [ordered]@{
    manifest_version = 2
    commit = $commitId
    created_at = (Get-Date).ToString("o")
    label = $Label
    root = $repoRoot
    release = [bool]$Release
    notes = $Notes
    tests = $normalizedTests
    missing_files = $missing
    files = $captured
}

$manifestPath = Join-Path $commitDir "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Output "Snapshot cree: $commitDir"
Write-Output "Fichiers captures: $($captured.Count)"
