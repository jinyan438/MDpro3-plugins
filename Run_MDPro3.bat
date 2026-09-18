@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "UNITY_VERSION=6000.0.24f1"
set "UNITY_CHANGESET=11fa355cd605"
set "UNITY_EXE="
set "UNITY_HUB="
set "LOG_DIR=%PROJECT_DIR%\Logs"
set "UNITY_LOG=%LOG_DIR%\UnityOpen.log"

rem ---------------------------------------------------------------------------
rem Plugins (see plugins\README.md)
rem
rem Sources:  <root>\plugins\MDPro3Plugins
rem Copied to: <project>\Assets\MDPro3Plugins  (the only folder Unity compiles into
rem            the game assembly Assembly-CSharp, so the plugin can use the game API)
rem
rem This script syncs the plugin, rebuilds the built player when the plugin changed
rem and then starts the game as before. Nothing outside plugins\ and this file is
rem edited: the copied folder inside the Unity project is generated and can be
rem removed again with:  Run_MDPro3.bat --plugin-off
rem ---------------------------------------------------------------------------
set "PLUGIN_DIR=%ROOT%\plugins"
set "PLUGIN_SRC=%PLUGIN_DIR%\MDPro3Plugins"
set "PLUGIN_DST=%PROJECT_DIR%\Assets\MDPro3Plugins"
set "PLUGIN_STATE=%PLUGIN_DIR%\.state"
set "PLUGIN_STATE_SCRIPT=%PLUGIN_DIR%\tools\plugin-state.ps1"
set "PLUGIN_DIAGNOSE_SCRIPT=%PLUGIN_DIR%\tools\plugin-diagnose.ps1"
set "POWERSHELL_EXE=powershell.exe"
set "PLUGIN_STATUS="
set "PLUGIN_MESSAGE="
set "PLUGIN_FEATURES="
set "PLUGIN_CONFIG="
set "PLUGIN_OFF=0"
set "PLUGIN_SKIP_BUILD=0"
set "PLUGIN_FORCE_BUILD=0"
set "PLUGIN_DIAGNOSE=0"
set "BUILT_EXE="
set "BUILT_DIR="
set "EXIT_CODE="

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--check" goto CHECK_ONLY
if /I "%~1"=="--help" goto SHOW_HELP
if /I "%~1"=="-h" goto SHOW_HELP
if /I "%~1"=="--rebuild" set "PLUGIN_FORCE_BUILD=1"
if /I "%~1"=="--no-build" set "PLUGIN_SKIP_BUILD=1"
if /I "%~1"=="--skip-build" set "PLUGIN_SKIP_BUILD=1"
if /I "%~1"=="--plugin-off" set "PLUGIN_OFF=1"
if /I "%~1"=="--diagnose" set "PLUGIN_DIAGNOSE=1"
shift
goto PARSE_ARGS

:AFTER_PARSE_ARGS
echo [MDPro3] Project dir: %PROJECT_DIR%
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>nul

if not exist "%PROJECT_DIR%\Assets" (
  echo.
  echo [MDPro3] Unity project folder not found: %PROJECT_DIR%
  echo [MDPro3] Keep this script next to the MDPro3 project folder.
  exit /b 1
)

call :FIND_BUILT_GAME
call :SYNC_PLUGIN

if defined PLUGIN_MESSAGE echo [MDPro3] %PLUGIN_MESSAGE%
if defined PLUGIN_FEATURES (
  if defined PLUGIN_CONFIG (
    echo [MDPro3] Features: %PLUGIN_FEATURES%   ^(%PLUGIN_CONFIG%^)
  ) else (
    echo [MDPro3] Features: %PLUGIN_FEATURES%
  )
)
if /I "%PLUGIN_STATUS%"=="ERROR" (
  echo [MDPro3] Continuing without plugin handling.
  echo [MDPro3] See plugins\README.md.
)

set "PLUGIN_NEEDS_BUILD=0"
if /I "%PLUGIN_STATUS%"=="CHANGED" set "PLUGIN_NEEDS_BUILD=1"
if "%PLUGIN_FORCE_BUILD%"=="1" set "PLUGIN_NEEDS_BUILD=1"
rem With --plugin-off the game is rebuilt without the plugin, so it runs the original behaviour again.
if "%PLUGIN_OFF%"=="1" set "PLUGIN_NEEDS_BUILD=1"
if "%PLUGIN_SKIP_BUILD%"=="1" (
  if "%PLUGIN_NEEDS_BUILD%"=="1" echo [MDPro3] Plugin rebuild skipped: --no-build
  set "PLUGIN_NEEDS_BUILD=0"
)

if "%PLUGIN_NEEDS_BUILD%"=="1" call :REBUILD_WITH_PLUGIN

call :FIND_BUILT_GAME
if not defined BUILT_EXE goto OPEN_PROJECT

if "%PLUGIN_DIAGNOSE%"=="1" (
  call :RUN_PLUGIN_DIAGNOSE
  goto END_OK
)

echo [MDPro3] Built game found: %BUILT_EXE%
start "MDPro3" /D "%BUILT_DIR%" "%BUILT_EXE%"
goto END_OK

:REBUILD_WITH_PLUGIN
echo.
if not exist "%ROOT%\Build_MDPro3_Windows64.bat" (
  echo [MDPro3] Build_MDPro3_Windows64.bat was not found, the plugin cannot be built into the game.
  echo [MDPro3] The game keeps running without the current plugin sources.
  exit /b 0
)
if exist "%PROJECT_DIR%\Temp\UnityLockfile" (
  echo [MDPro3] The Unity editor currently has this project open, so the game cannot be rebuilt now.
  echo [MDPro3] Close the editor and start this file again, or press Play inside the editor:
  echo [MDPro3] the plugin is loaded there as well.
  exit /b 0
)
call :WAIT_FOR_BUILD_FILES
if errorlevel 1 exit /b 0
echo [MDPro3] Plugin sources changed, rebuilding the game. This can take a few minutes...
call "%ROOT%\Build_MDPro3_Windows64.bat" --no-pause
if errorlevel 1 (
  echo.
  echo [MDPro3] The rebuild failed. The game is started with the last successfully built player.
  echo [MDPro3] A running game or its crash handler locks the built files, close everything and try again.
  echo [MDPro3] Unity log: %LOG_DIR%\UnityBuild_Windows64.log
  exit /b 0
)
if "%PLUGIN_OFF%"=="1" (
  call :STAMP_PLUGIN_OFF
  echo [MDPro3] Rebuild finished, the game runs without the plugin again.
) else (
  call :STAMP_PLUGIN
  echo [MDPro3] Rebuild finished, the plugin is part of the game now.
)
exit /b 0

:WAIT_FOR_BUILD_FILES
set "PLUGIN_WAIT_STATUS="
if not exist "%PLUGIN_STATE_SCRIPT%" exit /b 0
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PLUGIN_STATE_SCRIPT%" ^
  -Action wait -Source "%PLUGIN_SRC%" -BuiltExe "%BUILT_EXE%" -TimeoutSeconds 30 ^
  > "%PLUGIN_STATE%\plugin-wait.txt" 2>&1
if exist "%PLUGIN_STATE%\plugin-wait.txt" (
  for /f "usebackq tokens=1,* delims==" %%A in ("%PLUGIN_STATE%\plugin-wait.txt") do (
    if /I "%%A"=="PLUGINSTATUS" set "PLUGIN_WAIT_STATUS=%%B"
    if /I "%%A"=="PLUGINMESSAGE" echo [MDPro3] %%B
  )
)
if /I "%PLUGIN_WAIT_STATUS%"=="LOCKED" (
  echo [MDPro3] Close the running game and start this file again to get the plugin update.
  exit /b 1
)
exit /b 0

:SYNC_PLUGIN
set "PLUGIN_STATUS="
set "PLUGIN_MESSAGE="
if not exist "%PLUGIN_STATE_SCRIPT%" (
  set "PLUGIN_STATUS=ERROR"
  set "PLUGIN_MESSAGE=Plugin helper not found: %PLUGIN_STATE_SCRIPT%"
  exit /b 0
)
if not exist "%PLUGIN_SRC%" (
  set "PLUGIN_STATUS=ERROR"
  set "PLUGIN_MESSAGE=Plugin sources not found: %PLUGIN_SRC%"
  exit /b 0
)
if not exist "%PLUGIN_STATE%" mkdir "%PLUGIN_STATE%" >nul 2>nul

set "PLUGIN_OFF_ARG="
if "%PLUGIN_OFF%"=="1" set "PLUGIN_OFF_ARG=-PluginOff"

"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PLUGIN_STATE_SCRIPT%" ^
  -Action sync ^
  -Source "%PLUGIN_SRC%" ^
  -Target "%PLUGIN_DST%" ^
  -State "%PLUGIN_STATE%" ^
  -BuiltExe "%BUILT_EXE%" ^
  %PLUGIN_OFF_ARG% > "%PLUGIN_STATE%\plugin-state.txt" 2>&1

if exist "%PLUGIN_STATE%\plugin-state.txt" (
  for /f "usebackq tokens=1,* delims==" %%A in ("%PLUGIN_STATE%\plugin-state.txt") do (
    if /I "%%A"=="PLUGINSTATUS" set "PLUGIN_STATUS=%%B"
    if /I "%%A"=="PLUGINMESSAGE" set "PLUGIN_MESSAGE=%%B"
    if /I "%%A"=="PLUGINFEATURES" set "PLUGIN_FEATURES=%%B"
    if /I "%%A"=="PLUGINCONFIG" set "PLUGIN_CONFIG=%%B"
    if /I "%%A"=="PLUGINCONFIGERROR" echo [MDPro3] %%B
    if /I "%%A"=="PLUGINCONFIGMESSAGE" echo [MDPro3] %%B
  )
)
if not defined PLUGIN_STATUS (
  set "PLUGIN_STATUS=ERROR"
  set "PLUGIN_MESSAGE=The plugin helper produced no result, see %PLUGIN_STATE%\plugin-state.txt"
)
exit /b 0

:STAMP_PLUGIN
if not exist "%PLUGIN_STATE_SCRIPT%" exit /b 0
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PLUGIN_STATE_SCRIPT%" ^
  -Action stamp -Source "%PLUGIN_SRC%" -State "%PLUGIN_STATE%" > "%PLUGIN_STATE%\plugin-stamp.txt" 2>&1
exit /b 0

:STAMP_PLUGIN_OFF
if not exist "%PLUGIN_STATE_SCRIPT%" exit /b 0
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PLUGIN_STATE_SCRIPT%" ^
  -Action stamp -Source "%PLUGIN_SRC%" -State "%PLUGIN_STATE%" -Signature off > "%PLUGIN_STATE%\plugin-stamp.txt" 2>&1
exit /b 0

:RUN_PLUGIN_DIAGNOSE
if not exist "%PLUGIN_DIAGNOSE_SCRIPT%" (
  echo [MDPro3] Diagnose script not found: %PLUGIN_DIAGNOSE_SCRIPT%
  exit /b 0
)
echo.
echo [MDPro3] Running the plugin self test inside the built game, please wait...
set "PLUGIN_DIAGNOSE_STATUS="
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PLUGIN_DIAGNOSE_SCRIPT%" ^
  -Exe "%BUILT_EXE%" -LogPath "%PLUGIN_STATE%\player-selftest.log" > "%PLUGIN_STATE%\plugin-diagnose.txt" 2>&1

if exist "%PLUGIN_STATE%\plugin-diagnose.txt" (
  for /f "usebackq tokens=1,* delims==" %%A in ("%PLUGIN_STATE%\plugin-diagnose.txt") do (
    if /I "%%A"=="PLUGINDIAGNOSE" set "PLUGIN_DIAGNOSE_STATUS=%%B"
    if /I "%%A"=="PLUGINMESSAGE" echo [MDPro3] %%B
    if /I "%%A"=="PLUGINDETAIL" echo   %%B
  )
)
if not defined PLUGIN_DIAGNOSE_STATUS set "PLUGIN_DIAGNOSE_STATUS=ERROR"

echo.
echo [MDPro3] Plugin self test: %PLUGIN_DIAGNOSE_STATUS%
if /I "%PLUGIN_DIAGNOSE_STATUS%"=="PASS" (
  set "EXIT_CODE=0"
) else (
  set "EXIT_CODE=1"
)
exit /b 0

:OPEN_PROJECT
call :FIND_UNITY
if defined UNITY_EXE goto OPEN_UNITY
goto UNITY_NOT_FOUND

:OPEN_UNITY
for %%P in ("%UNITY_EXE%") do set "UNITY_EDITOR_DIR=%%~dpP"
set "UPM_EXE=%UNITY_EDITOR_DIR%Data\Resources\PackageManager\Server\UnityPackageManager.exe"
if not exist "%UPM_EXE%" goto UPM_NOT_FOUND
"%UPM_EXE%" --version >nul 2>nul
if errorlevel 1 goto UPM_NOT_FOUND
echo [MDPro3] Unity found: %UNITY_EXE%
echo [MDPro3] Unity Package Manager is ready.
echo [MDPro3] Opening project. The plugin is compiled there as well.
echo [MDPro3] First launch may take a long time while packages are restored.
start "MDPro3 Unity" "%UNITY_EXE%" -projectPath "%PROJECT_DIR%" -logFile "%UNITY_LOG%"
goto END_OK

:UPM_NOT_FOUND
echo.
echo [MDPro3] Unity Package Manager is missing or cannot run.
echo [MDPro3] Expected path: %UPM_EXE%
echo [MDPro3] Close Unity, repair or reinstall Unity Editor %UNITY_VERSION% in Unity Hub, then run this file again.
call :FIND_UNITY_HUB
if defined UNITY_HUB start "" "%UNITY_HUB%"
pause
exit /b 1

:UNITY_NOT_FOUND
echo.
echo [MDPro3] Unity Editor %UNITY_VERSION% was not found.
echo [MDPro3] Required changeset: %UNITY_CHANGESET%
echo [MDPro3] Install it in Unity Hub or edit UNITY_VERSION in this bat.
call :FIND_UNITY_HUB
if defined UNITY_HUB start "" "%UNITY_HUB%"
pause
exit /b 1

:CHECK_ONLY
call :FIND_UNITY
call :FIND_UNITY_HUB
call :FIND_BUILT_GAME
echo PROJECT_DIR=%PROJECT_DIR%
echo UNITY_VERSION=%UNITY_VERSION%
if defined UNITY_EXE echo UNITY_EXE=%UNITY_EXE%
if not defined UNITY_EXE echo UNITY_EXE=NOT_FOUND
if defined UNITY_HUB echo UNITY_HUB=%UNITY_HUB%
if not defined UNITY_HUB echo UNITY_HUB=NOT_FOUND
if defined BUILT_EXE echo BUILT_EXE=%BUILT_EXE%
if not defined BUILT_EXE echo BUILT_EXE=NOT_FOUND
call :CHECK_PLUGIN
exit /b 0

:CHECK_PLUGIN
if not exist "%PLUGIN_STATE_SCRIPT%" (
  echo PLUGIN_STATUS=ERROR
  echo PLUGIN_MESSAGE=Plugin helper not found: %PLUGIN_STATE_SCRIPT%
  exit /b 0
)
if not exist "%PLUGIN_STATE%" mkdir "%PLUGIN_STATE%" >nul 2>nul
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PLUGIN_STATE_SCRIPT%" ^
  -Action status -Source "%PLUGIN_SRC%" -Target "%PLUGIN_DST%" -State "%PLUGIN_STATE%" ^
  -BuiltExe "%BUILT_EXE%" > "%PLUGIN_STATE%\plugin-check.txt" 2>&1
if exist "%PLUGIN_STATE%\plugin-check.txt" (
  for /f "usebackq tokens=1,* delims==" %%A in ("%PLUGIN_STATE%\plugin-check.txt") do (
    if /I "%%A"=="PLUGINSTATUS" echo PLUGIN_STATUS=%%B
    if /I "%%A"=="PLUGINMESSAGE" echo PLUGIN_MESSAGE=%%B
    if /I "%%A"=="PLUGINCONFIG" echo PLUGIN_CONFIG=%%B
    if /I "%%A"=="PLUGINFEATURES" echo PLUGIN_FEATURES=%%B
    if /I "%%A"=="PLUGINCONFIGERROR" echo PLUGIN_CONFIG_ERROR=%%B
    if /I "%%A"=="PLUGINCONFIGMESSAGE" echo PLUGIN_CONFIG_MESSAGE=%%B
    if /I "%%A"=="PLUGINCONFIGFLAGS" echo PLUGIN_CONFIG_FLAGS=%%B
  )
)
exit /b 0

:SHOW_HELP
echo Run_MDPro3.bat [options]
echo.
echo Without options the plugin sources in plugins\MDPro3Plugins are copied into the
echo Unity project, the built player is rebuilt when the plugin changed and the game
echo is started.
echo.
echo Options:
echo   --check         Only report what was found, change nothing.
echo   --rebuild       Rebuild the player even when the plugin did not change.
echo   --no-build      Start the game without rebuilding it.
echo   --plugin-off    Remove the plugin from the Unity project and rebuild the game without it.
echo   --diagnose      Start the built game with the plugin self test and quit again.
echo   --help          Show this text.
echo.
echo Plugin documentation: plugins\README.md
echo Feature switches (no rebuild needed): plugins\config.json
exit /b 0

:FIND_BUILT_GAME
set "BUILT_EXE="
set "BUILT_DIR="
for %%F in (
  "%ROOT%\Build\MDPro3.exe"
  "%ROOT%\Build\MDPro3\MDPro3.exe"
  "%ROOT%\Builds\MDPro3.exe"
  "%ROOT%\Builds\MDPro3\MDPro3.exe"
) do (
  if exist "%%~F" set "BUILT_EXE=%%~F"
)
if not defined BUILT_EXE exit /b 0
for %%D in ("%BUILT_EXE%") do set "BUILT_DIR=%%~dpD"
exit /b 0

:FIND_UNITY
set "UNITY_EXE="
for %%F in (
  "C:\Program Files\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
  "C:\Program Files\Unity\Editor\Unity.exe"
  "D:\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
  "E:\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
) do (
  if exist "%%~F" set "UNITY_EXE=%%~F"
)
exit /b 0

:FIND_UNITY_HUB
set "UNITY_HUB="
for %%F in (
  "C:\Program Files\Unity Hub\Unity Hub.exe"
  "%LOCALAPPDATA%\Programs\Unity Hub\Unity Hub.exe"
) do (
  if exist "%%~F" set "UNITY_HUB=%%~F"
)
exit /b 0

:END_OK
if defined EXIT_CODE exit /b %EXIT_CODE%
exit /b 0
