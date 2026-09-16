# Generates assets/app.ico (multi-size icon) using only System.Drawing.
# NOTE: keep this file ASCII-only - Windows PowerShell 5.1 reads non-BOM
# script files with the system ANSI code page and mangles non-ASCII text.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets = Join-Path $root 'assets'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }
$target = Join-Path $assets 'app.ico'

function New-RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size
    $pad = $s * 0.055
    $inner = $s - 2 * $pad
    $path = New-RoundRect $pad $pad $inner $inner ($inner * 0.26)

    $c1 = [System.Drawing.Color]::FromArgb(255, 56, 189, 248)
    $c2 = [System.Drawing.Color]::FromArgb(255, 99, 102, 241)
    $p1 = New-Object System.Drawing.PointF -ArgumentList 0, 0
    $p2 = New-Object System.Drawing.PointF -ArgumentList $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $p1, $p2, $c1, $c2
    $g.FillPath($brush, $path)

    $g1 = New-Object System.Drawing.PointF -ArgumentList 0, 0
    $g2 = New-Object System.Drawing.PointF -ArgumentList 0, ($s * 0.55)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $g1, $g2,
        ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($hl, $path)

    $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))

    $headW = $s * 0.200
    $headH = $s * 0.150
    $stemW = $s * 0.052
    $x1 = $s * 0.268
    $x2 = $s * 0.582
    $yTop = $s * 0.255
    $y1 = $s * 0.638
    $y2 = $y1 - $s * 0.062
    $stemX1 = $x1 + $headW * 0.74
    $stemX2 = $x2 + $headW * 0.74

    # stems
    $g.FillRectangle($white, [float]$stemX1, [float]$yTop, [float]$stemW, [float]($y1 + $headH * 0.5 - $yTop))
    $g.FillRectangle($white, [float]$stemX2, [float]$yTop, [float]$stemW, [float]($y2 + $headH * 0.5 - $yTop))

    # note heads (slightly rotated ellipses)
    $m1 = New-Object System.Drawing.Drawing2D.Matrix
    $c1p = New-Object System.Drawing.PointF -ArgumentList ([float]($x1 + $headW / 2)), ([float]($y1 + $headH / 2))
    $m1.RotateAt(-20, $c1p)
    $g.Transform = $m1
    $g.FillEllipse($white, [float]$x1, [float]$y1, [float]$headW, [float]$headH)
    $g.ResetTransform()

    $m2 = New-Object System.Drawing.Drawing2D.Matrix
    $c2p = New-Object System.Drawing.PointF -ArgumentList ([float]($x2 + $headW / 2)), ([float]($y2 + $headH / 2))
    $m2.RotateAt(-20, $c2p)
    $g.Transform = $m2
    $g.FillEllipse($white, [float]$x2, [float]$y2, [float]$headW, [float]$headH)
    $g.ResetTransform()

    # beam connecting the stems
    $beam = New-Object System.Drawing.Drawing2D.GraphicsPath
    $b1 = New-Object System.Drawing.PointF -ArgumentList ([float]$stemX1), ([float]$yTop)
    $b2 = New-Object System.Drawing.PointF -ArgumentList ([float]($stemX2 + $stemW)), ([float]($yTop - $s * 0.062))
    $b3 = New-Object System.Drawing.PointF -ArgumentList ([float]($stemX2 + $stemW)), ([float]($yTop - $s * 0.062 + $s * 0.105))
    $b4 = New-Object System.Drawing.PointF -ArgumentList ([float]$stemX1), ([float]($yTop + $s * 0.105))
    $beam.AddPolygon(@($b1, $b2, $b3, $b4))
    $g.FillPath($white, $beam)

    $g.Dispose()
    return $bmp
}

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
