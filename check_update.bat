@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

echo ================================================
echo  BooruDatasetTagManagerPlus - Check for updates
echo ================================================
echo.

rem A .git entry next to the solution file means we run from a source checkout;
rem anything else is treated as an extracted release package.
if exist ".git" if exist "BooruDatasetTagManager.sln" goto :source_mode
goto :release_mode

:source_mode
where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Source checkout detected but git is not on PATH.
    echo         Install Git from https://git-scm.com/ and retry.
    goto :done
)
echo Source checkout detected. Running "git pull --ff-only"...
echo.
git pull --ff-only
if errorlevel 1 (
    echo.
    echo [WARN] git pull failed. Resolve local changes or branch divergence manually.
)
goto :done

:release_mode
echo Release install detected. Querying the latest GitHub release...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { };" ^
  "$rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/storyAura/BooruDatasetTagManagerPlus/releases/latest' -Headers @{ 'User-Agent' = 'BDTM-check-update' };" ^
  "$tag = [string]$rel.tag_name;" ^
  "$remote = [Version]($tag.TrimStart('v'));" ^
  "$local = [Version]'0.0.0';" ^
  "$exe = Join-Path (Get-Location) 'BooruDatasetTagManagerPlus.exe';" ^
  "if (Test-Path $exe) { $fv = (Get-Item $exe).VersionInfo.FileVersion; if ($fv) { $local = [Version]$fv } };" ^
  "Write-Host ('Local version : ' + $local);" ^
  "Write-Host ('Latest release: ' + $tag);" ^
  "if ($remote -le $local) { Write-Host ''; Write-Host 'Already up to date.'; exit 0 };" ^
  "$asset = $rel.assets | Where-Object { $_.name -like '*win-x64*.zip' } | Select-Object -First 1;" ^
  "if (-not $asset) { $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1 };" ^
  "if (-not $asset) { Write-Host ('No zip asset found; open: ' + $rel.html_url); exit 1 };" ^
  "$out = Join-Path (Get-Location) $asset.name;" ^
  "Write-Host ('Downloading ' + $asset.name + ' (' + [math]::Round($asset.size / 1MB, 1) + ' MB)...');" ^
  "Invoke-WebRequest -Uri $asset.browser_download_url -OutFile ($out + '.partial') -UseBasicParsing;" ^
  "Move-Item -Force ($out + '.partial') $out;" ^
  "Write-Host '';" ^
  "Write-Host ('Saved to: ' + $out);" ^
  "Write-Host 'Close the app, then extract the zip over the old files.'"
if errorlevel 1 (
    echo.
    echo [WARN] Update check or download failed. Visit:
    echo        https://github.com/storyAura/BooruDatasetTagManagerPlus/releases
)
goto :done

:done
echo.
pause
exit /b
