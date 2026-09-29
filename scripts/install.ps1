$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts"
$exe = Join-Path $publishDir "TouchSwitcher.exe"
if (-not (Test-Path $exe)) {
    & (Join-Path $PSScriptRoot "publish.ps1")
}

$target = Join-Path $env:LOCALAPPDATA "TouchSwitcher"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $exe (Join-Path $target "TouchSwitcher.exe") -Force

$ws = New-Object -ComObject WScript.Shell

# 1. Startup folder shortcut
$startupPath = Join-Path ([Environment]::GetFolderPath("Startup")) "TouchSwitcher.lnk"
$startupShortcut = $ws.CreateShortcut($startupPath)
$startupShortcut.TargetPath = Join-Path $target "TouchSwitcher.exe"
$startupShortcut.WorkingDirectory = $target
$startupShortcut.Save()

# 2. Desktop shortcut
$desktopPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "Touch Switcher.lnk"
$desktopShortcut = $ws.CreateShortcut($desktopPath)
$desktopShortcut.TargetPath = Join-Path $target "TouchSwitcher.exe"
$desktopShortcut.WorkingDirectory = $target
$desktopShortcut.Description = "Touch Switcher (3-finger touchpad app switcher)"
$desktopShortcut.Save()

Write-Host "Installed to $target"
Write-Host "Created Startup shortcut: $startupPath"
Write-Host "Created Desktop shortcut: $desktopPath"
