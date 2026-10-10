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

.PARAMETER SkipBuild
    Do not build or upload a prebuilt binary; publish the notes only.

.EXAMPLE
    ./publish-release.ps1
    ./publish-release.ps1 -Version 1.53 -NotesFile .\notes-1.53.md
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$NotesFile = '',
    [switch]$SkipBuild
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
# The title must begin with the version. GitHubUpdater.GetVersionNumber parses the first
# space-delimited token of the *title* and requires a leading "v", and the update dialog displays the
# title, so "Kronos 1.53" reads better than "v1.53" but silently disables the update check: the parse
# returns 0, which compares below every real version, so the app reports itself as up to date forever
# with nothing logged. GitHubRelease now falls back to the tag, but the title should still be right.
& $gh release create $tag --title "v$Version" --notes-file $NotesFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "Release creation failed. The tag was pushed, so delete it on GitHub before retrying," -ForegroundColor Yellow
    Write-Host "otherwise the retry will fail on 'tag already exists'." -ForegroundColor Yellow
    exit 1
}

if (-not $SkipBuild) {
    Write-Host "Building the Windows portable artifact..." -ForegroundColor Yellow
    $outputDir = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1') `
        -Target Windows -LicenseKey $env:IMAGESHARP_LICENSE_KEY | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { Fail "The Windows build failed; the release exists without an artifact." }
    $outputDir = "$outputDir".Trim()
    if (-not (Test-Path (Join-Path $outputDir 'Kronos.exe'))) {
        Fail "The build reported success but $outputDir has no Kronos.exe."
    }

    # Named to match what the Linux updater's asset matching expects for a Windows portable build,
    # even though the Windows GUI's own auto-update only looks for an -installer.exe and will skip
    # this. That is deliberate: the GUI must not offer to auto-install a portable zip over itself.
    $artifactName = "Kronos-$Version.0-portable.zip"
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("kronos-release-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        Copy-Item (Join-Path $outputDir '*') $staging
        $artifact = Join-Path $staging $artifactName
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $artifact -CompressionLevel Optimal

        Write-Host "Uploading $artifactName..." -ForegroundColor Yellow
        & $gh release upload $tag $artifact --clobber
        if ($LASTEXITCODE -ne 0) {
            Fail "The artifact upload failed. The release and tag exist without it; re-run with -SkipBuild once the build works."
        }
    }
    finally {
        Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "=== Published ===" -ForegroundColor Green
& $gh release view $tag --json url --jq .url