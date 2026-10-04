<#
.SYNOPSIS
Releases a new version: tags the current commit and pushes it. GitHub Actions builds the
release, and installed copies of the app offer to update.

.EXAMPLE
./scripts/release.ps1          # next patch version, e.g. 0.1.3 -> 0.1.4

.EXAMPLE
./scripts/release.ps1 0.2.0
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (git status --porcelain) {
    throw 'Commit or stash your changes first; a release is built from what is pushed.'
}

if (-not $Version) {
    $latest = git tag --list 'v*' --sort=-v:refname | Select-Object -First 1
    if ($latest) {
        $parts = $latest.TrimStart('v').Split('.')
        $Version = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    }
    else {
        $Version = '0.1.0'
    }
}

git push origin HEAD
git tag "v$Version"
git push origin "v$Version"

Write-Host "Version $Version is building: https://github.com/sliarsgard/timekeeper/actions"
