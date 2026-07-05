@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PROJECT=BooruDatasetTagManager\BooruDatasetTagManager.csproj"
set "CONFIG=Release"
set "FRAMEWORK=net8.0-windows"
set "RUNTIME=win-x64"
set "OUTPUT_DIR=dist"
set "LOG=quick_build.log"
set "OUTPUT_EXE=%OUTPUT_DIR%\BooruDatasetTagManagerPlus.exe"

if not "%~1"=="" (
    if /i "%~1"=="debug" set "CONFIG=Debug"
    if /i "%~1"=="release" set "CONFIG=Release"
)

set "OUTPUT_EXE=%OUTPUT_DIR%\BooruDatasetTagManagerPlus.exe"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK was not found. Please install .NET 8 SDK first.
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

if not exist "%PROJECT%" (
    echo Project file not found: %PROJECT%
    echo.
    pause
    exit /b 1
)

echo Building BooruDatasetTagManagerPlus [%CONFIG%]...
echo.
> "%LOG%" echo Building BooruDatasetTagManagerPlus [%CONFIG%]...

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

rem test_start.bat launches from bin\<Configuration>\net8.0-windows first,
rem so update that output before producing the self-contained dist package.
dotnet build "%PROJECT%" -c "%CONFIG%" -f "%FRAMEWORK%" --nologo --no-restore -m -v quiet /clp:ErrorsOnly >> "%LOG%" 2>&1
if errorlevel 1 (
    echo.
    echo Fast build failed. Restoring packages and trying once more...
    echo.
    >> "%LOG%" echo.
    >> "%LOG%" echo Fast build failed. Restoring packages and trying once more...
    dotnet restore "%PROJECT%" -r "%RUNTIME%" --nologo -v quiet /clp:ErrorsOnly >> "%LOG%" 2>&1
    if errorlevel 1 goto :build_failed

    dotnet build "%PROJECT%" -c "%CONFIG%" -f "%FRAMEWORK%" --nologo --no-restore -m -v quiet /clp:ErrorsOnly >> "%LOG%" 2>&1
    if errorlevel 1 goto :build_failed
)

dotnet publish "%PROJECT%" -c "%CONFIG%" -f "%FRAMEWORK%" -r "%RUNTIME%" --self-contained true -o "%OUTPUT_DIR%" --nologo --no-restore -m -v quiet /clp:ErrorsOnly >> "%LOG%" 2>&1
if errorlevel 1 (
    echo.
    echo Fast publish failed. Restoring packages and trying once more...
    echo.
    >> "%LOG%" echo.
    >> "%LOG%" echo Fast publish failed. Restoring packages and trying once more...
    dotnet restore "%PROJECT%" -r "%RUNTIME%" --nologo -v quiet /clp:ErrorsOnly >> "%LOG%" 2>&1
    if errorlevel 1 goto :build_failed

    dotnet publish "%PROJECT%" -c "%CONFIG%" -f "%FRAMEWORK%" -r "%RUNTIME%" --self-contained true -o "%OUTPUT_DIR%" --nologo --no-restore -m -v quiet /clp:ErrorsOnly >> "%LOG%" 2>&1
    if errorlevel 1 goto :build_failed
)

echo.
echo Build completed successfully.
if exist "%OUTPUT_EXE%" (
    echo Output: %OUTPUT_EXE%
) else (
    echo Output folder: %OUTPUT_DIR%
)
echo Log: %LOG%
echo.
pause
exit /b 0

:build_failed
echo.
echo Build failed. Check the messages above.
echo.
type "%LOG%"
echo.
pause
exit /b 1
