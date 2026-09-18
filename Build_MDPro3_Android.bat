@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PROJECT_DIR=%ROOT%\MDPro3"
set "UNITY_VERSION=6000.0.24f1"
set "UNITY_CHANGESET=11fa355cd605"
set "BUILD_DIR=%ROOT%\Build\Android"
set "BUILD_APK=%BUILD_DIR%\MDPro3.apk"
set "EXTERNAL_RES_DIR=%BUILD_DIR%\ExternalResources"
set "LOG_DIR=%PROJECT_DIR%\Logs"
set "UNITY_LOG=%LOG_DIR%\UnityBuild_Android.log"
set "DEVELOPMENT_ARG=-development"
set "CLEAN_BUILD=0"
set "COMPILE_ONLY=0"
set "SKIP_ADDRESSABLES_ARG="
set "SKIP_STREAMING_ASSETS_ARG="
set "PAUSE_ON_EXIT=1"

:PARSE_ARGS
if "%~1"=="" goto AFTER_PARSE_ARGS
if /I "%~1"=="--development" set "DEVELOPMENT_ARG=-development"
if /I "%~1"=="--release" set "DEVELOPMENT_ARG="
if /I "%~1"=="--clean" set "CLEAN_BUILD=1"
if /I "%~1"=="--compile-only" set "COMPILE_ONLY=1"
if /I "%~1"=="--skip-addressables" set "SKIP_ADDRESSABLES_ARG=-skip-addressables"
if /I "%~1"=="--skip-streaming-assets" set "SKIP_STREAMING_ASSETS_ARG=-skip-streaming-assets"
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

call :CHECK_ANDROID_SUPPORT
if not "%EXIT_CODE%"=="0" goto FINISH

if "%COMPILE_ONLY%"=="1" goto COMPILE_ONLY

if "%CLEAN_BUILD%"=="1" (
  echo [MDPro3] Cleaning build dir: %BUILD_DIR%
  if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
)

if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%" >nul 2>nul

echo [MDPro3] Build output: %BUILD_APK%
echo [MDPro3] Log file: %UNITY_LOG%
echo [MDPro3] Starting Android APK build...
echo [MDPro3] First Android build can take a long time while Addressables and zip packages are generated.

"%UNITY_EXE%" ^
  -batchmode ^
  -nographics ^
  -quit ^
  -projectPath "%PROJECT_DIR%" ^
  -buildTarget Android ^
  -logFile "%UNITY_LOG%" ^
  -executeMethod MDPro3.Editor.CommandLineBuild.BuildAndroid ^
  -buildPath "%BUILD_APK%" ^
  %DEVELOPMENT_ARG% ^
  %SKIP_ADDRESSABLES_ARG% ^
  %SKIP_STREAMING_ASSETS_ARG%

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto BUILD_FAILED
if not exist "%BUILD_APK%" goto BUILD_MISSING

for %%F in ("%BUILD_APK%") do set "APK_SIZE=%%~zF"
echo.
echo [MDPro3] APK build succeeded.
echo [MDPro3] Output: %BUILD_APK%
echo [MDPro3] Size: %APK_SIZE% bytes
if exist "%EXTERNAL_RES_DIR%" (
  echo.
  echo [MDPro3] External resource packages:
  echo [MDPro3] %EXTERNAL_RES_DIR%
  echo [MDPro3] Copy Picture_*.zip and Sound_*.zip to this phone directory:
  echo [MDPro3] Internal storage\Android\data\com.YGO.MDPro3\files\
  echo [MDPro3] adb path: /storage/emulated/0/Android/data/com.YGO.MDPro3/files/
)
goto FINISH

:COMPILE_ONLY
echo [MDPro3] Compile-only mode. Unity will switch to Android, import assets, compile scripts, then quit.
echo [MDPro3] Log file: %UNITY_LOG%
"%UNITY_EXE%" ^
  -batchmode ^
  -nographics ^
  -quit ^
  -projectPath "%PROJECT_DIR%" ^
  -buildTarget Android ^
  -logFile "%UNITY_LOG%"

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto BUILD_FAILED
echo.
echo [MDPro3] Compile-only check succeeded.
goto FINISH

:BUILD_MISSING
set "EXIT_CODE=1"
echo.
echo [MDPro3] Unity returned success, but the APK was not found:
echo [MDPro3] %BUILD_APK%
goto PRINT_ERRORS

:BUILD_FAILED
echo.
echo [MDPro3] Build failed. Exit code: %EXIT_CODE%
goto PRINT_ERRORS

:PRINT_ERRORS
echo [MDPro3] Important log lines:
if exist "%UNITY_LOG%" (
  findstr /I /C:"error CS" /C:"Build failed" /C:"Exception" /C:"Gradle" /C:"AndroidPlayer" /C:"Android SDK" "%UNITY_LOG%"
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
echo Build_MDPro3_Android.bat [options]
echo.
echo Options:
echo   --clean                  Delete the Build\Android folder next to this script before building.
echo   --development            Make a Unity development APK with script debugging. This is the default.
echo   --release                Make a non-development APK.
echo   --compile-only           Only switch target, import assets, and compile scripts.
echo   --skip-addressables      Reuse existing Platforms\Android\MDPro3 Addressables content.
echo   --skip-streaming-assets  Reuse existing Assets\StreamingAssets zip packages.
echo   --no-pause               Do not pause before exiting.
echo.
echo External resource packages are generated under:
echo   Build\Android\ExternalResources   - the Build folder next to this script
echo Copy Picture_*.zip and Sound_*.zip to:
echo   Internal storage\Android\data\com.YGO.MDPro3\files\
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

:CHECK_ANDROID_SUPPORT
set "UNITY_EDITOR_DIR="
for %%F in ("%UNITY_EXE%") do set "UNITY_EDITOR_DIR=%%~dpF"
set "ANDROID_PLAYER_DIR=%UNITY_EDITOR_DIR%Data\PlaybackEngines\AndroidPlayer"
if exist "%ANDROID_PLAYER_DIR%" (
  set "EXIT_CODE=0"
  exit /b 0
)

set "EXIT_CODE=1"
echo.
echo [MDPro3] Missing Unity Android Build Support:
echo [MDPro3] %ANDROID_PLAYER_DIR%
echo.
echo [MDPro3] Install Android Build Support, Android SDK/NDK Tools, and OpenJDK for Unity %UNITY_VERSION%.
echo [MDPro3] Direct Android Support package:
echo [MDPro3] https://download.unity3d.com/download_unity/%UNITY_CHANGESET%/TargetSupportInstaller/UnitySetup-Android-Support-for-Editor-%UNITY_VERSION%.exe
exit /b 1

:FINISH
if "%PAUSE_ON_EXIT%"=="1" pause
exit /b %EXIT_CODE%
