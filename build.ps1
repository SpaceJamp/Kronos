#!/usr/bin/env pwsh
<# 
.SYNOPSIS
    Kronos Build Script (PowerShell)
    Builds Kronos for the current platform or cross-compiles for Linux

.DESCRIPTION
    This script builds Kronos, an Uno Platform app with one target framework (net10.0-desktop), for
    Windows (a self-contained portable folder that needs no Windows App Runtime) or Linux. The Linux
    build does not compile yet; see the README.
    Requires .NET 10 SDK installed.

.EXAMPLE
    .\build.ps1                           # Build for current platform
    .\build.ps1 -Configuration Debug      # Debug build
    .\build.ps1 -Target Linux             # Cross-compile Linux from Windows
    .\build.ps1 -Target Windows           # Build Windows (native)
    .\build.ps1 -Target All               # Build both Linux and Windows
    .\build.ps1 -Configuration Release -Target Linux -Runtime linux-x64
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [ValidateSet('Windows', 'Linux', 'All')]
    [string]$Target = '',
    
    [ValidateSet('linux-x64')]
    [string]$Runtime = 'linux-x64',
    
    [string]$LicenseKey = '',
    
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

# Reports a fatal problem and stops the build.
#
# This existed in publish-release.ps1 the whole time and was never copied here, so build.ps1 called
# Fail(...) at its most important line - the one that reports a published binary which dies on startup -
# and PowerShell threw CommandNotFoundException instead. The script's own safety net was the one thing
# guaranteed not to work.
#
# It mattered. A KeyboardAccelerator registered from MainWindow's constructor fail-fasts inside
# Microsoft.UI.Xaml.dll, so the app died during startup with no exception, no log and no window. The
# smoke test detected exactly that, called Fail, and the detection vanished into a missing-function
# error that nobody read, because the build had already printed a green "Kronos -> ..." line.
#
# Defined before anything can call it, which is the whole point of defining it.
function Fail($message) {
    Write-Host "Error: $message" -ForegroundColor Red
    exit 1
}

# Auto-detect target if not specified.
# [System.Runtime.InteropServices.RuntimeInformation] rather than the bare [RuntimeInformation]: the
# bare form only resolves once some other code has loaded that assembly, which is true under pwsh 7 but
# not under the Windows PowerShell 5.1 that a double-clicked .ps1 still uses. The fully qualified type
# loads it on demand.
if (-not $Target) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        $Target = 'Windows'
    }
    elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
                [System.Runtime.InteropServices.OSPlatform]::Linux)) {
        $Target = 'Linux'
    }
    else {
        Write-Host "Error: Could not auto-detect platform. Specify -Target explicitly." -ForegroundColor Red
        exit 1
    }
}

Write-Host "=== Kronos Build Script (PowerShell) ===" -ForegroundColor Green
Write-Host "Target: $Target | Configuration: $Configuration | Runtime: $Runtime" -ForegroundColor Cyan
Write-Host ""

# Git metadata for the About section: the tag and the branch, passed in with -p:.
#
# The commit needs no help - $(SourceRevisionId) comes from the git checkout, so the csproj picks it up on
# its own - but the tag and the branch do, because MSBuild cannot run git while evaluating properties.
#
# These were never passed, so KronosGitTag and KronosGitBranch were always empty. Two consequences, both
# visible in the About section: the version's hyperlink pointed at the generic releases page rather than
# the tagged release it belongs to, and BuildInfo.IsFromTagBuild was always false, so the build commit row
# was shown on every build including a release one, which is the opposite of when it is useful.
#
# Read with git rather than assumed. An archive with no .git, or a directory git refuses to trust, leaves
# them empty, which the csproj and BuildInfo both already treat as "not a release build" rather than an
# error. Callers have already noticed this script exit non-zero from git writing to stderr, so the
# preference is lowered for the read and restored, and only the output is used.
$gitMetadataArgs = @()
$previousPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'

    $branchName = (& git rev-parse --abbrev-ref HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($branchName)) {
        $gitMetadataArgs += "-p:KronosGitBranch=$branchName"
    }

    # Only a tag that is actually on this commit counts. An old tag left lying around would otherwise be
    # baked into a build made weeks later, and the About section would link to a release it is not.
    #
    # Collected with @() rather than piped into Select-Object -First 1, and the exit code deliberately not
    # checked. Selecting the first item closes the pipe before git has finished writing, so git takes a
    # broken pipe, and PowerShell reports that as $LASTEXITCODE = -1. Reading the output and then asking
    # whether it was empty is the honest test: if git printed nothing, there is no tag, and if git
    # printed something the exit code is beside the point. That bug shipped once - v1.55 was published
    # with an empty tag because of exactly this.
    $tagLines = @(& git tag --points-at HEAD 2>$null)
    $tagName = $tagLines | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($tagName)) {
        $gitMetadataArgs += "-p:KronosGitTag=$tagName"
    }
}
finally {
    $ErrorActionPreference = $previousPreference
}

if ($gitMetadataArgs.Count -gt 0) {
    Write-Host "Git metadata: $($gitMetadataArgs -join ' ')" -ForegroundColor DarkGray
}

# Check for .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "Error: .NET SDK not found. Please install .NET 10 SDK." -ForegroundColor Red
    exit 1
}

$dotnetVersion = dotnet --version
Write-Host "Found .NET SDK: $dotnetVersion" -ForegroundColor Green

$srcDir = Join-Path $PSScriptRoot "src"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

# Set license key env var if provided
if ($LicenseKey) {
    $env:IMAGESHARP_LICENSE_KEY = $LicenseKey
    Write-Host "Using SixLabors ImageSharp license key" -ForegroundColor Green
}

# Fails loudly if the native command that just ran did not succeed.
#
# $ErrorActionPreference = 'Stop' does not apply to native executables - a dotnet command returning
# non-zero does not throw, so the script used to carry straight on past a failed restore into
# `dotnet build --no-restore`. That reported "NETSDK1004: Assets file project.assets.json not
# found", which names the missing file rather than the restore error that caused it, so the actual
# failure was never shown.
#
# This deliberately does not forward arguments. A wrapper taking [string[]] would have to be called
# with -f, -c and -r, which PowerShell would try to bind to the wrapper's own parameters.
function Assert-DotnetSucceeded {
    param([string]$Description)

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE. The output above is the real error."
    }
}

# Restores, and checks that it really did.
#
# A failure here is opaque: restore can fail without writing the assets file, and the --no-restore
# build that follows then reports "NETSDK1004: Assets file project.assets.json not found", naming the
# symptom rather than the cause. So the assets file is deleted first and required afterwards.
#
# One restore, with no framework or runtime filter. The project has one target framework,
# net10.0-desktop, and declares both runtime identifiers (win-x64 and linux-x64), so a plain restore
# covers every build this script makes, on either operating system. It is also the only restore that
# leaves the committed packages.lock.json as it is.
#
# This used to make up to three attempts, falling back to -p:TargetFramework=net10.0 when evaluating
# the old net10.0-windows target failed on Linux. Under Uno that target framework no longer exists,
# and restoring a framework the project does not have would have rewritten the committed lock file
# for nothing.
#
# The configuration, though, must be the one about to be built. Uno.Sdk adds its hot-reload package,
# Uno.WinUI.DevServer, only when Optimize is false, and decides that at restore time. A restore with no
# configuration is a Debug restore, and a Release build after it fails with UNOB0019 ("The DevServer
# package is intended for development builds only").
function Restore-Dependencies {
    param($srcDir, $Configuration)

    $project = Join-Path $srcDir "Kronos.csproj"
    $assets = Join-Path (Join-Path $srcDir "obj") "project.assets.json"

    # An assets file left over from an earlier run would make a restore that genuinely failed look
    # like one that succeeded.
    Remove-Item $assets -Force -ErrorAction SilentlyContinue

    # Never `-f`: on restore it means --force, and forwards the next argument to MSBuild as a second
    # project (MSB1008).
    dotnet restore $project -p:Configuration=$Configuration | Write-Host
    Assert-DotnetSucceeded "Restore"

    if (-not (Test-Path $assets)) {
        throw "Restore exited successfully but wrote no project.assets.json. The output above is the real error; building with --no-restore would only have reported NETSDK1004, which names the symptom and hides the cause."
    }
}

function Build-Linux {
    param($Configuration, $Runtime, $NoRestore, $srcDir, $outputDir)

    # The same Uno project as Windows, for the Linux runtime identifier. It does not compile yet: the
    # data layer still has Windows-only code (registry, P/Invoke) that the old net10.0 Linux CLI target
    # excluded. That is the next project after the Uno migration, not a build-script problem, so this
    # fails honestly at the build step rather than being skipped.
    Write-Host "=== Building Linux ===" -ForegroundColor Green

    if (-not $NoRestore) {
        Write-Host "Restoring dependencies..." -ForegroundColor Yellow
        Restore-Dependencies $srcDir $Configuration
    }

    Write-Host "Building Linux ($Configuration)..." -ForegroundColor Yellow
    dotnet build "$srcDir\Kronos.csproj" -f net10.0-desktop -c $Configuration -r $Runtime --no-restore | Write-Host
    Assert-DotnetSucceeded "Build (Linux)"

    Write-Host "Publishing Linux for $Runtime..." -ForegroundColor Yellow
    # Nested Join-Path rather than the three-argument form: -AdditionalChildPath only exists in
    # PowerShell 7+, so the short form failed outright on the Windows PowerShell 5.1 that ships with
    # Windows and is still the default for a double-clicked .ps1.
    $outputDir = Join-Path (Join-Path $PSScriptRoot "Output") "$Runtime-$timestamp"
    # No --no-build here: publish has to re-evaluate for the RID anyway (runtime pack, self-contained
    # layout), and pairing an RID-less build with an RID'd --no-build publish made this step look for
    # bin/<cfg>/<tfm>/<rid>/ output that the build had never produced.
    # --no-restore: publish would otherwise run its own restore. The restore above already covered
    # this framework and this RID, which is exactly what the build step then consumed.
    dotnet publish "$srcDir\Kronos.csproj" -f net10.0-desktop -c $Configuration -r $Runtime --self-contained --no-restore -o $outputDir | Write-Host
    Assert-DotnetSucceeded "Publish (Linux)"

    Write-Host "Linux published to: $outputDir" -ForegroundColor Green
    return $outputDir
}

function Build-Windows {
    param($Configuration, $NoRestore, $srcDir, $outputDir)

    Write-Host "=== Building Windows GUI ===" -ForegroundColor Green

    # The Windows build is Release with KronosPortable=true, whatever -Configuration asked for, because
    # that is the only Windows build this script produces: the portable zip.
    #
    # KronosPortable defines PORTABLE (see the csproj): user data in StoredData beside the executable,
    # the portable self-update, and the updater's -installer.exe branch compiled out, so a portable
    # build is not offered an installer asset that a portable zip release never publishes. It replaces
    # the Release_Portable configuration, which existed only because WinAppSDK could not publish
    # unpackaged any other way.
    #
    # Restore, build and publish all use it. They have to agree: see Restore-Dependencies for the
    # UNOB0019 failure a Debug restore causes in a Release build.
    $portableConfiguration = 'Release'
    if ($Configuration -ne $portableConfiguration) {
        Write-Host "Note: the Windows portable build is always $portableConfiguration; -Configuration $Configuration applies to Linux only." -ForegroundColor DarkYellow
    }

    if (-not $NoRestore) {
        Write-Host "Restoring dependencies..." -ForegroundColor Yellow
        Restore-Dependencies $srcDir $portableConfiguration
    }

    Write-Host "Building Windows GUI ($portableConfiguration, KronosPortable)..." -ForegroundColor Yellow
    dotnet build "$srcDir\Kronos.csproj" -f net10.0-desktop -c $portableConfiguration -r win-x64 --no-restore -p:KronosPortable=true | Write-Host
    Assert-DotnetSucceeded "Build (Windows GUI)"

    Write-Host "Publishing Windows Portable ($portableConfiguration, KronosPortable)..." -ForegroundColor Yellow
    $outputDir = Join-Path (Join-Path $PSScriptRoot "Output") "win-x64-portable-$timestamp"
    # --no-restore for the same reason as the Linux publish.
    #
    # Self-contained, so it needs neither .NET nor - now that the app is on Uno rather than Windows
    # App SDK - the Windows App Runtime. Uno ships its own UI framework (Uno.UI.dll) and renders through
    # Skia, so there is no Microsoft.UI.Xaml.dll to resolve from an installed runtime.
    #
    # Deliberately NOT -p:PublishSingleFile=true. Under WinAppSDK it produced a binary that crashed
    # with 0xC000027B before any window appeared, and the app reads Assets and Translations from disk
    # beside the executable, which a single-file bundle would not lay out.
    dotnet publish "$srcDir\Kronos.csproj" -f net10.0-desktop -c $portableConfiguration -r win-x64 --self-contained --no-restore -o $outputDir -p:KronosPortable=true @gitMetadataArgs | Write-Host
    Assert-DotnetSucceeded "Publish (Windows GUI)"

    # The files the app cannot start, or cannot look right, without. Kronos.pri was the WinAppSDK
    # resource index; Uno has none, and its equivalent is its own UI framework being present at all.
    foreach ($required in @('Kronos.exe', 'Uno.UI.dll', 'Assets\icon.ico', 'Assets\static_manifest.json')) {
        if (-not (Test-Path (Join-Path $outputDir $required))) {
            Fail "No $required in $outputDir. The published build is incomplete."
        }
    }
    if (-not (Test-Path (Join-Path $outputDir 'Translations'))) {
        Fail "No Translations folder in $outputDir. Every string in the app is read from it at runtime."
    }

    # Launch it. A build that produces a binary which dies on startup is not a build worth shipping,
    # and that is exactly what happened: a portable zip was published and attached to a release, and
    # it crashed before showing a window. Resource and loader failures are invisible from the
    # outside, so the only way to know is to start the thing.
    Write-Host "Smoke testing the published binary..." -ForegroundColor Yellow
    $process = $null
    try {
        $process = Start-Process -FilePath (Join-Path $outputDir 'Kronos.exe') -PassThru -ErrorAction Stop
        Start-Sleep -Seconds 12

        if ($process.HasExited) {
            Fail ("The published binary exited immediately with code {0}. It will not run, so it must not be " +
                  "published. Check the Application event log for the faulting module." -f $process.ExitCode)
        }

        Write-Host "  started and still running after 12s (window: '$((Get-Process -Id $process.Id).MainWindowTitle)')" -ForegroundColor Green
    }
    finally {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    
    Write-Host "Windows Portable published to: $outputDir" -ForegroundColor Green
    return $outputDir
}

$srcDir = Join-Path $PSScriptRoot "src"
$outputs = @()

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

switch ($Target) {
    'Linux' {
        $outputs += Build-Linux -Configuration $Configuration -Runtime $Runtime -NoRestore $NoRestore -srcDir $srcDir
    }
    'Windows' {
        if (-not $runningOnWindows) {
            Write-Host "Error: Windows build must run on Windows" -ForegroundColor Red
            exit 1
        }
        $outputs += Build-Windows -Configuration $Configuration -NoRestore $NoRestore -srcDir $srcDir
    }
    'All' {
        if (-not $runningOnWindows) {
            Write-Host "Error: 'All' target requires Windows (for Windows build)" -ForegroundColor Red
            exit 1
        }
        $outputs += Build-Linux -Configuration $Configuration -Runtime $Runtime -NoRestore $NoRestore -srcDir $srcDir
        $outputs += Build-Windows -Configuration $Configuration -NoRestore $NoRestore -srcDir $srcDir
    }
}

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
foreach ($output in $outputs) {
    Write-Host "Output: $output" -ForegroundColor Green
}

# Machine-readable handoff. publish-release.ps1 needs the output directory, and taking the last line
# of this script's output does not work: the human-readable summary below is not a path, so the caller
# was handed "Note: Copy the entire output folder to target machine to run." and tried to join a drive
# called Note. A tagged line is parseable no matter what else is printed.
Write-Output "KRONOS_OUTPUT_DIR=$($outputs -join ';')"

Write-Host ""
Write-Host "To run Linux: ./Kronos" -ForegroundColor Yellow
Write-Host "To run Windows: Double-click Kronos.exe" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: Copy the entire output folder to target machine to run." -ForegroundColor Yellow
