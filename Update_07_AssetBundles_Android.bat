@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "SCRIPT=%ROOT%\UpdateAssetBundlesFromGitLabApi.ps1"
set "PAUSE_ON_EXIT=1"
set "PS_ARGS=-Platform Android -SkipBuildSync"
set "EXIT_CODE=0"

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--no-pause" (
  set "PAUSE_ON_EXIT=0"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--no-commit" (
  set "PS_ARGS=!PS_ARGS! -NoCommit"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--help" goto SHOW_HELP
echo [AssetBundles-Android] Unknown option: %~1
set "EXIT_CODE=1"
goto FINISH

:AFTER_PARSE_ARGS
if not exist "%PROJECT_DIR%\.git" (
  echo [AssetBundles-Android] Main project repository not found: %PROJECT_DIR%
  echo [AssetBundles-Android] Keep this script next to the MDPro3 project folder.
  set "EXIT_CODE=1"
  goto FINISH
)

if not exist "%SCRIPT%" (
  echo [AssetBundles-Android] Missing helper script:
  echo [AssetBundles-Android] %SCRIPT%
  set "EXIT_CODE=1"
  goto FINISH
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Root "%ROOT%" !PS_ARGS!
set "EXIT_CODE=%ERRORLEVEL%"
goto FINISH

:SHOW_HELP
echo Update_07_AssetBundles_Android.bat [options]
echo.
echo Crawls and updates the Android folder next to this script from MDPro3-AssetBundles using the GitLab API.
echo Output:
echo   Android
echo   Platforms\Android  - junction to Android
echo.
echo It does not push anything.
echo.
echo Options:
echo   --no-commit  Leave downloaded files uncommitted in the local Android snapshot repo.
echo   --no-pause   Do not pause before exiting.
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
