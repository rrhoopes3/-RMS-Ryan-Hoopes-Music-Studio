$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "dist\RMS-portable"
if (-not (Test-Path (Join-Path $src "RMS.exe"))) {
    throw "Build first: powershell -File scripts/build-windows.ps1"
}

$dest = Join-Path $env:LOCALAPPDATA "RMS"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $src "*") -Destination $dest -Recurse -Force

$programs = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $programs | Out-Null
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut((Join-Path $programs "RMS.lnk"))
$lnk.TargetPath = Join-Path $dest "RMS.exe"
$lnk.WorkingDirectory = $dest
$lnk.Description = "RMS — Ryan Music Studio"
$lnk.Save()

Write-Host "Installed RMS to $dest"
Write-Host "Start Menu shortcut: RMS"
