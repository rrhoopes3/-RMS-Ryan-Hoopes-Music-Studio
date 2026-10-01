$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "RMS — restore, test, sample project, publish"
dotnet restore RyanMusicStudio.sln
dotnet test RyanMusicStudio.sln -c Release --nologo
dotnet run --project src\RyanMusicStudio.Spike\RyanMusicStudio.Spike.csproj -c Release -- --list-only

$sample = Join-Path $root "samples\vocal-over-beat"
dotnet run --project src\RyanMusicStudio.Spike\RyanMusicStudio.Spike.csproj -c Release -- --write-sample "$sample"

$out = Join-Path $root "dist\RMS-portable"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src\RyanMusicStudio.App\RyanMusicStudio.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $out

Write-Host "Portable build: $out\RMS.exe"
