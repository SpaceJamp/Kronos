@echo off

call "%~dp0config.cmd"

REM Delete bin and obj directory
rmdir /s /q ..\src\bin\publish\portable\
rmdir /s /q ..\src\obj\

REM create the output folder if it doesn't already exist.
mkdir Output > NUL 2>&1

REM
REM Collect the git metadata the csproj injects into BuildInfo. Kept in step with
REM build_Installer.cmd, which does the same thing.
REM
REM Only the branch and tag have to come from here. MSBuild cannot run git while evaluating
REM properties, and the commit is filled in by the SDK from SourceRevisionId, so passing it would be
REM redundant. An empty tag is expected between releases and is not an error: BuildInfo.IsFromTagBuild
REM treats an empty tag as "not a release build".
REM
set "git_branch="
set "git_tag="

for /f "usebackq delims=" %%i in (`git rev-parse --abbrev-ref HEAD 2^>nul`) do set "git_branch=%%i"
for /f "usebackq delims=" %%i in (`git describe --tags --exact-match 2^>nul`) do set "git_tag=%%i"

if "%git_branch%"=="" set "git_branch=unknown"

echo.
echo ################################
echo Compiling app
echo ################################
echo.

dotnet publish "%csproj_file%" ^
	--runtime win-x64 ^
    --self-contained ^
    --configuration Release_Portable ^
    -p:PublishDir=bin\publish\portable\ ^
    -p:KronosGitBranch="%git_branch%" ^
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
