param(
    [string]$Version = "0.1.0-alpha"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$DistDir = Join-Path $RepoRoot "dist"
$StageDir = Join-Path $DistDir "UnrealAgent"
$ZipPath = Join-Path $DistDir "UnrealAgent-$Version.zip"

if (Test-Path $StageDir) {
    Remove-Item -LiteralPath $StageDir -Recurse -Force
}

New-Item -ItemType Directory -Force $StageDir | Out-Null

$ExcludedDirectories = @(
    ".git",
    ".github",
    "dist",
    "Binaries",
    "Build",
    "DerivedDataCache",
    "Intermediate",
    "Saved",
    ".unrealagent",
    ".omc"
)

$ExcludedDirectoryNames = @("bin", "obj")
$ExcludedFiles = @("*.log", "*.tmp", "tools_cache.json")

Get-ChildItem -LiteralPath $RepoRoot -Force | ForEach-Object {
    if ($ExcludedDirectories -contains $_.Name) {
        return
    }

    $Destination = Join-Path $StageDir $_.Name
    Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
}

foreach ($DirectoryName in $ExcludedDirectoryNames) {
    Get-ChildItem -LiteralPath $StageDir -Recurse -Directory -Force |
        Where-Object { $_.Name -eq $DirectoryName } |
        Remove-Item -Recurse -Force
}

foreach ($Pattern in $ExcludedFiles) {
    Get-ChildItem -LiteralPath $StageDir -Recurse -File -Force -Filter $Pattern |
        Remove-Item -Force
}

if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

Compress-Archive -LiteralPath $StageDir -DestinationPath $ZipPath -Force
Write-Host "Created $ZipPath"
