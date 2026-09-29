$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish (Join-Path $root "src\TouchSwitcher.App\TouchSwitcher.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $out

Write-Host "Published: $out\TouchSwitcher.exe"
