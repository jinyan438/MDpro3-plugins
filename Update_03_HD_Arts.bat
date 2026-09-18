@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "REPO_DIR=%ROOT%\hd-arts"
set "REMOTE_URL=https://code.moenext.com/mycard/hd-arts.git"
set "SOURCE_DIR=%REPO_DIR%\arts"
set "JUNCTION_PATH=%PROJECT_DIR%\Picture\Art"
set "BUILD_TARGET=%ROOT%\Build\MDPro3\Picture\Art"
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
echo [HD-Arts] Unknown option: %~1
set "EXIT_CODE=1"
goto FINISH

:AFTER_PARSE_ARGS
call :NEED_GIT
if errorlevel 1 goto FINISH

if not exist "%PROJECT_DIR%\.git" (
  echo [HD-Arts] Main project repository not found: %PROJECT_DIR%
  echo [HD-Arts] Keep this script next to the MDPro3 project folder.
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
  echo [HD-Arts] Build folder sync skipped.
)

for /f "delims=" %%H in ('git -C "%REPO_DIR%" rev-parse --short HEAD') do set "NEW_HEAD=%%H"
echo.
echo [HD-Arts] Done. HEAD: !NEW_HEAD!
echo [HD-Arts] This script never pushes to the remote repository.
goto FINISH

:NEED_GIT
where git >nul 2>nul
if errorlevel 1 (
  echo [HD-Arts] Git was not found in PATH.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:UPDATE_REPO
if not exist "%REPO_DIR%\.git" (
  if exist "%REPO_DIR%" (
    echo [HD-Arts] Folder exists but is not a git repo: %REPO_DIR%
    set "EXIT_CODE=1"
    exit /b 1
  )
  echo [HD-Arts] Cloning %REMOTE_URL%
  git clone --depth=1 "%REMOTE_URL%" "%REPO_DIR%"
  if errorlevel 1 (
    echo [HD-Arts] Clone failed.
    set "EXIT_CODE=1"
    exit /b 1
  )
  exit /b 0
)

echo [HD-Arts] Repository: %REPO_DIR%
call :STASH_TRACKED
if errorlevel 1 exit /b 1

echo [HD-Arts] Fetching origin...
git -C "%REPO_DIR%" fetch --prune origin
if errorlevel 1 (
  echo [HD-Arts] Fetch failed.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  exit /b 1
)

git -C "%REPO_DIR%" checkout master
if errorlevel 1 (
  echo [HD-Arts] Could not checkout master.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  exit /b 1
)

echo [HD-Arts] Fast-forwarding to origin/master...
git -C "%REPO_DIR%" merge --ff-only origin/master
if errorlevel 1 (
  echo [HD-Arts] Fast-forward failed.
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
  echo [HD-Arts] Saving local tracked changes to a temporary stash...
  git -C "%REPO_DIR%" stash push -m "auto-update-hd-arts"
  if errorlevel 1 (
    echo [HD-Arts] Failed to create stash.
    set "EXIT_CODE=1"
    exit /b 1
  )
  set "STASH_CREATED=1"
)
exit /b 0

:POP_STASH_IF_NEEDED
if "%STASH_CREATED%"=="1" (
  echo [HD-Arts] Restoring local tracked changes from stash...
  git -C "%REPO_DIR%" stash pop
  if errorlevel 1 (
    echo [HD-Arts] Could not restore the temporary stash cleanly.
    echo [HD-Arts] The stash is kept by git. Resolve conflicts or run: git -C "%REPO_DIR%" stash list
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
  echo [HD-Arts] Missing source folder: %TARGET_PATH%
  set "EXIT_CODE=1"
  exit /b 1
)
if exist "%LINK_PATH%" exit /b 0
for %%P in ("%LINK_PATH%") do if not exist "%%~dpP" mkdir "%%~dpP" >nul 2>nul
echo [HD-Arts] Creating junction: %LINK_PATH% -^> %TARGET_PATH%
mklink /J "%LINK_PATH%" "%TARGET_PATH%"
if errorlevel 1 (
  echo [HD-Arts] Failed to create junction.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

:SYNC_BUILD_FOLDER
if not exist "%ROOT%\Build\MDPro3" (
  echo [HD-Arts] Build folder not found. Skip build sync.
  exit /b 0
)
echo [HD-Arts] Sync HD card art to build folder...
robocopy "%SOURCE_DIR%" "%BUILD_TARGET%" /MIR /XJ /XD .git /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if !ROBOCOPY_CODE! GEQ 8 (
  echo [HD-Arts] Robocopy failed with code !ROBOCOPY_CODE!.
  set "EXIT_CODE=!ROBOCOPY_CODE!"
  exit /b !ROBOCOPY_CODE!
)
exit /b 0

:SHOW_HELP
echo Update_03_HD_Arts.bat [options]
echo.
echo Updates the HD card art repository:
echo   %REMOTE_URL%
echo It also keeps Picture\Art pointed at hd-arts\arts and syncs Build\MDPro3\Picture\Art when a build exists.
echo It does not push anything.
echo.
echo Options:
echo   --skip-build-sync  Update source repository only.
echo   --no-pause         Do not pause before exiting.
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
