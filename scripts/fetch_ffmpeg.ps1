$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'BooruDatasetTagManager\ThirdParty\ffmpeg\win-x64'
$zip = Join-Path $root 'BooruDatasetTagManager\ThirdParty\ffmpeg.zip'
$tmp = Join-Path $root 'BooruDatasetTagManager\ThirdParty\ffmpeg\_tmp'

# Pinned BtbN autobuild (ffmpeg release branch n8.1.2), NOT the rolling
# "latest" tag: what ships in the package must be a known, reviewable build.
# To upgrade: pick a newer autobuild tag + asset from
# https://github.com/BtbN/FFmpeg-Builds/releases and update both lines.
$tag = 'autobuild-2026-07-21-13-38'
$asset = 'ffmpeg-n8.1.2-29-g703dcc25b9-win64-gpl-8.1.zip'
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$tag/$asset"

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Write-Host "Downloading FFmpeg ($asset)..."
Invoke-WebRequest -Uri $url -OutFile $zip
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
# Expand-Archive throws on a corrupt/HTML "zip", failing the build loudly.
Expand-Archive -Path $zip -DestinationPath $tmp -Force
$ffmpeg = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $ffmpeg) { throw 'ffmpeg.exe not found in archive' }
$probe = Join-Path $ffmpeg.DirectoryName 'ffprobe.exe'
if (-not (Test-Path $probe)) { throw 'ffprobe.exe not found in archive' }
Copy-Item $ffmpeg.FullName (Join-Path $dest 'ffmpeg.exe') -Force
Copy-Item $probe (Join-Path $dest 'ffprobe.exe') -Force
foreach ($name in 'ffmpeg.exe', 'ffprobe.exe') {
    $file = Get-Item (Join-Path $dest $name)
    if ($file.Length -lt 1MB) { throw "$name is suspiciously small ($($file.Length) bytes) - refusing to bundle it" }
}
Remove-Item $zip -Force
Remove-Item $tmp -Recurse -Force
Get-ChildItem $dest | Format-Table Name, Length
