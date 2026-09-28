@echo off

REM Keep in step with <Version> in the csproj and APP_VERSION in NSIS\Installer.nsi.
REM 1.3.0 is the first release under the Kronos name. It is deliberately higher than
REM upstream DLSS Swapper 1.2.6.1 so the in-app update check stays quiet.
set app_version=1.3.0
set initial_directory=%cd%

set csproj_file=..\src\Kronos.csproj

set output_installer=Output\Kronos-%app_version%-installer.exe
set output_zip=Output\Kronos-%app_version%-portable.zip
