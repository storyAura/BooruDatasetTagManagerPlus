@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "VERSION=1.2.0"
set "PROJECT=BooruDatasetTagManager\BooruDatasetTagManager.csproj"
set "CONFIG=Release"
set "FRAMEWORK=net8.0-windows"
set "RUNTIME=win-x64"
rem Publish into a fresh throwaway staging dir, never into dist: dist doubles
rem as a local run dir, and zipping it shipped settings.json/caches before.
set "STAGE_DIR=%TEMP%\bdtm-publish-%RANDOM%%RANDOM%"
set "RELEASES_DIR=releases"
set "ZIP_NAME=BooruDatasetTagManagerPlus-%VERSION%-win-x64.zip"
set "ZIP_PATH=%RELEASES_DIR%\%ZIP_NAME%"
set "NOTES=docs\RELEASE_NOTES_v%VERSION%.md"
set "TAG=v%VERSION%"

if not "%~1"=="" set "VERSION=%~1"
if not "%~1"=="" set "ZIP_NAME=BooruDatasetTagManagerPlus-%VERSION%-win-x64.zip"
if not "%~1"=="" set "ZIP_PATH=%RELEASES_DIR%\%ZIP_NAME%"
if not "%~1"=="" set "NOTES=docs\RELEASE_NOTES_v%VERSION%.md"
if not "%~1"=="" set "TAG=v%VERSION%"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK was not found. Install .NET 8 SDK first.
    exit /b 1
)

where gh >nul 2>nul
if errorlevel 1 (
    echo GitHub CLI ^(gh^) was not found. Install from https://cli.github.com/
    exit /b 1
)

gh auth status >nul 2>nul
if errorlevel 1 (
    echo GitHub CLI is not logged in. Run: gh auth login
    exit /b 1
)

echo Publishing BooruDatasetTagManagerPlus %TAG%...
echo.

mkdir "%STAGE_DIR%"
if not exist "%RELEASES_DIR%" mkdir "%RELEASES_DIR%"

if not exist "BooruDatasetTagManager\ThirdParty\ffmpeg\win-x64\ffmpeg.exe" (
    echo FFmpeg not found. Downloading bundled binaries...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fetch_ffmpeg.ps1"
    if errorlevel 1 (
        echo Failed to download FFmpeg.
        exit /b 1
    )
)

dotnet publish "%PROJECT%" -c "%CONFIG%" -f "%FRAMEWORK%" -r "%RUNTIME%" --self-contained true -o "%STAGE_DIR%" --nologo
if errorlevel 1 (
    echo Publish failed.
    rmdir /s /q "%STAGE_DIR%"
    exit /b 1
)

rem The tag/zip version must match what the EXE actually reports.
powershell -NoProfile -Command "$v=(Get-Item '%STAGE_DIR%\BooruDatasetTagManagerPlus.exe').VersionInfo.ProductVersion; if ($v -notlike '%VERSION%*') { Write-Host ('EXE ProductVersion ' + $v + ' does not match %VERSION%.'); exit 1 }"
if errorlevel 1 (
    echo Version check failed. Bump csproj/AssemblyInfo before releasing %VERSION%.
    rmdir /s /q "%STAGE_DIR%"
    exit /b 1
)

del /q "%STAGE_DIR%\*.pdb" "%STAGE_DIR%\*.lib" >nul 2>nul

if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE_DIR%\*' -DestinationPath '%ZIP_PATH%' -CompressionLevel Optimal"
if errorlevel 1 (
    echo Failed to create zip: %ZIP_PATH%
    rmdir /s /q "%STAGE_DIR%"
    exit /b 1
)
rmdir /s /q "%STAGE_DIR%"

if not exist "%NOTES%" (
    echo Release notes not found: %NOTES%
    echo Create the file or pass notes with gh release create manually.
    exit /b 1
)

rem Check every gh step separately: a failed upload used to be masked by the
rem following edit succeeding, and the script still reported success.
gh release view %TAG% --repo storyAura/BooruDatasetTagManagerPlus >nul 2>nul
if errorlevel 1 (
    gh release create %TAG% "%ZIP_PATH%" --repo storyAura/BooruDatasetTagManagerPlus --title "%TAG%" --notes-file "%NOTES%"
    if errorlevel 1 (
        echo GitHub release create failed.
        exit /b 1
    )
) else (
    gh release upload %TAG% "%ZIP_PATH%" --repo storyAura/BooruDatasetTagManagerPlus --clobber
    if errorlevel 1 (
        echo GitHub release upload failed.
        exit /b 1
    )
    gh release edit %TAG% --repo storyAura/BooruDatasetTagManagerPlus --notes-file "%NOTES%"
    if errorlevel 1 (
        echo GitHub release notes update failed.
        exit /b 1
    )
)

echo.
echo Release %TAG% published.
echo Asset: %ZIP_PATH%
echo URL: https://github.com/storyAura/BooruDatasetTagManagerPlus/releases/tag/%TAG%
exit /b 0
