#!/usr/bin/env pwsh
<# 
.SYNOPSIS
    Kronos Linux Build Script (PowerShell)
    Builds the Kronos CLI for Linux from Windows using cross-compilation

.DESCRIPTION
    This script builds the Linux version of Kronos on Windows using .NET's cross-platform publishing.
    Requires .NET 10 SDK installed.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Runtime linux-arm64
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [ValidateSet('linux-x64')]
    [string]$Runtime = 'linux-x64',
    
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Kronos Linux Build Script (PowerShell) ===" -ForegroundColor Green
Write-Host ""

# Check for .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "Error: .NET SDK not found. Please install .NET 10 SDK." -ForegroundColor Red
    exit 1
}

$dotnetVersion = dotnet --version
Write-Host "Found .NET SDK: $dotnetVersion" -ForegroundColor Green

$srcDir = Join-Path $PSScriptRoot "src"
$outputDir = Join-Path $PSScriptRoot "Output" $Runtime

Write-Host "Restoring dependencies..." -ForegroundColor Yellow
if (-not $NoRestore) {
    dotnet restore "$srcDir\Kronos.csproj" -f net10.0
}

Write-Host "Building for Linux ($Configuration)..." -ForegroundColor Yellow
dotnet build "$srcDir\Kronos.csproj" -f net10.0 -c $Configuration --no-restore

Write-Host "Publishing for $Runtime..." -ForegroundColor Yellow
dotnet publish "$srcDir\Kronos.csproj" -f net10.0 -c $Configuration -r $Runtime --self-contained -o $outputDir --no-build

Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "Output: $outputDir" -ForegroundColor Green
Write-Host ""
Write-Host "To run on Linux: ./Kronos --help" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: Copy the entire output folder to a Linux machine to run." -ForegroundColor Yellow