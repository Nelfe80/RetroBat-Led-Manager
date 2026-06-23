@echo off
setlocal EnableExtensions DisableDelayedExpansion

rem EmulationStation start hook for LedManager.
rem This script is intended to be copied to:
rem   emulationstation\.emulationstation\scripts\start\LedManager-start.bat

for %%I in ("%~dp0..\..\..\..\plugins\LedManager") do set "PLUGIN_DIR=%%~fI"
set "LED_EXE=%PLUGIN_DIR%\LedManager.exe"
set "LED_INI=%PLUGIN_DIR%\LedManager.ini"
set "LOG_DIR=%PLUGIN_DIR%\logs"
set "LOG_FILE=%LOG_DIR%\es-start-hook.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1

if not exist "%LED_EXE%" (
  echo LedManager executable not found:
  echo   %LED_EXE%
  exit /b 1
)

if not exist "%LED_INI%" (
  echo LedManager configuration not found:
  echo   %LED_INI%
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"$ErrorActionPreference='SilentlyContinue'; ^
 $exe=[System.IO.Path]::GetFullPath('%LED_EXE%'); ^
 $wd=[System.IO.Path]::GetFullPath('%PLUGIN_DIR%'); ^
 $log='%LOG_FILE%'; ^
 function Log([string]$m){ $stamp=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'); Add-Content -LiteralPath $log -Value ($stamp + ' ' + $m) -Encoding UTF8 }; ^
 Log 'ES start hook entered.'; ^
 $running=Get-Process -Name 'LedManager' -ErrorAction SilentlyContinue | Where-Object { try { [System.IO.Path]::GetFullPath($_.Path) -eq $exe } catch { $false } }; ^
 if ($running) { Log ('LedManager already running PID ' + (($running | Select-Object -First 1).Id)); exit 0 }; ^
 Unblock-File -LiteralPath $exe -ErrorAction SilentlyContinue; ^
 try { ^
   $proc=Start-Process -FilePath $exe -ArgumentList @('--ini','LedManager.ini') -WorkingDirectory $wd -WindowStyle Hidden -PassThru -ErrorAction Stop; ^
   if ($null -eq $proc) { throw 'Start-Process returned no process.' } ^
   Log ('LedManager started as hidden process PID ' + $proc.Id); ^
   exit 0; ^
 } catch { ^
   Log ('ERROR failed to start LedManager: ' + $_.Exception.Message); ^
   exit 1; ^
 }"

exit /b %ERRORLEVEL%
