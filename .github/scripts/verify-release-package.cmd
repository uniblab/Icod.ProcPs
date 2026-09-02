@echo off
setlocal EnableExtensions

if "%~1"=="" goto usage
if "%~2"=="" goto usage
if not "%~3"=="" goto usage

set "ARTIFACT_DIR=%~1"
set "CONFIGURATION=%~2"

if /I "%CONFIGURATION%"=="Debug" (
    set "CONFIGURATION=Debug"
) else if /I "%CONFIGURATION%"=="Staging" (
    set "CONFIGURATION=Staging"
) else if /I "%CONFIGURATION%"=="Release" (
    set "CONFIGURATION=Release"
) else (
    goto usage
)

pushd "%~dp0\..\.." >nul || exit /b 1

if not exist "%ARTIFACT_DIR%" (
    echo Artifact directory "%ARTIFACT_DIR%" does not exist. 1>&2
    popd
    exit /b 1
)

for %%I in ("%ARTIFACT_DIR%") do set "ARTIFACT_DIR=%%~fI"

echo.
echo === Verify packed ProcPs artifact (%CONFIGURATION%) ===
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .github\scripts\verify-release-package.ps1 -ArtifactDirectory "%ARTIFACT_DIR%" -Configuration %CONFIGURATION%
set "RESULT=%errorlevel%"

popd
exit /b %RESULT%

:usage
echo Usage: %~nx0 ^<artifact-directory^> ^<Debug^|Staging^|Release^> 1>&2
exit /b 1
