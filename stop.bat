@echo off
setlocal EnableExtensions

rem Stops LedManager and its senders. Pure batch on purpose (no PowerShell):
rem AV heuristics flag powershell one-liners as ClickFix trojans.

echo Stopping LedManager...
taskkill /IM LedManager.exe /F >nul 2>&1
if errorlevel 1 (
  echo No LedManager process found.
) else (
  echo LedManager stopped.
)

taskkill /IM PicoCommandSender.exe /F >nul 2>&1
if not errorlevel 1 echo PicoCommandSender stopped.

endlocal
