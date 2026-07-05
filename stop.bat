@echo off
setlocal

set "PLUGIN_DIR=%~dp0"
set "API_EXE=%PLUGIN_DIR%LedManager.exe"

echo Stopping LedManager...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$apiPath=[System.IO.Path]::GetFullPath('%API_EXE%'); $processes=Get-Process LedManager -ErrorAction SilentlyContinue | Where-Object { try { [System.IO.Path]::GetFullPath($_.Path) -eq $apiPath } catch { $false } }; if (-not $processes) { Write-Host 'No LedManager process found for this plugin.'; exit 0 }; foreach ($proc in $processes) { Write-Host ('Stopping LedManager PID ' + $proc.Id); Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }; Start-Sleep -Milliseconds 300; $remaining=Get-Process RetroBat.Api -ErrorAction SilentlyContinue | Where-Object { try { [System.IO.Path]::GetFullPath($_.Path) -eq $apiPath } catch { $false } }; if ($remaining) { Write-Host 'LedManager may still be running.'; exit 1 } else { Write-Host 'LedManager stopped.' }"

endlocal
