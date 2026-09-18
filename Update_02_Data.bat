@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "BUILD_DIR=%ROOT%\Build\MDPro3"
set "PAUSE_ON_EXIT=1"
rem Data files that only exist in the build folder, because the game or the player
rem writes them there while it runs. A mirror sync reads them as leftovers and
rem deletes them, so they stay out of the mirror and are copied by hand instead.
set "LOCAL_ONLY_FILES=cards_Lite.json cards.json FileGroups.json hosts.conf book.ydk sr.ydk ur.ydk "Duel Links Ids.txt""
set "SYNC_BUILD=1"
set "EXIT_CODE=0"

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--no-pause" (
  set "PAUSE_ON_EXIT=0"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--skip-build-sync" (
  set "SYNC_BUILD=0"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--help" goto SHOW_HELP
echo [Data] Unknown option: %~1
set "EXIT_CODE=1"
goto FINISH

:AFTER_PARSE_ARGS
if not exist "%PROJECT_DIR%\.git" (
  echo [Data] Main project repository not found: %PROJECT_DIR%
  echo [Data] Keep this script next to the MDPro3 project folder.
  set "EXIT_CODE=1"
  goto FINISH
)

call :NEED_GIT
if errorlevel 1 goto FINISH

git -C "%PROJECT_DIR%" rev-parse --show-toplevel >nul 2>nul
if errorlevel 1 (
  echo [Data] This folder is not a git repository: %PROJECT_DIR%
  set "EXIT_CODE=1"
  goto FINISH
)

call :CHECK_DATA_CLEAN
if errorlevel 1 goto FINISH

echo [Data] Repository: %PROJECT_DIR%
echo [Data] Fetching origin...
git -C "%PROJECT_DIR%" fetch --prune origin
if errorlevel 1 (
  echo [Data] Fetch failed.
  set "EXIT_CODE=1"
  goto FINISH
)

echo [Data] Updating Data from origin/master...
git -C "%PROJECT_DIR%" checkout origin/master -- Data
if errorlevel 1 (
  echo [Data] Failed to update Data from origin/master.
  set "EXIT_CODE=1"
  goto FINISH
)

git -C "%PROJECT_DIR%" reset -- Data >nul 2>nul

call :ENSURE_ROOT_CDB "%PROJECT_DIR%\Data"
if errorlevel 1 goto FINISH

call :REFRESH_CID_CACHE

if "%SYNC_BUILD%"=="1" (
  call :SYNC_BUILD_DATA
  if errorlevel 1 goto FINISH
) else (
  echo [Data] Build folder sync skipped.
)

echo.
echo [Data] Done.
echo [Data] Updated card databases, scripts, and the Data folder from origin/master.
echo [Data] This script never pushes to the remote repository.
goto FINISH

:NEED_GIT
where git >nul 2>nul
if errorlevel 1 (
  echo [Data] Git was not found in PATH.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:CHECK_DATA_CLEAN
set "HAS_DATA_CHANGES=0"
git -C "%PROJECT_DIR%" diff --quiet -- Data
if errorlevel 1 set "HAS_DATA_CHANGES=1"
git -C "%PROJECT_DIR%" diff --cached --quiet -- Data
if errorlevel 1 set "HAS_DATA_CHANGES=1"
if "!HAS_DATA_CHANGES!"=="1" (
  echo [Data] Tracked local changes exist under Data.
  echo [Data] Please stash or commit those Data changes before running this updater.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:ENSURE_ROOT_CDB
set "DATA_DIR=%~1"
set "CONFIG_LANG=zh-CN"
if exist "%DATA_DIR%\config.conf" (
  for /f "tokens=2 delims=^>" %%L in ('findstr /B "Language-" "%DATA_DIR%\config.conf"') do set "CONFIG_LANG=%%L"
)
set "LANG_CDB=%DATA_DIR%\locales\!CONFIG_LANG!\cards.cdb"
if not exist "!LANG_CDB!" set "LANG_CDB=%DATA_DIR%\locales\zh-CN\cards.cdb"
if not exist "!LANG_CDB!" (
  echo [Data] Missing locale cards database: !LANG_CDB!
  set "EXIT_CODE=1"
  exit /b 1
)
echo [Data] Refresh Data\cards.cdb from locale: !CONFIG_LANG!
copy /Y "!LANG_CDB!" "%DATA_DIR%\cards.cdb" >nul
if errorlevel 1 (
  echo [Data] Failed to refresh Data\cards.cdb.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:SYNC_BUILD_DATA
if not exist "%BUILD_DIR%" (
  echo [Data] Build folder not found. Skip build sync: %BUILD_DIR%
  exit /b 0
)
echo [Data] Sync Data to build folder...
robocopy "%PROJECT_DIR%\Data" "%BUILD_DIR%\Data" /MIR /XJ /XD .git /XF %LOCAL_ONLY_FILES% /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if !ROBOCOPY_CODE! GEQ 8 (
  echo [Data] Robocopy failed with code !ROBOCOPY_CODE!.
  set "EXIT_CODE=!ROBOCOPY_CODE!"
  exit /b !ROBOCOPY_CODE!
)
call :COPY_LOCAL_ONLY_FILES
if errorlevel 1 exit /b 1
exit /b 0

rem Keeps Data\cards_Lite.json, the merged CID table, in step with cards_Alt.json.
rem The helper walks away from the file when no base table is left to build on.
:REFRESH_CID_CACHE
if not exist "%ROOT%\UpdateCidCache.ps1" exit /b 0
where powershell >nul 2>nul
if errorlevel 1 (
  echo [Data] PowerShell was not found. Data\cards_Lite.json is left as it is.
  exit /b 0
)
set "CID_BUILD_ARG="
if "%SYNC_BUILD%"=="1" if exist "%BUILD_DIR%\Data" set "CID_BUILD_ARG=-BuildDir "%BUILD_DIR%""
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\UpdateCidCache.ps1" -Root "%PROJECT_DIR%" %CID_BUILD_ARG%
if errorlevel 1 echo [Data] Data\cards_Lite.json was not refreshed. The cache on disk stays as it is.
exit /b 0

rem Hands the excluded files over to the build folder, but only where the build has
rem no copy of its own, so live build data never gets overwritten by the source.
:COPY_LOCAL_ONLY_FILES
for %%F in (%LOCAL_ONLY_FILES%) do (
  if exist "%PROJECT_DIR%\Data\%%~F" (
    if not exist "%BUILD_DIR%\Data\%%~F" (
      copy /Y "%PROJECT_DIR%\Data\%%~F" "%BUILD_DIR%\Data\%%~F" >nul
      if errorlevel 1 (
        echo [Data] Failed to copy %%~F into the build folder.
        exit /b 1
      )
    )
  )
)
exit /b 0

:SHOW_HELP
echo Update_02_Data.bat [options]
echo.
echo Updates the Data folder from the MDPro3 project repository in the MDPro3 subfolder.
echo This includes card databases under Data\locales, Data\script.zip, and other tracked Data files.
echo It also refreshes Data\cards.cdb from the configured locale and syncs the Build\MDPro3\Data folder next to this script.
echo Data\cards_Lite.json is refreshed from cards_Alt.json rather than dropped.
echo Data files the game writes into the build folder are kept out of the sync.
echo It does not push anything.
echo.
echo Options:
echo   --skip-build-sync  Update source Data only.
echo   --no-pause         Do not pause before exiting.
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
