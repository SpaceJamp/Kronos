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

function Build-Linux {
    param($Configuration, $Runtime, $NoRestore, $srcDir, $outputDir)
    
    Write-Host "=== Building Linux CLI ===" -ForegroundColor Green
    
    if (-not $NoRestore) {
        Write-Host "Restoring dependencies..." -ForegroundColor Yellow
        # No framework filter here. `dotnet restore` has no --framework option at all, and its -f is
        # --force - so "-f net10.0" parsed as "--force net10.0", and net10.0 was then passed on as a
        # second project: "MSB1008: Only one project can be specified." Restore handles every target
        # framework in the project by itself, which is what we want anyway.
        dotnet restore "$srcDir\Kronos.csproj"
    }
    
    Write-Host "Building Linux CLI ($Configuration)..." -ForegroundColor Yellow
    dotnet build "$srcDir\Kronos.csproj" -f net10.0 -c $Configuration -r $Runtime --no-restore

    Write-Host "Publishing Linux CLI for $Runtime..." -ForegroundColor Yellow
    # Nested Join-Path rather than the three-argument form: -AdditionalChildPath only exists in
    # PowerShell 7+, so the short form failed outright on the Windows PowerShell 5.1 that ships with
    # Windows and is still the default for a double-clicked .ps1.
    $outputDir = Join-Path (Join-Path $PSScriptRoot "Output") "$Runtime-$timestamp"
    # No --no-build here: publish has to re-evaluate for the RID anyway (runtime pack, self-contained
    # layout), and pairing an RID-less build with an RID'd --no-build publish made this step look for
    # bin/<cfg>/<tfm>/<rid>/ output that the build had never produced.
    dotnet publish "$srcDir\Kronos.csproj" -f net10.0 -c $Configuration -r $Runtime --self-contained -o $outputDir
    
    Write-Host "Linux CLI published to: $outputDir" -ForegroundColor Green
    return $outputDir
}

function Build-Windows {
    param($Configuration, $NoRestore, $srcDir, $outputDir)
    
    Write-Host "=== Building Windows GUI ===" -ForegroundColor Green
    
    if (-not $NoRestore) {
        Write-Host "Restoring dependencies..." -ForegroundColor Yellow
        dotnet restore "$srcDir\Kronos.csproj"
    }
    
    Write-Host "Building Windows GUI ($Configuration)..." -ForegroundColor Yellow
    dotnet build "$srcDir\Kronos.csproj" -f net10.0-windows10.0.26100.0 -c $Configuration -r win-x64 --no-restore

    Write-Host "Publishing Windows Portable..." -ForegroundColor Yellow
    $outputDir = Join-Path (Join-Path $PSScriptRoot "Output") "win-x64-portable-$timestamp"
    dotnet publish "$srcDir\Kronos.csproj" -f net10.0-windows10.0.26100.0 -c $Configuration -r win-x64 --self-contained -p:PublishSingleFile=true -o $outputDir
    
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
Write-Host ""
Write-Host "To run Linux CLI: ./Kronos --help" -ForegroundColor Yellow
Write-Host "To run Windows: Double-click Kronos.exe" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: Copy the entire output folder to target machine to run." -ForegroundColor Yellow