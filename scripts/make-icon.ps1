# Generates assets/app.ico (multi-size icon) using only System.Drawing.
# NOTE: keep this file ASCII-only - Windows PowerShell 5.1 reads non-BOM
# script files with the system ANSI code page and mangles non-ASCII text.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'icon-artwork.ps1')

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets = Join-Path $root 'assets'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }
$target = Join-Path $assets 'app.ico'

function Get-BmpEntryBytes($bmp) {
    $w = $bmp.Width
    $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter -ArgumentList $ms
    $bw.Write([int]40)
    $bw.Write([int]$w)
    $bw.Write([int]($h * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B)
            $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R)
            $bw.Write([byte]$c.A)
        }
    }
    $rowSize = [int]([math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($rowSize * $h)))
    $bw.Flush()
    return , $ms.ToArray()
}

function Get-PngEntryBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$specs = @(
    @{ Size = 16; Mode = 'bmp' },
    @{ Size = 24; Mode = 'bmp' },
    @{ Size = 32; Mode = 'bmp' },
    @{ Size = 48; Mode = 'bmp' },
    @{ Size = 64; Mode = 'png' },
    @{ Size = 128; Mode = 'png' },
    @{ Size = 256; Mode = 'png' }
)

$entries = @()
foreach ($spec in $specs) {
    $bmp = New-IconBitmap $spec.Size
    if ($spec.Mode -eq 'bmp') { $data = Get-BmpEntryBytes $bmp } else { $data = Get-PngEntryBytes $bmp }
    $entries += [pscustomobject]@{ Size = $spec.Size; Data = [byte[]]$data }
    $bmp.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter -ArgumentList $ms
$bw.Write([int16]0)
$bw.Write([int16]1)
$bw.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = 0
    if ($e.Size -lt 256) { $dim = $e.Size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]$e.Data.Length)
    $bw.Write([int]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) {
    $bytes = [byte[]]$e.Data
    $bw.Write($bytes, 0, $bytes.Length)
}
$bw.Flush()
$out = [byte[]]$ms.ToArray()
if ($out.Length -ne $offset) { throw "icon size mismatch: $($out.Length) != $offset" }
[System.IO.File]::WriteAllBytes($target, $out)

Write-Host ("icon written: {0} ({1} bytes)" -f $target, $out.Length)
