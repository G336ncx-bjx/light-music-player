# Builds the player and creates a desktop shortcut.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\install.ps1
param(
    [switch]$NoBuild,
    [switch]$NoShortcut,
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root 'dist\Skylark.exe'

if (-not $NoBuild) {
    & (Join-Path $root 'build.ps1')
}
if (-not (Test-Path $exe)) { throw "not found: $exe" }

if (-not $NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath('Desktop')
    $link = Join-Path $desktop '云雀.lnk'
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = Split-Path -Parent $exe
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = '云雀 Skylark - 轻量级云端音乐播放器'
    $shortcut.Save()
    Write-Host "desktop shortcut: $link" -ForegroundColor Green

    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) '云雀.lnk'
    try {
        $shortcut2 = $shell.CreateShortcut($startMenu)
        $shortcut2.TargetPath = $exe
        $shortcut2.WorkingDirectory = Split-Path -Parent $exe
        $shortcut2.IconLocation = "$exe,0"
        $shortcut2.Description = '云雀 Skylark - 轻量级云端音乐播放器'
        $shortcut2.Save()
        Write-Host "start menu shortcut: $startMenu" -ForegroundColor DarkGray
    } catch {
        Write-Host "start menu shortcut skipped: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

if ($Start) { Start-Process $exe }
Write-Host "done." -ForegroundColor Green
