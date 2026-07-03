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

if exist "%APP_RELEASE%" (
    start "" "%APP_RELEASE%"
    exit /b 0
)

if exist "%APP_DEBUG%" (
    start "" "%APP_DEBUG%"
    exit /b 0
)

if exist "%APP_RELEASE_PLUS_LEGACY%" (
    start "" "%APP_RELEASE_PLUS_LEGACY%"
    exit /b 0
)

if exist "%APP_DEBUG_PLUS_LEGACY%" (
    start "" "%APP_DEBUG_PLUS_LEGACY%"
    exit /b 0
)

if exist "%APP_RELEASE_LEGACY%" (
    start "" "%APP_RELEASE_LEGACY%"
    exit /b 0
)

if exist "%APP_DEBUG_LEGACY%" (
    start "" "%APP_DEBUG_LEGACY%"
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
