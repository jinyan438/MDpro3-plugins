@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ---------------------------------------------------------------------------
rem Update_01_MainRepo.bat
rem
rem Fast-forwards the local MDPro3 main repository to origin/master, then puts
rem the local tracked edits back into the working tree.
rem
rem Unity and the Addressables package rewrite a few build artifacts every time
rem the editor builds the project (addressables_content_state.bin, link.xml,
rem link.xml.meta). Those files are binary or machine generated, so replaying
rem them from a stash on top of an upstream update can only end in a conflict
rem (binary conflict / modify-delete conflict). They are therefore kept out of
rem the stash and reset to the upstream revision instead.
rem
rem This script sits in the container folder that holds every repository as its
rem own subfolder. The MDPro3 project repository is the MDPro3 subfolder below,
rem so the excluded paths are resolved from there.
rem ---------------------------------------------------------------------------

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "PAUSE_ON_EXIT=1"
set "EXIT_CODE=0"
set "STASH_CREATED=0"
set "UPSTREAM_REF=origin/master"

set "GEN1=Assets/AddressableAssetsData/Android/addressables_content_state.bin"
set "GEN2=Assets/AddressableAssetsData/Windows/addressables_content_state.bin"
set "GEN3=Assets/AddressableAssetsData/link.xml"
set "GEN4=Assets/AddressableAssetsData/link.xml.meta"

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--no-pause" (
  set "PAUSE_ON_EXIT=0"
  shift
  goto PARSE_ARGS
)
if /I "%~1"=="--help" goto SHOW_HELP
echo [MainRepo] Unknown option: %~1
set "EXIT_CODE=1"
goto FINISH

:AFTER_PARSE_ARGS
if not exist "%PROJECT_DIR%\.git" (
  echo [MainRepo] Main project repository not found: %PROJECT_DIR%
  echo [MainRepo] Keep this script next to the MDPro3 project folder.
  set "EXIT_CODE=1"
  goto FINISH
)

call :NEED_GIT
if errorlevel 1 goto FINISH

git -C "%PROJECT_DIR%" rev-parse --show-toplevel >nul 2>nul
if errorlevel 1 (
  echo [MainRepo] This folder is not a git repository: %PROJECT_DIR%
  set "EXIT_CODE=1"
  goto FINISH
)

for /f "delims=" %%H in ('git -C "%PROJECT_DIR%" rev-parse --short HEAD') do set "OLD_HEAD=%%H"
for /f "delims=" %%B in ('git -C "%PROJECT_DIR%" rev-parse --abbrev-ref HEAD') do set "BRANCH=%%B"

echo [MainRepo] Repository: %PROJECT_DIR%
echo [MainRepo] Branch: !BRANCH!
echo [MainRepo] Old HEAD: !OLD_HEAD!
if /I not "!BRANCH!"=="master" echo [MainRepo] Note: this script always updates from !UPSTREAM_REF!.

call :RESET_GENERATED
call :STASH_TRACKED
if errorlevel 1 goto FINISH

echo [MainRepo] Fetching !UPSTREAM_REF!...
git -C "%PROJECT_DIR%" fetch --prune origin
if errorlevel 1 (
  echo [MainRepo] Fetch failed.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  goto FINISH
)

echo [MainRepo] Fast-forwarding to !UPSTREAM_REF!...
git -C "%PROJECT_DIR%" merge --ff-only !UPSTREAM_REF!
if errorlevel 1 (
  echo [MainRepo] Fast-forward failed. Local branch may have diverged from !UPSTREAM_REF!.
  set "EXIT_CODE=1"
  call :POP_STASH_IF_NEEDED
  goto FINISH
)

call :POP_STASH_IF_NEEDED
if errorlevel 1 goto FINISH

for /f "delims=" %%H in ('git -C "%PROJECT_DIR%" rev-parse --short HEAD') do set "NEW_HEAD=%%H"
echo.
echo [MainRepo] Done: !OLD_HEAD! -^> !NEW_HEAD!
echo [MainRepo] Local tracked edits are back in the working tree.
echo [MainRepo] This script never pushes to the remote repository.
goto FINISH

:NEED_GIT
where git >nul 2>nul
if errorlevel 1 (
  echo [MainRepo] Git was not found in PATH.
  set "EXIT_CODE=1"
  exit /b 1
)
exit /b 0

rem Sets %~2 to 1 when %~1 is one of the generated Unity/Addressables artifacts.
:IS_GENERATED
set "%~2=0"
if /I "%~1"=="%GEN1%" set "%~2=1"
if /I "%~1"=="%GEN2%" set "%~2=1"
if /I "%~1"=="%GEN3%" set "%~2=1"
if /I "%~1"=="%GEN4%" set "%~2=1"
exit /b 0

rem Throws away local edits to the generated artifacts so the fast-forward can
rem never be blocked by them. Paths that upstream removed are left alone.
:RESET_GENERATED
for %%P in ("%GEN1%" "%GEN2%" "%GEN3%" "%GEN4%") do (
  set "IN_HEAD=0"
  for /f "delims=" %%T in ('git -C "%PROJECT_DIR%" ls-tree -r --name-only HEAD -- "%%~P" 2^>nul') do set "IN_HEAD=1"
  if "!IN_HEAD!"=="1" (
    git -C "%PROJECT_DIR%" diff --quiet HEAD -- "%%~P"
    if errorlevel 1 (
      echo [MainRepo] Resetting generated artifact to HEAD: %%~P
      git -C "%PROJECT_DIR%" checkout HEAD -- "%%~P" >nul 2>nul
    )
  )
)
exit /b 0

:STASH_TRACKED
set "HAS_TRACKED=0"
git -C "%PROJECT_DIR%" diff --quiet
if errorlevel 1 set "HAS_TRACKED=1"
git -C "%PROJECT_DIR%" diff --cached --quiet
if errorlevel 1 set "HAS_TRACKED=1"
if not "!HAS_TRACKED!"=="1" exit /b 0

echo [MainRepo] Saving local tracked changes to a temporary stash...
set "STASH_BEFORE="
for /f "delims=" %%S in ('git -C "%PROJECT_DIR%" rev-parse -q --verify refs/stash') do set "STASH_BEFORE=%%S"
git -C "%PROJECT_DIR%" stash push -m "auto-update-mainrepo-before-fetch" -- . ":(exclude)%GEN1%" ":(exclude)%GEN2%" ":(exclude)%GEN3%" ":(exclude)%GEN4%"
if errorlevel 1 (
  echo [MainRepo] Failed to create stash.
  set "EXIT_CODE=1"
  exit /b 1
)
set "STASH_AFTER="
for /f "delims=" %%S in ('git -C "%PROJECT_DIR%" rev-parse -q --verify refs/stash') do set "STASH_AFTER=%%S"
if not "!STASH_AFTER!"=="!STASH_BEFORE!" set "STASH_CREATED=1"
if "!STASH_CREATED!"=="0" echo [MainRepo] Nothing left to stash after skipping generated artifacts.
exit /b 0

:POP_STASH_IF_NEEDED
if not "!STASH_CREATED!"=="1" exit /b 0
echo [MainRepo] Restoring local tracked changes from stash...
git -C "%PROJECT_DIR%" stash pop
if errorlevel 1 (
  echo [MainRepo] Stash pop could not be applied cleanly.
  call :RESOLVE_GENERATED_CONFLICTS
  if errorlevel 1 (
    echo [MainRepo] These paths need a manual decision:
    git -C "%PROJECT_DIR%" diff --name-only --diff-filter=U
    echo [MainRepo] Edit them, then run: git add ^<file^> and git stash drop
    set "EXIT_CODE=1"
    exit /b 1
  )
  echo [MainRepo] Generated artifacts were resolved with the upstream revision.
  call :RESTORE_INDEX_SHAPE
  git -C "%PROJECT_DIR%" stash drop >nul 2>nul
)
set "STASH_CREATED=0"
exit /b 0

rem Auto-resolves conflicted paths that are generated artifacts, and reports
rem failure when a hand written source file is conflicted instead.
:RESOLVE_GENERATED_CONFLICTS
set "NEEDS_MANUAL="
for /f "delims=" %%F in ('git -C "%PROJECT_DIR%" diff --name-only --diff-filter=U') do (
  set "IS_GEN=0"
  call :IS_GENERATED "%%F" IS_GEN
  if "!IS_GEN!"=="1" (
    set "IN_HEAD=0"
    for /f "delims=" %%T in ('git -C "%PROJECT_DIR%" ls-tree -r --name-only HEAD -- "%%F" 2^>nul') do set "IN_HEAD=1"
    if "!IN_HEAD!"=="1" (
      echo [MainRepo]   keeping upstream revision: %%F
      git -C "%PROJECT_DIR%" checkout --ours -- "%%F" >nul 2>nul
      git -C "%PROJECT_DIR%" add -- "%%F" >nul 2>nul
    ) else (
      echo [MainRepo]   accepting upstream deletion: %%F
      git -C "%PROJECT_DIR%" rm -f --quiet -- "%%F" >nul 2>nul
    )
  ) else (
    set "NEEDS_MANUAL=!NEEDS_MANUAL! %%F"
  )
)
if not "!NEEDS_MANUAL!"=="" exit /b 1
exit /b 0

rem A conflicted stash pop stages every cleanly merged file. When the stash held
rem no staged changes to begin with, unstage again so the working tree looks the
rem way it did before the script started.
:RESTORE_INDEX_SHAPE
git -C "%PROJECT_DIR%" diff --quiet "refs/stash^" "refs/stash^2"
if errorlevel 1 exit /b 0
echo [MainRepo] Unstaging auto-merged files, the stash held no staged changes.
git -C "%PROJECT_DIR%" reset -q
exit /b 0

:SHOW_HELP
echo Update_01_MainRepo.bat [options]
echo.
echo Updates the MDPro3 project repository in the MDPro3 subfolder from origin/master
echo with a fast-forward merge.
echo It stashes tracked local changes before updating and restores them afterwards.
echo Generated Unity/Addressables artifacts are excluded from the stash and reset to
echo the upstream revision, so binary or modify-delete conflicts cannot occur.
echo It does not push anything.
echo.
echo Options:
echo   --no-pause  Do not pause before exiting.
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
