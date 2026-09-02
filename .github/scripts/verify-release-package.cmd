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

set "PACKAGE_VERSION="
for /f "delims=" %%V in ('dotnet msbuild procps\Icod.ProcPs.Router.csproj -nologo -getProperty:PackageVersion') do set "PACKAGE_VERSION=%%V"
if not defined PACKAGE_VERSION (
    echo Unable to determine PackageVersion. 1>&2
    popd
    exit /b 1
)

if not exist "%ARTIFACT_DIR%\Icod.ProcPs.%PACKAGE_VERSION%.nupkg" (
    echo Icod.ProcPs package not found in "%ARTIFACT_DIR%". 1>&2
    popd
    exit /b 1
)

echo.
echo === Verify ProcPs distribution (%CONFIGURATION%) ===
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File packaging\VerifyDistribution.ps1 -Configuration %CONFIGURATION%
set "RESULT=%errorlevel%"

popd
exit /b %RESULT%

:usage
echo Usage: %~nx0 ^<artifact-directory^> ^<Debug^|Staging^|Release^> 1>&2
exit /b 1
