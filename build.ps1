#!/usr/bin/env pwsh
<# 
.SYNOPSIS
    Kronos Build Script (PowerShell)
    Builds Kronos for the current platform or cross-compiles for Linux

.DESCRIPTION
    This script builds Kronos for the current platform (Windows GUI or Linux CLI) 
    or cross-compiles Linux from Windows.
    Requires .NET 10 SDK installed.

.EXAMPLE
    .\build.ps1                           # Build for current platform
    .\build.ps1 -Configuration Debug      # Debug build
    .\build.ps1 -Target Linux             # Cross-compile Linux from Windows
    .\build.ps1 -Target Windows           # Build Windows (native)
    .\build.ps1 -Target All               # Build both Linux CLI and Windows GUI
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

# Restores, trying several approaches in turn.
#
# No single restore command works everywhere and a failure here is opaque: restore can fail without
# writing the assets file, and the --no-restore build that follows then reports "NETSDK1004: Assets
# file project.assets.json not found", naming the symptom rather than the cause.
#
#   Pass 1  every target framework - preferred. The only pass that leaves the committed lock file
#           untouched, so it keeps builds reproducible.
#   Pass 2  net10.0 only - never evaluates the Windows target framework, which is the most likely
#           reason pass 1 fails on Linux.
#   Pass 3  net10.0 only plus the runtime identifier - adds the RID-specific assets the build needs.
#
# Passes 2 and 3 restore one framework, which rewrites the committed packages.lock.json to hold only
# that framework - 553 lines of it, as measured. So it is saved first and restored in a finally block,
# which also covers restore throwing. Quietly deleting the Windows target's dependency graph from a
# committed file would be worse than any restore failure.
function Restore-Dependencies {
    param($srcDir, $Runtime = 'linux-x64')

    $project = Join-Path $srcDir "Kronos.csproj"
    $assets = Join-Path (Join-Path $srcDir "obj") "project.assets.json"
    $lockFile = Join-Path $srcDir "packages.lock.json"

    $attemptLabels = @(
        'every target framework',
        'net10.0 only',
        "net10.0 only, $Runtime"
    )

    for ($i = 0; $i -lt $attemptLabels.Count; $i++) {
        Write-Host ("Restore pass {0} of {1}: {2}" -f ($i + 1), $attemptLabels.Count, $attemptLabels[$i]) -ForegroundColor Blue

        # An assets file left over from an earlier run would make a restore that genuinely failed
        # look like one that succeeded.
        Remove-Item $assets -Force -ErrorAction SilentlyContinue

        $backup = $null
        if ($i -gt 0 -and (Test-Path $lockFile)) {
            $backup = "$lockFile.kronosbak"
            Copy-Item $lockFile $backup -Force
        }

        try {
            # Literal commands rather than a splatted argument array. `-f` on restore means --force and
            # forwards the framework as a second project (MSB1008), and an argument array is one step
            # further from the thing that goes wrong.
            #
            # The exit code is recorded rather than asserted, so a failing pass falls through to the
            # next one instead of ending the build - which is the whole point of having several.
            $restoreExitCode = 1
            if ($i -eq 0) {
                dotnet restore $project
                $restoreExitCode = $LASTEXITCODE
            } elseif ($i -eq 1) {
                dotnet restore $project -p:TargetFramework=net10.0
                $restoreExitCode = $LASTEXITCODE
            } else {
                dotnet restore $project -p:TargetFramework=net10.0 -r $Runtime
                $restoreExitCode = $LASTEXITCODE
            }

            if ($restoreExitCode -eq 0 -and (Test-Path $assets)) {
                Write-Host ("Restore succeeded on pass {0}: {1}." -f ($i + 1), $attemptLabels[$i]) -ForegroundColor Green
                return
            }
        }
        finally {
            if ($backup) {
                Copy-Item $backup $lockFile -Force
                Remove-Item $backup -Force -ErrorAction SilentlyContinue
            }
        }

        if ($i -lt $attemptLabels.Count - 1) {
            Write-Host "That did not produce the assets file. Trying the next approach." -ForegroundColor Yellow
            Write-Host ""
        }
    }

    throw "Restore produced no assets file on any of $($attemptLabels.Count) passes. The errors above are the real ones; building with --no-restore would only have reported NETSDK1004, which names the symptom and hides the cause."
}

function Build-Linux {
    param($Configuration, $Runtime, $NoRestore, $srcDir, $outputDir)
    
    Write-Host "=== Building Linux CLI ===" -ForegroundColor Green
    
    if (-not $NoRestore) {
        Write-Host "Restoring dependencies..." -ForegroundColor Yellow
        Restore-Dependencies $srcDir $Runtime
    }
    
    Write-Host "Building Linux CLI ($Configuration)..." -ForegroundColor Yellow
    dotnet build "$srcDir\Kronos.csproj" -f net10.0 -c $Configuration -r $Runtime --no-restore
    Assert-DotnetSucceeded "Build (Linux CLI)"

    Write-Host "Publishing Linux CLI for $Runtime..." -ForegroundColor Yellow
    # Nested Join-Path rather than the three-argument form: -AdditionalChildPath only exists in
    # PowerShell 7+, so the short form failed outright on the Windows PowerShell 5.1 that ships with
    # Windows and is still the default for a double-clicked .ps1.
    $outputDir = Join-Path (Join-Path $PSScriptRoot "Output") "$Runtime-$timestamp"
    # No --no-build here: publish has to re-evaluate for the RID anyway (runtime pack, self-contained
    # layout), and pairing an RID-less build with an RID'd --no-build publish made this step look for
    # bin/<cfg>/<tfm>/<rid>/ output that the build had never produced.
    # --no-restore: publish would otherwise restore again, evaluating the Windows target framework on
    # Linux - the thing the restore passes exist to avoid - and rewriting the committed lock file
    # outside the protection in Restore-Dependencies. The restore above covered this framework and
    # this RID, which is exactly what the build step then consumed.
    dotnet publish "$srcDir\Kronos.csproj" -f net10.0 -c $Configuration -r $Runtime --self-contained --no-restore -o $outputDir
    Assert-DotnetSucceeded "Publish (Linux CLI)"
    
    Write-Host "Linux CLI published to: $outputDir" -ForegroundColor Green
    return $outputDir
}

function Build-Windows {
    param($Configuration, $NoRestore, $srcDir, $outputDir)
    
    Write-Host "=== Building Windows GUI ===" -ForegroundColor Green
    
    if (-not $NoRestore) {
        Write-Host "Restoring dependencies..." -ForegroundColor Yellow
        Restore-Dependencies $srcDir 'win-x64'
    }
    
    
    Write-Host "Building Windows GUI ($Configuration)..." -ForegroundColor Yellow
    dotnet build "$srcDir\Kronos.csproj" -f net10.0-windows10.0.26100.0 -c $Configuration -r win-x64 --no-restore
    Assert-DotnetSucceeded "Build (Windows GUI)"

    # The published configuration is Release_Portable, whatever -Configuration asked for.
    #
    # Not because Release is broken - a plain `-c Release` publish runs perfectly well. It is because
    # Release_Portable is this project's designated *unpackaged* configuration: OutputType Exe, with
    # the JSON assets embedded as resources, and the PORTABLE constant defined. PORTABLE compiles out
    # the updater's -installer.exe branch, so a portable build is not offered an installer it would
    # have no way to run. Release leaves that branch in, where it will look for an asset that a
    # portable zip release does not publish.
    #
    # Measured across every combination, because the first artifact published here crashed and the
    # cause had to be pinned down rather than guessed:
    #
    #   -c Release                     plain publish                    runs
    #   -c Release                     + PublishSingleFile=true        CRASHES  <- what shipped
    #   -c Release                     + WindowsAppSDKSelfContained    CRASHES
    #   -c Release_Portable            plain publish                    runs
    #   -c Release_Portable            + WindowsAppSDKSelfContained    CRASHES
    #
    # All three crashes are 0xC000027B, STATUS_STOWED_RESOURCE_NOT_FOUND, in Microsoft.UI.Xaml.dll,
    # before any window appears.
    $portableConfiguration = 'Release_Portable'

    Write-Host "Publishing Windows Portable ($portableConfiguration)..." -ForegroundColor Yellow
    $outputDir = Join-Path (Join-Path $PSScriptRoot "Output") "win-x64-portable-$timestamp"
    # --no-restore for the same reason as the Linux publish: it would otherwise restore again and
    # rewrite the committed lock file outside the protection in Restore-Dependencies.
    #
    # Deliberately NOT -p:PublishSingleFile=true. The csproj imports CopyPriFile.targets only when
    # PublishSingleFile is not true, so single-file means the resource index is never embedded - and
    # that is exactly the build that was published and did not start. Windows App SDK does not support
    # single-file publishing at all.
    #
    # Deliberately NOT -p:WindowsAppSDKSelfContained=true either. It reads like it would remove the
    # runtime prerequisite, but measured it crashes Release_Portable where it otherwise runs. The
    # artifact therefore needs the Windows App Runtime installed, which the README states.
    dotnet publish "$srcDir\Kronos.csproj" -f net10.0-windows10.0.26100.0 -c $portableConfiguration -r win-x64 --self-contained --no-restore -o $outputDir
    Assert-DotnetSucceeded "Publish (Windows GUI)"

    if (-not (Test-Path (Join-Path $outputDir 'Kronos.exe'))) { Fail "No Kronos.exe in $outputDir." }
    if (-not (Test-Path (Join-Path $outputDir 'Kronos.pri'))) {
        Fail "No Kronos.pri in $outputDir. Without the resource index the app cannot start, and it fails at launch rather than saying so."
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
Write-Host "To run Linux CLI: ./Kronos --help" -ForegroundColor Yellow
Write-Host "To run Windows: Double-click Kronos.exe" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: Copy the entire output folder to target machine to run." -ForegroundColor Yellow