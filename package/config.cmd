@echo off

REM Keep in step with <Version> in the csproj. The value is also handed to the Inno Setup script as
REM /DAppVersion, so the installer filename and the version it registers cannot drift apart.
REM 1.45 is a 64-bit only build with automatic Steam app id detection. It is
REM deliberately higher than upstream DLSS Swapper 1.2.6.1 so the update check stays quiet.
set app_version=1.45
set initial_directory=%cd%

set csproj_file=..\src\Kronos.csproj

set output_installer=Output\Kronos-%app_version%-installer.exe
set output_zip=Output\Kronos-%app_version%-portable.zip
