@echo off
setlocal

echo ===================================================
echo  RetroBat LedManager - Build Release
echo ===================================================
echo.

call "%~dp0build-LedManager.bat" --no-pause
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build LedManager.exe en echec.
    pause
    exit /b %ERRORLEVEL%
)

call "%~dp0build-PicoCommandSender.bat" --no-pause
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build PicoCommandSender.exe en echec.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo  Build termine avec succes !
echo  Lancez LedManager.exe depuis la racine.
echo ===================================================
echo.
pause
