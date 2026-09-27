@echo off

call "%~dp0config.cmd"

echo.
echo ################################
echo Packaging installer
echo ################################
echo.

:installer 
DEL NSIS\installer.exe > NUL 2>&1
DEL NSIS\FileList.nsh > NUL 2>&1

REM -ExecutionPolicy Bypass matches package_Portable.cmd. Without it the default
REM Windows policy blocks this unsigned script and the build fails at step one.
pwsh.exe -ExecutionPolicy Bypass -File .\NSIS\create_nsh_file_list.ps1 || goto :error

makensis.exe NSIS\Installer.nsi || goto :error
 
REM Move the installer to the output folder.
move NSIS\installer.exe "%output_installer%" || goto :error

REM Everything is fine, go to the end of the file.
goto :end

REM If there was an error output this error message and navigate back to the initial directory 
:error
echo.
echo.
echo ERROR: Failed with error code %errorlevel%.
REM Save the failing code first: a successful cd resets %errorlevel% to 0,
REM which would make a failed build report success.
set "result=%errorlevel%"
cd %initial_directory% > NUL 2>&1
exit /b %result%

:end
exit /b 0
