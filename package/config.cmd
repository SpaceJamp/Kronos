@echo off

REM Keep in step with <Version> in the csproj and APP_VERSION in NSIS\Installer.nsi.
REM 1.4.0 adds automatic Steam app id detection for manually added games. It is
REM deliberately higher than upstream DLSS Swapper 1.2.6.1 so the update check stays quiet.
set app_version=1.4.0
set initial_directory=%cd%

set csproj_file=..\src\Kronos.csproj

set output_installer=Output\Kronos-%app_version%-installer.exe
set output_zip=Output\Kronos-%app_version%-portable.zip
