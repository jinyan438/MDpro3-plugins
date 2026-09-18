@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "REPO_DIR=%ROOT%\ygopro2-closeup"
set "REMOTE_URL=https://code.moenext.com/mycard/ygopro2-closeup.git"
set "SOURCE_DIR=%REPO_DIR%\closeup"
set "JUNCTION_PATH=%PROJECT_DIR%\Picture\Closeup"
set "BUILD_TARGET=%ROOT%\Build\MDPro3\Picture\Closeup"
set "PAUSE_ON_EXIT=1"
set "SYNC_BUILD=1"
set "EXIT_CODE=0"
set "STASH_CREATED=0"

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
echo [Closeup] Unknown option: %~1
set "EXIT_CODE=1"
goto FINISH

:AFTER_PARSE_ARGS
call :NEED_GIT
if errorlevel 1 goto FINISH

if not exist "%PROJECT_DIR%\.git" (
  echo [Closeup] Main project repository not found: %PROJECT_DIR%
  echo [Closeup] Keep this script next to the MDPro3 project folder.
  set "EXIT_CODE=1"
  goto FINISH
)

call :UPDATE_REPO
if errorlevel 1 goto FINISH

call :ENSURE_JUNCTION "%JUNCTION_PATH%" "%SOURCE_DIR%"
if errorlevel 1 goto FINISH

if "%SYNC_BUILD%"=="1" (
  call :SYNC_BUILD_FOLDER
  if errorlevel 1 goto FINISH
) else (
  echo [Closeup] Build folder sync skipped.
)

for /f "delims=" %%H in ('git -C "%REPO_DIR%" rev-parse --short HEAD') do set "NEW_HEAD=%%H"
echo.
echo [Closeup] Done. HEAD: !NEW_HEAD!
echo [Closeup] This script never pushes to the remote repository.
goto FINISH

:NEED_GIT
where git >nul 2>nul
if errorlevel 1 (
  echo [Closeup] Git was not found in PATH.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:UPDATE_REPO
if not exist "%REPO_DIR%\.git" (
  if exist "%REPO_DIR%" (
    echo [Closeup] Folder exists but is not a git repo: %REPO_DIR%
    set "EXIT_CODE=1"
    exit /b 1
  )
  echo [Closeup] Cloning %REMOTE_URL%
  git clone --depth=1 "%REMOTE_URL%" "%REPO_DIR%"
  if errorlevel 1 (
    echo [Closeup] Clone failed.
    set "EXIT_CODE=1"
    exit /b 1
  )
  exit /b 0
)

echo [Closeup] Repository: %REPO_DIR%
call :STASH_TRACKED
if errorlevel 1 exit /b 1

echo [Closeup] Fetching origin...
git -C "%REPO_DIR%" fetch --prune origin
if errorlevel 1 (
  echo [Closeup] Fetch failed.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  exit /b 1
)

git -C "%REPO_DIR%" checkout master
if errorlevel 1 (
  echo [Closeup] Could not checkout master.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  exit /b 1
)

echo [Closeup] Fast-forwarding to origin/master...
git -C "%REPO_DIR%" merge --ff-only origin/master
if errorlevel 1 (
  echo [Closeup] Fast-forward failed.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  exit /b 1
)

call :POP_STASH_IF_NEEDED
if errorlevel 1 exit /b 1
exit /b 0

:STASH_TRACKED
set "HAS_TRACKED=0"
git -C "%REPO_DIR%" diff --quiet
if errorlevel 1 set "HAS_TRACKED=1"
git -C "%REPO_DIR%" diff --cached --quiet
if errorlevel 1 set "HAS_TRACKED=1"
if "!HAS_TRACKED!"=="1" (
  echo [Closeup] Saving local tracked changes to a temporary stash...
  git -C "%REPO_DIR%" stash push -m "auto-update-closeup-arts"
  if errorlevel 1 (
    echo [Closeup] Failed to create stash.
    set "EXIT_CODE=1"
    exit /b 1
  )
  set "STASH_CREATED=1"
)
exit /b 0

:POP_STASH_IF_NEEDED
if "%STASH_CREATED%"=="1" (
  echo [Closeup] Restoring local tracked changes from stash...
  git -C "%REPO_DIR%" stash pop
  if errorlevel 1 (
    echo [Closeup] Could not restore the temporary stash cleanly.
    echo [Closeup] The stash is kept by git. Resolve conflicts or run: git -C "%REPO_DIR%" stash list
    set "EXIT_CODE=1"
    exit /b 1
  )
  set "STASH_CREATED=0"
)
exit /b 0

:ENSURE_JUNCTION
set "LINK_PATH=%~1"
set "TARGET_PATH=%~2"
if not exist "%TARGET_PATH%" (
  echo [Closeup] Missing source folder: %TARGET_PATH%
  set "EXIT_CODE=1"
  exit /b 1
)
if exist "%LINK_PATH%" exit /b 0
for %%P in ("%LINK_PATH%") do if not exist "%%~dpP" mkdir "%%~dpP" >nul 2>nul
echo [Closeup] Creating junction: %LINK_PATH% -^> %TARGET_PATH%
mklink /J "%LINK_PATH%" "%TARGET_PATH%"
if errorlevel 1 (
  echo [Closeup] Failed to create junction.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:SYNC_BUILD_FOLDER
if not exist "%ROOT%\Build\MDPro3" (
  echo [Closeup] Build folder not found. Skip build sync.
  exit /b 0
)
echo [Closeup] Sync closeup card art to build folder...
robocopy "%SOURCE_DIR%" "%BUILD_TARGET%" /MIR /XJ /XD .git /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if !ROBOCOPY_CODE! GEQ 8 (
  echo [Closeup] Robocopy failed with code !ROBOCOPY_CODE!.
  set "EXIT_CODE=!ROBOCOPY_CODE!"
  exit /b !ROBOCOPY_CODE!
)
exit /b 0

:SHOW_HELP
echo Update_04_Closeup_Arts.bat [options]
echo.
echo Updates the closeup card art repository:
echo   %REMOTE_URL%
echo It also keeps Picture\Closeup pointed at ygopro2-closeup\closeup and syncs Build\MDPro3\Picture\Closeup when a build exists.
echo It does not push anything.
echo.
echo Options:
echo   --skip-build-sync  Update source repository only.
echo   --no-pause         Do not pause before exiting.
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
