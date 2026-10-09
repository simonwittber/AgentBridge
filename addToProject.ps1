<#
.SYNOPSIS
Links the AgentBridge package into a Unity project's Packages folder without committing it.

.DESCRIPTION
Creates a link at <project>/Packages/<package name> that points at this repo's package folder.
Unity loads it as an embedded package, so manifest.json is not touched.
The link path is added to the repo's .git/info/exclude, which is a local-only ignore list.
A symbolic link is tried first; if that fails (no Developer Mode or admin rights), a directory junction is used.

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

$linkPath = Join-Path $packagesDir $packageName
if (Test-Path $linkPath) {
    Write-Host "Already present: $linkPath"
} else {
    try {
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $packageSource | Out-Null
        Write-Host "Created symbolic link: $linkPath -> $packageSource"
    } catch {
        New-Item -ItemType Junction -Path $linkPath -Target $packageSource | Out-Null
        Write-Host "Symbolic link not allowed, created junction instead: $linkPath -> $packageSource"
    }
}

# Find the repo that owns the project; the project may sit in a subfolder of the repo, or in a worktree.
$gitRoot = git -C $projectRoot rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "'$projectRoot' is not inside a git repo, skipping .git/info/exclude."
    return
}
$excludeFile = git -C $projectRoot rev-parse --path-format=absolute --git-path info/exclude

# Exclude patterns are relative to the repo root and use forward slashes; a leading slash anchors the pattern there.
$relativeLink = [IO.Path]::GetRelativePath((Resolve-Path $gitRoot).Path, $linkPath).Replace('\', '/')
$pattern = "/$relativeLink"

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
