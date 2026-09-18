@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "SCRIPT=%ROOT%\UpdateAssetBundlesFromGitLabApi.ps1"
set "PAUSE_ON_EXIT=1"
set "PS_ARGS="
set "EXIT_CODE=0"

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--no-pause" (
  set "PAUSE_ON_EXIT=0"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--skip-build-sync" (
  set "PS_ARGS=!PS_ARGS! -SkipBuildSync"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--no-commit" (
  set "PS_ARGS=!PS_ARGS! -NoCommit"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--help" goto SHOW_HELP
echo [AssetBundles] Unknown option: %~1
set "EXIT_CODE=1"
goto FINISH

:AFTER_PARSE_ARGS
if not exist "%PROJECT_DIR%\.git" (
  echo [AssetBundles] Main project repository not found: %PROJECT_DIR%
  echo [AssetBundles] Keep this script next to the MDPro3 project folder.
  set "EXIT_CODE=1"
  goto FINISH
)

if not exist "%SCRIPT%" (
  echo [AssetBundles] Missing helper script:
  echo [AssetBundles] %SCRIPT%
  set "EXIT_CODE=1"
  goto FINISH
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Root "%ROOT%" !PS_ARGS!
set "EXIT_CODE=%ERRORLEVEL%"
goto FINISH

:SHOW_HELP
echo Update_06_AssetBundles.bat [options]
echo.
echo Updates the StandaloneWindows64 folder next to this script from MDPro3-AssetBundles using the GitLab API.
echo This avoids the normal git fetch path that currently fails with HTTP 504 on the large pack.
echo It compares remote blob IDs, downloads only changed/missing files, verifies hashes,
echo optionally makes a local-only snapshot commit, and syncs the Build\MDPro3\StandaloneWindows64 folder next to this script.
echo It does not push anything.
echo.
echo Options:
echo   --skip-build-sync  Update source StandaloneWindows64 only.
echo   --no-commit        Leave downloaded files uncommitted in the local assetbundle repo.
echo   --no-pause         Do not pause before exiting.
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
