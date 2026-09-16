# Generates the Android launcher icons (mipmap PNGs).
# The artwork is shared with the Windows exe: both come from scripts/icon-artwork.ps1.
#   ic_launcher.png            legacy icon (sky-blue -> indigo rounded square + note)
#   ic_launcher_foreground.png adaptive icon foreground (transparent, note only)
# NOTE: keep this file ASCII-only - Windows PowerShell 5.1 reads non-BOM
# script files with the system ANSI code page and mangles non-ASCII text.
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'icon-artwork.ps1')

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$res  = Join-Path $root 'android\res'

function Save-Png($bitmap, [string]$file) {
    $dir = Split-Path -Parent $file
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ("  {0}  {1} x {1}" -f (Split-Path $file -Leaf), $bitmap.Width)
    $bitmap.Dispose()
}

$densities = [ordered]@{
    'mipmap-mdpi'    = 48
    'mipmap-hdpi'    = 72
    'mipmap-xhdpi'   = 96
    'mipmap-xxhdpi'  = 144
    'mipmap-xxxhdpi' = 192
}

Write-Output 'legacy icon: ic_launcher.png (same artwork as the Windows exe)'
foreach ($name in $densities.Keys) {
    Save-Png (New-IconBitmap $densities[$name]) (Join-Path $res "$name\ic_launcher.png")
}

Write-Output 'adaptive icon foreground: ic_launcher_foreground.png'
foreach ($name in @('mipmap-xxhdpi', 'mipmap-xxxhdpi')) {
    $size = $densities[$name] * 3
    # Adaptive icon layers are 108dp and the system only shows the middle 72dp
    # (masked, and only the inner 66dp is guaranteed). The note is scaled so its
    # bounding box diagonal fits that safe circle.
    Save-Png (New-NoteBitmap $size 0.77) (Join-Path $res "$name\ic_launcher_foreground.png")
}

Write-Output 'android icons written.'
