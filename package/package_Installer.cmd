@echo off

call "%~dp0config.cmd"

echo.
echo ################################
echo Packaging installer
echo ################################
echo.

REM Inno Setup is installed per-user by winget and machine-wide by the traditional installer, so
REM check both rather than requiring it on PATH. The bare name is the last resort, for a developer
REM who has put it there themselves.
set "iscc_exe="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "iscc_exe=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "iscc_exe=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "iscc_exe=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined iscc_exe set "iscc_exe=ISCC.exe"

REM Unconditional, with the error suppressed so a missing previous build is not noise. This has to
REM be unconditional: leaving a stale exe in place would let a failed compile look like a success.
DEL "%output_installer%" > NUL 2>&1

REM The version is handed over from config.cmd rather than repeated in the .iss, which is what
REM kept the installer's name, the csproj and the registry entry drifting apart before.
"%iscc_exe%" /DAppVersion=%app_version% Installer.iss || goto :error

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
