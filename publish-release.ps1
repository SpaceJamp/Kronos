#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes a Kronos release: tags the commit, pushes the tag, and creates a GitHub Release
    whose body is RELEASE_BODY.md.

.DESCRIPTION
    Requires the GitHub CLI (gh) to be installed and authenticated:
        gh auth login

    Nothing is published if any step fails. The tag is only created after the working tree is
    confirmed clean and the version in src/Kronos.csproj is confirmed to match, because a tag
    that disagrees with the csproj makes the in-app updater compare against the wrong number.

.PARAMETER Version
    Release version, without a leading "v". Defaults to the value in src/Kronos.csproj.

.PARAMETER NotesFile
    Markdown file used as the release body. Defaults to RELEASE_BODY.md in the repo root.

.EXAMPLE
    ./publish-release.ps1
    ./publish-release.ps1 -Version 1.53 -NotesFile .\notes-1.53.md
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$NotesFile = ''
)

$ErrorActionPreference = 'Stop'

function Fail($message) {
    Write-Host "Error: $message" -ForegroundColor Red
    exit 1
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $candidate = Join-Path ${env:ProgramFiles} 'GitHub CLI\gh.exe'
    if (Test-Path $candidate) { $gh = $candidate } else { Fail "GitHub CLI not found. Install it with: winget install GitHub.cli" }
}

& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Fail "Not logged in to GitHub. Run: gh auth login"
}

# Read the version out of the csproj, so the default can never disagree with the source of truth.
$csproj = Join-Path $PSScriptRoot 'src\Kronos.csproj'
$match = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $match) { Fail "No <Version> element found in src/Kronos.csproj." }
$csprojVersion = $match.Matches[0].Groups[1].Value

if (-not $Version) { $Version = $csprojVersion }

if ($Version -ne $csprojVersion) {
    Fail "Version mismatch: requested $Version but src/Kronos.csproj says $csprojVersion. They must match or the updater misreports."
}

$status = & git status --porcelain
if ($status) {
    Fail "Working tree is not clean. Commit or stash first:`n$($status -join "`n")"
}

if (-not $NotesFile) { $NotesFile = Join-Path $PSScriptRoot 'RELEASE_BODY.md' }
if (-not (Test-Path $NotesFile)) { Fail "Release notes file not found: $NotesFile" }

$tag = "v$Version"

& git rev-parse --verify "refs/tags/$tag" *> $null
if ($LASTEXITCODE -eq 0) { Fail "Tag $tag already exists locally. Move or delete it first." }

Write-Host "=== Publishing Kronos $Version ===" -ForegroundColor Green
Write-Host "Notes: $NotesFile" -ForegroundColor Cyan
Write-Host ""

Write-Host "Creating tag $tag..." -ForegroundColor Yellow
& git tag -a $tag -m "Kronos $Version"
if ($LASTEXITCODE -ne 0) { Fail "Failed to create tag $tag." }

Write-Host "Pushing tag..." -ForegroundColor Yellow
& git push origin $tag
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tag push failed. The local tag $tag still exists; delete it with: git tag -d $tag" -ForegroundColor Yellow
    exit 1
}

Write-Host "Creating GitHub Release..." -ForegroundColor Yellow
& $gh release create $tag --title "Kronos $Version" --notes-file $NotesFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "Release creation failed. The tag was pushed, so delete it on GitHub before retrying," -ForegroundColor Yellow
    Write-Host "otherwise the retry will fail on 'tag already exists'." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "=== Published ===" -ForegroundColor Green
& $gh release view $tag --json url --jq .url