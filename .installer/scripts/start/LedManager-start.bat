@echo off
setlocal EnableExtensions DisableDelayedExpansion

rem EmulationStation start hook for LedManager.
rem This script is intended to be copied to:
rem   emulationstation\.emulationstation\scripts\start\LedManager-start.bat
rem Pure batch on purpose: PowerShell one-liners with hidden windows are
rem flagged by antivirus heuristics (Trojan:Win32/ClickFix).

for %%I in ("%~dp0..\..\..\..\plugins\LedManager") do set "PLUGIN_DIR=%%~fI"
set "LED_EXE=%PLUGIN_DIR%\LedManager.exe"
set "LED_INI=%PLUGIN_DIR%\LedManager.ini"
set "LOG_DIR=%PLUGIN_DIR%\.log"
set "LOG_FILE=%LOG_DIR%\es-start-hook.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
echo %date% %time% ES start hook entered.>> "%LOG_FILE%"

if not exist "%LED_EXE%" (
  echo %date% %time% ERROR missing executable: %LED_EXE%>> "%LOG_FILE%"
  exit /b 1
)

if not exist "%LED_INI%" (
  echo %date% %time% ERROR missing configuration: %LED_INI%>> "%LOG_FILE%"
  exit /b 1
)

tasklist /FI "IMAGENAME eq LedManager.exe" 2>nul | find /I "LedManager.exe" >nul
if not errorlevel 1 (
  echo %date% %time% LedManager already running.>> "%LOG_FILE%"
  exit /b 0
)

start "LedManager" /D "%PLUGIN_DIR%" /MIN "%LED_EXE%" --ini LedManager.ini --hide-console
echo %date% %time% LedManager started.>> "%LOG_FILE%"
exit /b 0
