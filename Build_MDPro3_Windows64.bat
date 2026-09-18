@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "UNITY_VERSION=6000.0.24f1"
set "UNITY_CHANGESET=11fa355cd605"
set "BUILD_DIR=%ROOT%\Build\MDPro3"
set "BUILD_EXE=%BUILD_DIR%\MDPro3.exe"
set "LOG_DIR=%PROJECT_DIR%\Logs"
set "UNITY_LOG=%LOG_DIR%\UnityBuild_Windows64.log"
set "DEVELOPMENT_ARG="
set "SCRIPTING_ARG="
set "CLEAN_BUILD=0"
set "COMPILE_ONLY=0"
set "PAUSE_ON_EXIT=1"

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--development" set "DEVELOPMENT_ARG=-development"
if /I "%~1"=="--il2cpp" set "SCRIPTING_ARG=-il2cpp"
if /I "%~1"=="--clean" set "CLEAN_BUILD=1"
if /I "%~1"=="--compile-only" set "COMPILE_ONLY=1"
if /I "%~1"=="--no-pause" set "PAUSE_ON_EXIT=0"
if /I "%~1"=="--help" goto SHOW_HELP
shift
goto PARSE_ARGS

:AFTER_PARSE_ARGS
echo [MDPro3] Project dir: %PROJECT_DIR%
echo [MDPro3] Unity version: %UNITY_VERSION%
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>nul

if not exist "%PROJECT_DIR%\Assets" (
  set "EXIT_CODE=1"
  echo.
  echo [MDPro3] Unity project folder not found: %PROJECT_DIR%
  echo [MDPro3] Keep this script next to the MDPro3 project folder.
  goto FINISH
)

call :FIND_UNITY
if not defined UNITY_EXE goto UNITY_NOT_FOUND
echo [MDPro3] Unity found: %UNITY_EXE%

if "%COMPILE_ONLY%"=="1" goto COMPILE_ONLY

if "%CLEAN_BUILD%"=="1" (
  echo [MDPro3] Cleaning build dir: %BUILD_DIR%
  if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
)

if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%" >nul 2>nul

echo [MDPro3] Build output: %BUILD_EXE%
echo [MDPro3] Log file: %UNITY_LOG%
echo [MDPro3] Starting Windows64 build...

"%UNITY_EXE%" ^
  -batchmode ^
  -nographics ^
  -quit ^
  -projectPath "%PROJECT_DIR%" ^
  -logFile "%UNITY_LOG%" ^
  -executeMethod MDPro3.Editor.CommandLineBuild.BuildWindows64 ^
  -buildPath "%BUILD_EXE%" ^
  %DEVELOPMENT_ARG% ^
  %SCRIPTING_ARG%

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto BUILD_FAILED
if not exist "%BUILD_EXE%" goto BUILD_MISSING

call :COPY_RUNTIME_DATA
if not "%EXIT_CODE%"=="0" goto BUILD_FAILED

echo.
echo [MDPro3] Build succeeded.
echo [MDPro3] Output: %BUILD_EXE%
echo [MDPro3] You can run it with Run_MDPro3.bat.
goto FINISH

:COMPILE_ONLY
echo [MDPro3] Compile-only mode. Unity will import assets, compile scripts, then quit.
echo [MDPro3] Log file: %UNITY_LOG%
"%UNITY_EXE%" ^
  -batchmode ^
  -nographics ^
  -quit ^
  -projectPath "%PROJECT_DIR%" ^
  -logFile "%UNITY_LOG%"

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto BUILD_FAILED
echo.
echo [MDPro3] Compile-only check succeeded.
goto FINISH

:BUILD_MISSING
set "EXIT_CODE=1"
echo.
echo [MDPro3] Unity returned success, but the build exe was not found:
echo [MDPro3] %BUILD_EXE%
goto PRINT_ERRORS

:BUILD_FAILED
echo.
echo [MDPro3] Build failed. Exit code: %EXIT_CODE%
goto PRINT_ERRORS

:PRINT_ERRORS
echo [MDPro3] Important log lines:
if exist "%UNITY_LOG%" (
  findstr /I /C:"error CS" /C:"Build failed" /C:"Exception" /C:"Aborting batchmode" "%UNITY_LOG%"
  echo [MDPro3] Full log: %UNITY_LOG%
) else (
  echo [MDPro3] Log file was not created.
)
goto FINISH

:UNITY_NOT_FOUND
set "EXIT_CODE=1"
echo.
echo [MDPro3] Unity Editor %UNITY_VERSION% was not found.
echo [MDPro3] Required changeset: %UNITY_CHANGESET%
echo [MDPro3] Install it in Unity Hub or edit UNITY_VERSION in this bat.
goto FINISH

:SHOW_HELP
echo Build_MDPro3_Windows64.bat [options]
echo.
echo Options:
echo   --clean         Delete the Build\MDPro3 folder next to this script before building.
echo   --development   Make a Unity development build with script debugging.
echo   --il2cpp        Use IL2CPP instead of the default Mono backend.
echo   --compile-only  Only import assets and compile scripts.
echo   --no-pause      Do not pause before exiting.
echo.
echo Deck and replay are only added to: nothing in the build folder is deleted, and an
echo older project copy never replaces a newer file saved by the game.
exit /b 0

:FIND_UNITY
if defined UNITY_EXE (
  if exist "%UNITY_EXE%" exit /b 0
)
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

:COPY_RUNTIME_DATA
echo.
echo [MDPro3] Copying runtime data to build folder...
call :ROBOCOPY_ABSOLUTE_DIR "%ROOT%\StandaloneWindows64" "%BUILD_DIR%\StandaloneWindows64" "StandaloneWindows64"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR "Data"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ENSURE_ROOT_CDB
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR_KEEP "Deck"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR "Expansions"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR "Picture"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_ABSOLUTE_DIR "%PROJECT_DIR%\Picture\Art" "%BUILD_DIR%\Picture\Art" "Picture\Art"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_ABSOLUTE_DIR "%PROJECT_DIR%\Picture\Closeup" "%BUILD_DIR%\Picture\Closeup" "Picture\Closeup"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR "Sound"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_ABSOLUTE_DIR "%PROJECT_DIR%\Sound\Voice" "%BUILD_DIR%\Sound\Voice" "Sound\Voice"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR "specials"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR_KEEP "replay"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%
call :ROBOCOPY_DIR "Video"
if not "%EXIT_CODE%"=="0" exit /b %EXIT_CODE%

echo [MDPro3] Runtime data copy finished.
exit /b 0

:ROBOCOPY_DIR
set "COPY_NAME=%~1"
set "COPY_SOURCE=%PROJECT_DIR%\%COPY_NAME%"
set "COPY_TARGET=%BUILD_DIR%\%COPY_NAME%"
if not exist "%COPY_SOURCE%" (
  echo [MDPro3] Skip missing folder: %COPY_NAME%
  exit /b 0
)
echo [MDPro3] Sync %COPY_NAME%
robocopy "%COPY_SOURCE%" "%COPY_TARGET%" /MIR /XJ /XD .git /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if %ROBOCOPY_CODE% GEQ 8 (
  set "EXIT_CODE=%ROBOCOPY_CODE%"
  echo [MDPro3] Failed to copy %COPY_NAME%. Robocopy exit code: %ROBOCOPY_CODE%
  exit /b %ROBOCOPY_CODE%
)
set "EXIT_CODE=0"
exit /b 0

rem Deck and replay are only added to. The game writes saved decks and replays into
rem the build folder while it runs, and a mirror sync would delete them as leftovers.
rem Missing files are copied in, and an older project copy never replaces a newer
rem file that the game saved in the build folder.
:ROBOCOPY_DIR_KEEP
set "COPY_NAME=%~1"
set "COPY_SOURCE=%PROJECT_DIR%\%COPY_NAME%"
set "COPY_TARGET=%BUILD_DIR%\%COPY_NAME%"
if not exist "%COPY_SOURCE%" (
  echo [MDPro3] Skip missing folder: %COPY_NAME%
  exit /b 0
)
echo [MDPro3] Sync %COPY_NAME% (keep existing files)
robocopy "%COPY_SOURCE%" "%COPY_TARGET%" /E /XO /XJ /XD .git /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if %ROBOCOPY_CODE% GEQ 8 (
  set "EXIT_CODE=%ROBOCOPY_CODE%"
  echo [MDPro3] Failed to copy %COPY_NAME%. Robocopy exit code: %ROBOCOPY_CODE%
  exit /b %ROBOCOPY_CODE%
)
set "EXIT_CODE=0"
exit /b 0

:ROBOCOPY_ABSOLUTE_DIR
set "COPY_SOURCE=%~1"
set "COPY_TARGET=%~2"
set "COPY_NAME=%~3"
if not exist "%COPY_SOURCE%" (
  echo [MDPro3] Skip missing folder: %COPY_NAME%
  exit /b 0
)
echo [MDPro3] Sync %COPY_NAME%
robocopy "%COPY_SOURCE%" "%COPY_TARGET%" /MIR /XJ /XD .git /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if %ROBOCOPY_CODE% GEQ 8 (
  set "EXIT_CODE=%ROBOCOPY_CODE%"
  echo [MDPro3] Failed to copy %COPY_NAME%. Robocopy exit code: %ROBOCOPY_CODE%
  exit /b %ROBOCOPY_CODE%
)
set "EXIT_CODE=0"
exit /b 0

:ENSURE_ROOT_CDB
set "CONFIG_LANG=zh-CN"
set "ROOT_CDB=%BUILD_DIR%\Data\cards.cdb"
if exist "%BUILD_DIR%\Data\config.conf" (
  for /f "usebackq tokens=2 delims=^>" %%L in (`findstr /B "Language-" "%BUILD_DIR%\Data\config.conf"`) do set "CONFIG_LANG=%%L"
)
set "LANG_CDB=%BUILD_DIR%\Data\locales\%CONFIG_LANG%\cards.cdb"
if not exist "%LANG_CDB%" set "LANG_CDB=%BUILD_DIR%\Data\locales\zh-CN\cards.cdb"
if not exist "%LANG_CDB%" (
  set "EXIT_CODE=1"
  echo [MDPro3] Missing cards database: %LANG_CDB%
  exit /b 1
)
set "ROOT_CDB_SIZE=0"
if exist "%ROOT_CDB%" for %%F in ("%ROOT_CDB%") do set "ROOT_CDB_SIZE=%%~zF"
if "%ROOT_CDB_SIZE%"=="0" (
  echo [MDPro3] Fix empty Data\cards.cdb from locale: %CONFIG_LANG%
  copy /Y "%LANG_CDB%" "%ROOT_CDB%" >nul
  if errorlevel 1 (
    set "EXIT_CODE=1"
    echo [MDPro3] Failed to copy cards database.
    exit /b 1
  )
)
set "EXIT_CODE=0"
exit /b 0

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
