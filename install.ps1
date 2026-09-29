<#
.SYNOPSIS
    TouchSwitcher for Windows - One-Line Terminal Installer
.DESCRIPTION
    Installs TouchSwitcher onto the system, creates Desktop/Startup shortcuts,
    and starts the utility.
    Usage:
        irm https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.ps1 | iex
#>

param(
    [switch]$Uninstall,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"

$RepoOwner = "MohammadMohid03"
$RepoName  = "Touch_Switcher"
$TargetDir = Join-Path $env:LOCALAPPDATA "TouchSwitcher"
$ExePath   = Join-Path $TargetDir "TouchSwitcher.exe"

function Write-Step {
    param([string]$Message)
    Write-Host " [>] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host " [✓] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host " [!] $Message" -ForegroundColor Yellow
}

function Write-Banner {
    Write-Host ""
    Write-Host " =====================================================" -ForegroundColor DarkCyan
    Write-Host "          TouchSwitcher for Windows Installer         " -ForegroundColor Cyan
    Write-Host " =====================================================" -ForegroundColor DarkCyan
    Write-Host ""
}

Write-Banner

# -------------------------------------------------------------
# Handle Uninstallation
# -------------------------------------------------------------
if ($Uninstall) {
    Write-Step "Uninstalling TouchSwitcher..."
    
    Get-Process -Name "TouchSwitcher" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    $startupLnk = Join-Path ([Environment]::GetFolderPath("Startup")) "TouchSwitcher.lnk"
    $desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "Touch Switcher.lnk"
    $startMenuLnk = Join-Path ([Environment]::GetFolderPath("Programs")) "Touch Switcher.lnk"

    @($startupLnk, $desktopLnk, $startMenuLnk) | ForEach-Object {
        if (Test-Path $_) {
            Remove-Item $_ -Force -ErrorAction SilentlyContinue
            Write-Success "Removed shortcut: $_"
        }
    }

    if (Test-Path $TargetDir) {
        Remove-Item $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Success "Removed directory: $TargetDir"
    }

    Write-Host ""
    Write-Success "TouchSwitcher has been completely removed from your system."
    return
}

# -------------------------------------------------------------
# Stop existing instance if running
# -------------------------------------------------------------
$running = Get-Process -Name "TouchSwitcher" -ErrorAction SilentlyContinue
if ($running) {
    Write-Step "Stopping running TouchSwitcher instance..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# -------------------------------------------------------------
# Prepare Target Directory
# -------------------------------------------------------------
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
}

# -------------------------------------------------------------
# Download Binary from GitHub Releases
# -------------------------------------------------------------
$zipUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/TouchSwitcher-Windows.zip"
$exeUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/TouchSwitcher.exe"
$tempZip = Join-Path $env:TEMP "TouchSwitcher-Windows.zip"

$downloaded = $false

Write-Step "Downloading latest TouchSwitcher release..."
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
} catch {}

# Attempt 1: Compressed ZIP package
try {
    Invoke-WebRequest -Uri $zipUrl -OutFile $tempZip -UseBasicParsing -TimeoutSec 60
    if ((Test-Path $tempZip) -and ((Get-Item $tempZip).Length -gt 1000000)) {
        Write-Step "Extracting files to $TargetDir..."
        Expand-Archive -Path $tempZip -DestinationPath $TargetDir -Force
        Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
        $downloaded = $true
        Write-Success "Downloaded and extracted release package."
    }
} catch {
    Write-Warn "Zip download did not succeed, trying direct executable..."
}

# Attempt 2: Direct executable download
if (-not $downloaded) {
    try {
        Invoke-WebRequest -Uri $exeUrl -OutFile $ExePath -UseBasicParsing -TimeoutSec 120
        if ((Test-Path $ExePath) -and ((Get-Item $ExePath).Length -gt 1000000)) {
            $downloaded = $true
            Write-Success "Downloaded executable directly."
        }
    } catch {
        Write-Warn "Direct executable download did not succeed."
    }
}

# Attempt 3: Local build fallback if dotnet SDK is installed
if (-not $downloaded) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Step ".NET SDK detected! Building from source..."
        $srcZip = Join-Path $env:TEMP "TouchSwitcher-src.zip"
        $srcDir = Join-Path $env:TEMP "TouchSwitcher-src"
        $archiveUrl = "https://github.com/$RepoOwner/$RepoName/archive/refs/heads/main.zip"
        
        Invoke-WebRequest -Uri $archiveUrl -OutFile $srcZip -UseBasicParsing
        Expand-Archive -Path $srcZip -DestinationPath $srcDir -Force
        
        $proj = Join-Path $srcDir "Touch_Switcher-main\src\TouchSwitcher.App\TouchSwitcher.App.csproj"
        dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $TargetDir
        
        Remove-Item $srcZip -Force -ErrorAction SilentlyContinue
        Remove-Item $srcDir -Recurse -Force -ErrorAction SilentlyContinue
        
        if (Test-Path $ExePath) {
            $downloaded = $true
            Write-Success "Built and installed successfully from source."
        }
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Host ""
    Write-Host " [X] Installation failed: Could not download or build TouchSwitcher.exe" -ForegroundColor Red
    Write-Host "     Please check your internet connection or install manually from:" -ForegroundColor Red
    Write-Host "     https://github.com/$RepoOwner/$RepoName/releases" -ForegroundColor Red
    Exit 1
}

# -------------------------------------------------------------
# Create Shortcuts
# -------------------------------------------------------------
Write-Step "Creating desktop and startup shortcuts..."
try {
    $ws = New-Object -ComObject WScript.Shell
    
    # 1. Startup folder shortcut (runs automatically on login)
    $startupLnk = Join-Path ([Environment]::GetFolderPath("Startup")) "TouchSwitcher.lnk"
    $scStartup = $ws.CreateShortcut($startupLnk)
    $scStartup.TargetPath = $ExePath
    $scStartup.WorkingDirectory = $TargetDir
    $scStartup.Description = "TouchSwitcher background touchpad utility"
    $scStartup.Save()

    # 2. Desktop shortcut
    $desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "Touch Switcher.lnk"
    $scDesktop = $ws.CreateShortcut($desktopLnk)
    $scDesktop.TargetPath = $ExePath
    $scDesktop.WorkingDirectory = $TargetDir
    $scDesktop.Description = "Touch Switcher"
    $scDesktop.Save()

    # 3. Start Menu shortcut
    $startMenuDir = [Environment]::GetFolderPath("Programs")
    $startMenuLnk = Join-Path $startMenuDir "Touch Switcher.lnk"
    $scStart = $ws.CreateShortcut($startMenuLnk)
    $scStart.TargetPath = $ExePath
    $scStart.WorkingDirectory = $TargetDir
    $scStart.Description = "Touch Switcher"
    $scStart.Save()

    Write-Success "Shortcuts created (Startup, Desktop, Start Menu)."
} catch {
    Write-Warn "Could not create shortcuts: $_"
}

# -------------------------------------------------------------
# Launch TouchSwitcher
# -------------------------------------------------------------
if (-not $NoStart) {
    Write-Step "Starting TouchSwitcher..."
    Start-Process -FilePath $ExePath -WorkingDirectory $TargetDir
    Write-Success "TouchSwitcher is now running in your system tray!"
}

# -------------------------------------------------------------
# Guide & Complete
# -------------------------------------------------------------
Write-Host ""
Write-Host " =====================================================" -ForegroundColor DarkCyan
Write-Host "             Installation Complete!                   " -ForegroundColor Green
Write-Host " =====================================================" -ForegroundColor DarkCyan
Write-Host ""
Write-Host " Location:  $TargetDir" -ForegroundColor Gray
Write-Host ""
Write-Host " GESTURES:" -ForegroundColor Cyan
Write-Host "   3 Fingers Left/Right : Switch between applications"
Write-Host "   3 Fingers Rest/Hold  : HUD preview (left, current, right app)"
Write-Host "   3 Fingers Up         : Open Windows Task View & Virtual Desktops"
Write-Host "   3 Fingers Down       : Dismiss Task View / Return to desktop"
Write-Host ""
Write-Host " RECOMMENDED WINDOWS SETTING:" -ForegroundColor Yellow
Write-Host "   Open Windows Settings -> Bluetooth & devices -> Touchpad"
Write-Host "   Under 'Three-finger gestures', set Swipes to 'Nothing' or 'Off'"
Write-Host "   so Windows doesn't conflict with TouchSwitcher."
Write-Host ""
