$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'BooruDatasetTagManager\ThirdParty\ffmpeg\win-x64'
$zip = Join-Path $root 'BooruDatasetTagManager\ThirdParty\ffmpeg.zip'
$tmp = Join-Path $root 'BooruDatasetTagManager\ThirdParty\ffmpeg\_tmp'

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Write-Host "Downloading FFmpeg..."
Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' -OutFile $zip
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $tmp -Force
$ffmpeg = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $ffmpeg) { throw 'ffmpeg.exe not found in archive' }
Copy-Item $ffmpeg.FullName (Join-Path $dest 'ffmpeg.exe') -Force
$probe = Join-Path $ffmpeg.DirectoryName 'ffprobe.exe'
Copy-Item $probe (Join-Path $dest 'ffprobe.exe') -Force
Remove-Item $zip -Force
Remove-Item $tmp -Recurse -Force
Get-ChildItem $dest | Format-Table Name, Length
