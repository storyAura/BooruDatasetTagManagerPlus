@echo off
setlocal
cd /d "%~dp0"

set "APP_RELEASE=BooruDatasetTagManager\bin\Release\net8.0-windows\BooruDatasetTagManagerPlus.exe"
set "APP_DEBUG=BooruDatasetTagManager\bin\Debug\net8.0-windows\BooruDatasetTagManagerPlus.exe"
set "APP_RELEASE_PLUS_LEGACY=BooruDatasetTagManager\bin\Release\net8.0-windows\BooruDatasetTagManager+.exe"
set "APP_DEBUG_PLUS_LEGACY=BooruDatasetTagManager\bin\Debug\net8.0-windows\BooruDatasetTagManager+.exe"
set "APP_RELEASE_LEGACY=BooruDatasetTagManager\bin\Release\net8.0-windows\BooruDatasetTagManager.exe"
set "APP_DEBUG_LEGACY=BooruDatasetTagManager\bin\Debug\net8.0-windows\BooruDatasetTagManager.exe"
set "PROJECT=BooruDatasetTagManager\BooruDatasetTagManager.csproj"

rem Launch the NEWEST existing binary, not the first found: preferring
rem Release by position used to start a stale build after a Debug rebuild.
set "APP_NEWEST="
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command ^
  "@('%APP_RELEASE%','%APP_DEBUG%','%APP_RELEASE_PLUS_LEGACY%','%APP_DEBUG_PLUS_LEGACY%','%APP_RELEASE_LEGACY%','%APP_DEBUG_LEGACY%') | Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTime } -Descending | Select-Object -First 1"`) do set "APP_NEWEST=%%i"

if defined APP_NEWEST (
    start "" "%APP_NEWEST%"
    exit /b 0
)

echo No compiled application was found. Building Release version...
dotnet build "%PROJECT%" -c Release
if errorlevel 1 (
    echo.
    echo Build failed. Check the messages above.
    pause
    exit /b 1
)

if not exist "%APP_RELEASE%" (
    echo.
    echo Build completed, but the application executable was not found.
    pause
    exit /b 1
)

start "" "%APP_RELEASE%"
exit /b 0
