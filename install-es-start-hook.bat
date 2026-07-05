@echo off
setlocal EnableExtensions DisableDelayedExpansion

rem Installs the LedManager EmulationStation start hook.
rem This does not modify RetroBat's updatestores.bat.

set "PLUGIN_DIR=%~dp0"
for %%I in ("%PLUGIN_DIR%..\..") do set "RETROBAT_ROOT=%%~fI"

set "SOURCE=%PLUGIN_DIR%.installer\scripts\start\LedManager-start.bat"
set "TARGET_DIR=%RETROBAT_ROOT%\emulationstation\.emulationstation\scripts\start"
set "TARGET=%TARGET_DIR%\LedManager-start.bat"

if not exist "%SOURCE%" (
  echo LedManager hook source not found:
  echo   %SOURCE%
  pause
  exit /b 1
)

if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%" >nul 2>&1

copy /Y "%SOURCE%" "%TARGET%" >nul
if errorlevel 1 (
  echo Failed to install LedManager ES start hook:
  echo   %TARGET%
  pause
  exit /b 1
)

echo Installed LedManager ES start hook:
echo   %TARGET%
echo.
if exist "%RETROBAT_SCRIPT%" (
  echo RetroBat start script left untouched:
  echo   %RETROBAT_SCRIPT%
) else (
  echo Note: RetroBat updatestores.bat was not found in:
  echo   %TARGET_DIR%
)
echo.
echo On next EmulationStation startup, LedManager will be started from scripts\start.
echo.
pause
exit /b 0
