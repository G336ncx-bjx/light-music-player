# Shared icon artwork for both platforms (dot-source this file).
# Windows: assets/app.ico (scripts/make-icon.ps1)
# Android: mipmap PNGs  (scripts/make-android-icon.ps1)
# NOTE: keep this file ASCII-only - Windows PowerShell 5.1 reads non-BOM
# script files with the system ANSI code page and mangles non-ASCII text.
Add-Type -AssemblyName System.Drawing

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

# The white beamed note, drawn inside a square of side $s at the origin.
function Add-NoteShape($g, $white, [double]$s) {
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
    # NOTE: save/restore instead of ResetTransform(), so that a caller's
    # translation/scale is kept (ResetTransform would drop it).
    $c1p = New-Object System.Drawing.PointF -ArgumentList ([float]($x1 + $headW / 2)), ([float]($y1 + $headH / 2))
    $state = $g.Save()
    $g.TranslateTransform([float]$c1p.X, [float]$c1p.Y)
    $g.RotateTransform(-20)
    $g.TranslateTransform([float](-$c1p.X), [float](-$c1p.Y))
    $g.FillEllipse($white, [float]$x1, [float]$y1, [float]$headW, [float]$headH)
    $g.Restore($state)

    $c2p = New-Object System.Drawing.PointF -ArgumentList ([float]($x2 + $headW / 2)), ([float]($y2 + $headH / 2))
    $state = $g.Save()
    $g.TranslateTransform([float]$c2p.X, [float]$c2p.Y)
    $g.RotateTransform(-20)
    $g.TranslateTransform([float](-$c2p.X), [float](-$c2p.Y))
    $g.FillEllipse($white, [float]$x2, [float]$y2, [float]$headW, [float]$headH)
    $g.Restore($state)

    # beam connecting the stems
    $beam = New-Object System.Drawing.Drawing2D.GraphicsPath
    $b1 = New-Object System.Drawing.PointF -ArgumentList ([float]$stemX1), ([float]$yTop)
    $b2 = New-Object System.Drawing.PointF -ArgumentList ([float]($stemX2 + $stemW)), ([float]($yTop - $s * 0.062))
    $b3 = New-Object System.Drawing.PointF -ArgumentList ([float]($stemX2 + $stemW)), ([float]($yTop - $s * 0.062 + $s * 0.105))
    $b4 = New-Object System.Drawing.PointF -ArgumentList ([float]$stemX1), ([float]($yTop + $s * 0.105))
    # Cast to PointF[] explicitly: passing @(...) makes PowerShell try the
    # Point[] overload first and fail with a conversion error.
    $points = [System.Drawing.PointF[]]@($b1, $b2, $b3, $b4)
    $beam.AddPolygon($points)
    $g.FillPath($white, $beam)
}

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# Full application icon: sky-blue -> indigo rounded square + white note.
function New-IconBitmap([int]$size) {
    $canvas = New-Canvas $size
    $bmp = $canvas[0]
    $g = $canvas[1]

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
    Add-NoteShape $g $white $s

    $g.Dispose()
    return $bmp
}

# Note only, transparent background - used as the Android adaptive icon foreground.
function New-NoteBitmap([int]$size, [double]$scale) {
    $canvas = New-Canvas $size
    $bmp = $canvas[0]
    $g = $canvas[1]

    $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $box = [double]$size * $scale
    $offset = ([double]$size - $box) / 2.0
    $g.TranslateTransform([float]$offset, [float]$offset)
    Add-NoteShape $g $white $box
    $g.ResetTransform()

    $g.Dispose()
    return $bmp
}
