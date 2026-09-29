$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
$proj = Join-Path $root "src\TouchSwitcher.App\TouchSwitcher.App.csproj"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$standaloneTemp = Join-Path $out "temp_standalone"
$lightweightTemp = Join-Path $out "temp_lightweight"

# 1. Build Compressed Self-Contained Windows Executable (No .NET required on user machine)
Write-Host "--> Building Compressed Self-Contained Windows Executable..." -ForegroundColor Cyan
dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $standaloneTemp

Copy-Item (Join-Path $standaloneTemp "TouchSwitcher.exe") (Join-Path $out "TouchSwitcher.exe") -Force
if (Test-Path (Join-Path $standaloneTemp "TouchSwitcher.pdb")) {
    Copy-Item (Join-Path $standaloneTemp "TouchSwitcher.pdb") (Join-Path $out "TouchSwitcher.pdb") -Force
}
Remove-Item -Recurse -Force $standaloneTemp

# 2. Build Framework-Dependent Lightweight Windows Executable (~250 KB)
Write-Host "--> Building Framework-Dependent Lightweight Windows Executable..." -ForegroundColor Cyan
dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o $lightweightTemp

Copy-Item (Join-Path $lightweightTemp "TouchSwitcher.exe") (Join-Path $out "TouchSwitcher-FrameworkDependent.exe") -Force
Remove-Item -Recurse -Force $lightweightTemp

# 3. Create Windows ZIP archives
Write-Host "--> Packaging Windows Archives..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $out "TouchSwitcher.exe") -DestinationPath (Join-Path $out "TouchSwitcher-Windows.zip") -Force
Compress-Archive -Path (Join-Path $out "TouchSwitcher-FrameworkDependent.exe") -DestinationPath (Join-Path $out "TouchSwitcher-FrameworkDependent.zip") -Force

# 4. Prepare Linux Executable and Package
Write-Host "--> Packaging Linux Executable and Distribution..." -ForegroundColor Cyan
$linuxDir = Join-Path $root "linux"
$linuxExe = Join-Path $out "touchswitcher"

# Create Linux executable without .exe extension and LF line endings
$pyContent = [System.IO.File]::ReadAllText((Join-Path $linuxDir "touchswitcher.py")).Replace("`r`n", "`n")
[System.IO.File]::WriteAllText($linuxExe, $pyContent, (New-Object System.Text.UTF8Encoding($false)))

# Package Linux files into zip
$linuxTemp = Join-Path $out "temp_linux"
New-Item -ItemType Directory -Force -Path $linuxTemp | Out-Null
Copy-Item (Join-Path $linuxDir "*") $linuxTemp -Force
Copy-Item $linuxExe (Join-Path $linuxTemp "touchswitcher") -Force
Compress-Archive -Path (Join-Path $linuxTemp "*") -DestinationPath (Join-Path $out "TouchSwitcher-Linux.zip") -Force
Remove-Item -Recurse -Force $linuxTemp

Write-Host ""
Write-Host "=== Build Completed Successfully! ===" -ForegroundColor Green
Get-ChildItem $out | Select-Object Name, @{Name="Size (MB)";Expression={[math]::Round($_.Length / 1MB, 2)}} | Format-Table -AutoSize
