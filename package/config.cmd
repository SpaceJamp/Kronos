@echo off

REM Keep in step with <Version> in the csproj. The value is also handed to the Inno Setup script as
REM /DAppVersion, so the installer filename and the version it registers cannot drift apart.
REM 1.46 warns when a swap would install an older DLSS runtime than the game already has, and
REM replaces the single consumed dll backup with a versioned chain so swaps can be undone more than
REM once. Still deliberately higher than upstream DLSS Swapper 1.2.6.1 so the update check stays
REM quiet.
set app_version=1.46
set initial_directory=%cd%

set csproj_file=..\src\Kronos.csproj

set output_installer=Output\Kronos-%app_version%-installer.exe
set output_zip=Output\Kronos-%app_version%-portable.zip
