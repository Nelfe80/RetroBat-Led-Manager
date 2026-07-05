@echo off
setlocal

echo ===================================================
echo  RetroBat LedManager - Build PicoCommandSender.exe
echo ===================================================
echo.

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK introuvable dans le PATH.
    goto :fail
)

echo [0/4] Arret de l'instance PicoCommandSender...
taskkill /IM PicoCommandSender.exe /F >nul 2>nul
if %ERRORLEVEL% equ 0 (
    echo   - PicoCommandSender arrete.
    timeout /t 2 /nobreak >nul
) else (
    echo   - Aucune instance PicoCommandSender active.
)

echo.
echo [1/4] Nettoyage du projet...
dotnet clean src\PicoCommandSender\PicoCommandSender.csproj -c Release -v quiet >nul 2>nul
echo   - Clean OK.

set "BUILD_TMP=%~dp0.temp\publish\PicoCommandSender"
if exist "%BUILD_TMP%" rmdir /S /Q "%BUILD_TMP%"
if not exist "%BUILD_TMP%" mkdir "%BUILD_TMP%"

echo.
echo [2/4] Publication Release win-x64...
echo.
dotnet publish src\PicoCommandSender\PicoCommandSender.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%BUILD_TMP%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Echec de la compilation PicoCommandSender.
    if exist "%BUILD_TMP%" rmdir /S /Q "%BUILD_TMP%"
    goto :fail
)

echo.
echo [3/4] Copie de la publication a la racine...
if exist "%BUILD_TMP%\PicoCommandSender.exe" (
    copy /Y "%BUILD_TMP%\PicoCommandSender.exe" "%~dp0PicoCommandSender.exe" >nul
    if %ERRORLEVEL% neq 0 (
        echo [ERROR] Copie de PicoCommandSender.exe impossible. Le fichier est-il encore verrouille ?
        if exist "%BUILD_TMP%" rmdir /S /Q "%BUILD_TMP%"
        goto :fail
    )
    echo   - PicoCommandSender.exe mis a jour a la racine.
) else (
    echo [ERROR] PicoCommandSender.exe introuvable dans le dossier de publication.
    dir "%BUILD_TMP%"
    if exist "%BUILD_TMP%" rmdir /S /Q "%BUILD_TMP%"
    goto :fail
)

echo.
echo [4/4] Nettoyage temporaire...
rmdir /S /Q "%BUILD_TMP%"
echo   - Done.

echo.
echo ===================================================
echo  Build PicoCommandSender.exe termine avec succes !
echo ===================================================
echo.
goto :success

:fail
if /I not "%~1"=="--no-pause" pause
exit /b 1

:success
if /I not "%~1"=="--no-pause" pause
exit /b 0
