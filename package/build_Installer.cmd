@echo off

call "%~dp0config.cmd"

REM Delete bin and obj directory
rmdir /s /q ..\src\bin\publish\installer\
rmdir /s /q ..\src\obj\

REM create the output folder if it doesn't already exist.
mkdir Output > NUL 2>&1

REM
REM Collect the git metadata the csproj injects into BuildInfo.
REM
REM MSBuild cannot call git while evaluating properties, so this has to be done here and passed in with
REM -p:. Without it BuildInfo.GitCommit is empty and BuildInfo.BuildTimestamp is zero, which made the
REM Settings page show a build date of 1 January 1970. That is the same value FromUnixTimeSeconds(0)
REM produces, and it read as a real date rather than as missing information.
REM
REM describe --tags --exact-match finds the tag on the current commit. An empty result is expected for
REM a build between releases and is not an error, because BuildInfo.IsFromTagBuild treats an empty tag
REM as "not a release build".
REM
set "git_branch="
set "git_commit="
set "git_tag="

for /f "usebackq delims=" %%i in (`git rev-parse --abbrev-ref HEAD 2^>nul`) do set "git_branch=%%i"
for /f "usebackq delims=" %%i in (`git rev-parse HEAD 2^>nul`) do set "git_commit=%%i"
for /f "usebackq delims=" %%i in (`git describe --tags --exact-match 2^>nul`) do set "git_tag=%%i"

if "%git_commit%"=="" echo WARNING: git commit not available, BuildInfo will show an unknown build date.
if "%git_branch%"=="" set "git_branch=unknown"
if "%git_commit%"=="" set "git_commit=unknown"
if "%git_tag%"=="" set "git_tag="

echo.
echo ################################
echo Compiling app
echo ################################
echo.

dotnet publish "%csproj_file%" ^
	--runtime win-x64 ^
    --self-contained ^
    --configuration Release ^
    -p:PublishDir=bin\publish\installer\ ^
    -p:KronosGitBranch="%git_branch%" ^
    -p:KronosGitCommit=%git_commit% ^
    -p:KronosGitTag="%git_tag%" || goto :error

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
