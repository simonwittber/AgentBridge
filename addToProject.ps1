<#
.SYNOPSIS
Copies the AgentBridge package into a Unity project's Packages folder without committing it.

.DESCRIPTION
Copies this repo's package folder to <project>/Packages/<package name> and keeps it
up to date on each run (a mirror). Unity loads it as an embedded package, so
manifest.json is not touched. The copy path is added to the repo's .git/info/exclude,
which is a local-only ignore list.
If a previous version of this script left a symbolic link or junction at the target
path, it is removed (the link itself, not its contents) before the copy.
After changing something in the package source, re-run this script to resync it.

.PARAMETER ProjectPath
Root of the Unity project (the folder that contains Packages). Defaults to the current directory.

.EXAMPLE
cd ~/git/com.dffrnt.bt
~/git/AgentBridge/addToProject.ps1
#>
param(
    [string]$ProjectPath = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$packageSource = Join-Path $PSScriptRoot 'AgentBridge'
$packageName = (Get-Content (Join-Path $packageSource 'package.json') -Raw | ConvertFrom-Json).name

$projectRoot = (Resolve-Path $ProjectPath).Path
$packagesDir = Join-Path $projectRoot 'Packages'
if (-not (Test-Path $packagesDir -PathType Container)) {
    throw "No Packages folder found in '$projectRoot'. Run this from a Unity project root, or pass -ProjectPath."
}

$targetPath = Join-Path $packagesDir $packageName
$packageSourceFull = (Resolve-Path $packageSource).Path
# The target must not sit inside the package source itself. If the Unity project lives
# inside this repo (e.g. AgentBridge/Example~), Copy-Item would copy the source into its
# own subfolder, producing a nested copy of itself.
if ($targetPath.StartsWith($packageSourceFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to copy the package into itself: '$targetPath' is inside '$packageSourceFull'."
}
if (Test-Path $targetPath) {
    $existing = Get-Item $targetPath
    if ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        # Remove only the reparse point of an old symbolic link or junction; its contents stay untouched.
        Remove-Item $targetPath -Force
        Write-Host "Removed old link: $targetPath"
    } elseif ($existing.PSIsContainer) {
        Remove-Item $targetPath -Recurse -Force
        Write-Host "Removed old copy: $targetPath"
    } else {
        throw "'$targetPath' exists and is a file, so it cannot be replaced by the package. Remove it first."
    }
}
if (-not (Test-Path $targetPath)) {
    New-Item -ItemType Directory -Path $targetPath | Out-Null
}
Copy-Item -Path (Join-Path $packageSource '*') -Destination $targetPath -Recurse
Write-Host "Copied package to: $targetPath"

# Find the repo that owns the project; the project may sit in a subfolder of the repo, or in a worktree.
$gitRoot = git -C $projectRoot rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "'$projectRoot' is not inside a git repo, skipping .git/info/exclude."
    return
}
$excludeFile = git -C $projectRoot rev-parse --path-format=absolute --git-path info/exclude

# Exclude patterns are relative to the repo root and use forward slashes; a leading slash anchors the pattern there.
$relativePath = [IO.Path]::GetRelativePath((Resolve-Path $gitRoot).Path, $targetPath).Replace('\', '/')
$pattern = "/$relativePath"

$excludeDir = Split-Path $excludeFile -Parent
if (-not (Test-Path $excludeDir)) {
    New-Item -ItemType Directory -Path $excludeDir | Out-Null
}
$existing = if (Test-Path $excludeFile) { Get-Content $excludeFile } else { @() }
if ($existing -contains $pattern) {
    Write-Host "Already excluded: $pattern"
} else {
    $content = if (Test-Path $excludeFile) { Get-Content $excludeFile -Raw } else { '' }
    if ($content -and -not $content.EndsWith("`n")) {
        Add-Content -Path $excludeFile -Value ''
    }
    Add-Content -Path $excludeFile -Value $pattern
    Write-Host "Added '$pattern' to $excludeFile"
}
