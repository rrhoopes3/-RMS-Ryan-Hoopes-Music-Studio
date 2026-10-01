$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Dotnet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host "RMS — restore, test, sample project, publish"
Invoke-Dotnet -Arguments @("restore", "RyanMusicStudio.sln")
Invoke-Dotnet -Arguments @("test", "RyanMusicStudio.sln", "-c", "Release", "--nologo")
Invoke-Dotnet -Arguments @("run", "--project", "src\RyanMusicStudio.Spike\RyanMusicStudio.Spike.csproj", "-c", "Release", "--", "--list-only")

$sample = Join-Path $root "samples\vocal-over-beat"
Invoke-Dotnet -Arguments @("run", "--project", "src\RyanMusicStudio.Spike\RyanMusicStudio.Spike.csproj", "-c", "Release", "--", "--write-sample", $sample)

$out = Join-Path $root "dist\RMS-portable"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Invoke-Dotnet -Arguments @(
    "publish", "src\RyanMusicStudio.App\RyanMusicStudio.App.csproj",
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $out
)

$exe = Join-Path $out "RMS.exe"
if (-not (Test-Path $exe -PathType Leaf)) {
    throw "Publish completed without creating $exe."
}
Write-Host "Portable build: $exe"
