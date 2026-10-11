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
    [switch]$SkipBuild,
    [switch]$ReplaceArtifactOnly
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

# Runs a native command and returns its exit code, without its output.
#
# Needed because $ErrorActionPreference = 'Stop' turns a native command that writes to stderr into a
# terminating error. Two of the checks here depend on that stderr: `gh auth status` says it is not
# logged in that way, and `git rev-parse --verify` reports a missing ref that way. Both are ordinary
# answers here rather than failures, so the preference is lowered for the call and restored after.
function Test-NativeCommand {
    param([scriptblock]$Command)

    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Command *> $null
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

# Runs a native command, letting its output through, and returns its exit code.
#
# Same stderr problem as Test-NativeCommand, but for the commands that publish the release rather than
# probe the environment. `git push` and `git tag` always write progress to stderr - "To https://...",
# "* [new tag]", "Everything up-to-date" - and under $ErrorActionPreference = 'Stop' PowerShell turns
# that into a NativeCommandError. The work still completed, so the script ran to the end and printed
# "=== Published ===" while the process exited 1. A wrapper checking $LASTEXITCODE would read a
# successful release as a failure, and a genuine failure later on would be the second error in the
# output rather than the only one. Progress on stderr is not an error; the exit code is the answer.
function Invoke-NativeCommand {
    param([scriptblock]$Command)

    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        # Out-Host, not bare output. A command that writes to stdout would otherwise put its output into
        # this function's return value alongside the exit code, and the caller would get an array. That
        # is not hypothetical: `gh release create` prints the release URL, so the function returned
        # @('https://...', 0), the caller's `-ne 0` comparison was true because the array is non-empty,
        # and a release that had been created successfully was reported as failed. Out-Host keeps the
        # text on screen where the user can see it and out of the value.
        & $Command | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

if ((Test-NativeCommand { & $gh auth status }) -ne 0) {
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

# Rebuilding an artifact that was already uploaded to this release. The tag exists by definition here, so
# the guard below and the whole tag-and-release sequence are skipped, and only the build and upload run.
#
# This exists because a release can be published correctly and still ship a wrong artifact. v1.55 was
# published with an empty build tag in its binary, found afterwards, and had to be replaced without
# deleting the release and its notes and starting over. -SkipBuild could not do it, because the tag check
# is not about the build.
if ($ReplaceArtifactOnly) {
    if ((Test-NativeCommand { & git rev-parse --verify "refs/tags/$tag" }) -ne 0) {
        Fail "Tag $tag does not exist locally. There is no release to replace an artifact on; publish normally."
    }

    Write-Host "=== Rebuilding the $tag artifact ===" -ForegroundColor Green
    Write-Host "The tag, release and notes are left alone." -ForegroundColor DarkGray
}
else {
    if ((Test-NativeCommand { & git rev-parse --verify "refs/tags/$tag" }) -eq 0) {
        Fail "Tag $tag already exists locally. Move or delete it first, or use -ReplaceArtifactOnly to rebuild just the artifact."
    }
}

Write-Host "=== Publishing Kronos $Version ===" -ForegroundColor Green
Write-Host "Notes: $NotesFile" -ForegroundColor Cyan
Write-Host ""

if ($ReplaceArtifactOnly) {
    # Fall through to the build and upload below, skipping tag creation, push and release creation.
}
else {
Write-Host "Creating tag $tag..." -ForegroundColor Yellow
if ((Invoke-NativeCommand { & git tag -a $tag -m "Kronos $Version" }) -ne 0) { Fail "Failed to create tag $tag." }

Write-Host "Pushing tag..." -ForegroundColor Yellow
if ((Invoke-NativeCommand { & git push origin $tag }) -ne 0) {
    Write-Host "Tag push failed. The local tag $tag still exists; delete it with: git tag -d $tag" -ForegroundColor Yellow
    exit 1
}

Write-Host "Creating GitHub Release..." -ForegroundColor Yellow
# The title must begin with the version. GitHubUpdater.GetVersionNumber parses the first
# space-delimited token of the *title* and requires a leading "v", and the update dialog displays the
# title, so "Kronos 1.53" reads better than "v1.53" but silently disables the update check: the parse
# returns 0, which compares below every real version, so the app reports itself as up to date forever
# with nothing logged. GitHubRelease now falls back to the tag, but the title should still be right.
if ((Invoke-NativeCommand { & $gh release create $tag --title "v$Version" --notes-file $NotesFile }) -ne 0) {
    Write-Host "Release creation failed. The tag was pushed, so delete it on GitHub before retrying," -ForegroundColor Yellow
    Write-Host "otherwise the retry will fail on 'tag already exists'." -ForegroundColor Yellow
    exit 1
}
}

if (-not $SkipBuild) {
    Write-Host "Building the Windows portable artifact..." -ForegroundColor Yellow
    # Release_Portable, not the -Configuration this script was given. build.ps1 decides this itself.
    # The artifact first published crashed on startup; build.ps1 now smoke tests the binary it is
    # about to hand over, and fails if it exits immediately or if Kronos.pri is missing. Both of those
    # were true of the build that shipped, and neither appeared anywhere in the build output.
    $buildOutput = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1') `
        -Target Windows -LicenseKey $env:IMAGESHARP_LICENSE_KEY
    if ($LASTEXITCODE -ne 0) { Fail "The Windows build failed; the release exists without an artifact." }

    # Parsed from the tagged line build.ps1 writes, not from the last line of its output. The summary
    # it prints ends with "Note: Copy the entire output folder to target machine to run.", and taking
    # that as the path produced an attempt to join a drive called "Note".
    $outputDir = ($buildOutput |
        Where-Object { $_ -like 'KRONOS_OUTPUT_DIR=*' } |
        Select-Object -First 1) -replace '^KRONOS_OUTPUT_DIR=', ''

    if (-not $outputDir) { Fail "The build did not report an output directory. Nothing was uploaded." }
    if (-not (Test-Path (Join-Path $outputDir 'Kronos.exe'))) {
        Fail "The build reported success but $outputDir has no Kronos.exe."
    }

    $artifactName = "Kronos-$Version.0-portable.zip"
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("kronos-release-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        # -Recurse is required and its absence is silent.
        #
        # Copy-Item without it copies the *directories* but none of their contents, leaving empty
        # folders behind. Compress-Archive then omits empty directories entirely, so the archive is
        # quietly short of everything that lived in them. That is exactly what shipped as v1.53: 97
        # files gone - the whole of Assets (including the app icon the title bar loads), Translations,
        # and StoredData - and the only symptom anyone saw was a missing logo.
        Copy-Item (Join-Path $outputDir '*') $staging -Recurse

        # Verified rather than assumed. A partial archive is not a build failure, so nothing upstream
        # would ever report it, and the only place it shows up is the user's desktop.
        $sourceCount = @(Get-ChildItem -LiteralPath $outputDir -Recurse -File).Count
        $stagedCount = @(Get-ChildItem -LiteralPath $staging -Recurse -File).Count
        if ($sourceCount -ne $stagedCount) {
            Fail "Staging copied $stagedCount of $sourceCount files from $outputDir. Refusing to upload a partial archive."
        }

        # Named explicitly because these are what go missing, and the logo in particular is the one
        # thing every user looks at.
        foreach ($required in @('Kronos.exe', 'Kronos.pri', 'Assets\icon_256.png', 'Assets\icon.ico')) {
            if (-not (Test-Path (Join-Path $staging $required))) {
                Fail "The archive is missing $required. Refusing to upload it."
            }
        }

        $artifact = Join-Path $staging $artifactName
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $artifact -CompressionLevel Optimal

        Write-Host "Uploading $artifactName..." -ForegroundColor Yellow
        if ((Invoke-NativeCommand { & $gh release upload $tag $artifact --clobber }) -ne 0) {
            Fail "The artifact upload failed. The release and tag exist without it; re-run with -SkipBuild once the build works."
        }
    }
    finally {
        Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "=== Published ===" -ForegroundColor Green

# Printed as output rather than returned as a value, so the script's result is the exit code and not a
# string a caller might accidentally treat as one. Without the explicit exit below, a PowerShell script
# that ends on a successful command still exits 0, but one that ends after a suppressed error does not,
# and this script had already proved it could reach here having exited 1.
#
# The URL goes in a hashtable rather than a local: Invoke-NativeCommand returns the exit code, and a
# plain assignment inside the scriptblock would be made in a scope that discards it. Setting a property
# mutates the one hashtable in every scope.
$published = @{ Url = '' }
$viewExit = Invoke-NativeCommand { $published.Url = (& $gh release view $tag --json url --jq .url) }
if ($viewExit -ne 0) { exit 1 }
Write-Host $published.Url
exit 0