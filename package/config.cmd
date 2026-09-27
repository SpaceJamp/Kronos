@echo off

REM Keep in step with <Version> in the csproj and APP_VERSION in NSIS\Installer.nsi.
REM 1.3.0 is the first release of this unofficial fork. It is deliberately higher than
REM upstream's 1.2.6.1 so the in-app update check does not offer users the official build.
set app_version=1.3.0
set initial_directory=%cd%

set csproj_file=..\src\Unofficial DLSS Swapper.csproj

set output_installer=Output\Unofficial.DLSS.Swapper-%app_version%-installer.exe
set output_zip=Output\Unofficial.DLSS.Swapper-%app_version%-portable.zip
