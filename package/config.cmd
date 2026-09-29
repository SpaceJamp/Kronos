@echo off

REM Keep in step with <Version> in the csproj. The value is also handed to the Inno Setup script as
REM /DAppVersion, so the installer filename and the version it registers cannot drift apart.
REM
REM Bump this on every change that ships. The log's session header records the commit hash as well
REM as this version, which is what makes a stale install identifiable: two locally built binaries
REM both claim the same version and only the hash tells them apart.
REM
REM 1.47 adds a mass update for the games library, tick games and preview what would change before
REM anything is written, and fixes a startup crash from updating a bound property off the UI thread.
REM Still deliberately higher than upstream DLSS Swapper 1.2.6.1 so the update check stays quiet.
set app_version=1.48
set initial_directory=%cd%

set csproj_file=..\src\Kronos.csproj

set output_installer=Output\Kronos-%app_version%-installer.exe
set output_zip=Output\Kronos-%app_version%-portable.zip
