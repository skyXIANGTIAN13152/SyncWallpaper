param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetRoot = Join-Path $RepositoryRoot 'assets'
$trayRoot = Join-Path $assetRoot 'TrayIcons'
New-Item -ItemType Directory -Force -Path $trayRoot | Out-Null

function New-Color([int]$a, [int]$r, [int]$g, [int]$b) {
    return [System.Drawing.Color]::FromArgb($a, $r, $g, $b)
}

function Draw-RoundedRectangle([System.Drawing.Graphics]$graphics, [System.Drawing.Pen]$pen, [System.Drawing.RectangleF]$rectangle, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($rectangle.Left, $rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($rectangle.Right - $diameter, $rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($rectangle.Right - $diameter, $rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rectangle.Left, $rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.DrawPath($pen, $path)
    $path.Dispose()
}

function Test-BackgroundPixel([System.Drawing.Color]$color) {
    $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
    return $maximum -lt 92
}

function Remove-EdgeBackground([string]$path) {
    $source = [System.Drawing.Bitmap]::new($path)
    $width = $source.Width
    $height = $source.Height
    $visited = [bool[]]::new($width * $height)
    $queue = [System.Collections.Generic.Queue[int]]::new()

    function Add-BackgroundPixel([int]$x, [int]$y) {
        if ($x -lt 0 -or $y -lt 0 -or $x -ge $width -or $y -ge $height) { return }
        $index = $y * $width + $x
        if ($visited[$index]) { return }
        if (-not (Test-BackgroundPixel $source.GetPixel($x, $y))) { return }
        $visited[$index] = $true
        $queue.Enqueue($index)
    }

    for ($x = 0; $x -lt $width; $x++) { Add-BackgroundPixel $x 0; Add-BackgroundPixel $x ($height - 1) }
    for ($y = 1; $y -lt ($height - 1); $y++) { Add-BackgroundPixel 0 $y; Add-BackgroundPixel ($width - 1) $y }
    while ($queue.Count -gt 0) {
        $index = $queue.Dequeue()
        $x = $index % $width
        $y = [Math]::Floor($index / $width)
        Add-BackgroundPixel ($x - 1) $y; Add-BackgroundPixel ($x + 1) $y
        Add-BackgroundPixel $x ($y - 1); Add-BackgroundPixel $x ($y + 1)
    }

    $result = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($x = 0; $x -lt $width; $x++) {
        for ($y = 0; $y -lt $height; $y++) {
            $color = $source.GetPixel($x, $y)
            $index = $y * $width + $x
            if ($visited[$index]) {
                $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
                $alpha = if ($maximum -lt 55) { 0 } else { [Math]::Min(255, ($maximum - 55) * 6) }
                $color = [System.Drawing.Color]::FromArgb($alpha, $color.R, $color.G, $color.B)
            }
            $result.SetPixel($x, $y, $color)
        }
    }
    $source.Dispose()
    return $result
}

function New-Frame([string]$state, [int]$size) {
    if ($state -eq 'taskbar-reference' -and $null -ne $script:ReferenceBitmap) {
        $referenceFrame = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $referenceGraphics = [System.Drawing.Graphics]::FromImage($referenceFrame)
        $referenceGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $referenceGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $referenceGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $referenceGraphics.Clear([System.Drawing.Color]::Transparent)
        $referenceGraphics.DrawImage($script:ReferenceBitmap, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
        $referenceGraphics.Dispose()
        return $referenceFrame
    }
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $scale = [float]($size / 64.0)

    switch ($state) {
        'paused' { $accent = New-Color 255 166 184 211; $dim = New-Color 220 101 120 148 }
        'error' { $accent = New-Color 255 255 79 78; $dim = New-Color 220 195 46 48 }
        default { $accent = New-Color 255 53 231 255; $dim = New-Color 220 26 139 204 }
    }
    $stroke = [Math]::Max(1.2, 4 * $scale)
    $ring = [System.Drawing.RectangleF]::new(5 * $scale, 5 * $scale, 54 * $scale, 54 * $scale)
    if ($size -ge 24) {
        $halo = [System.Drawing.Pen]::new((New-Color 35 $accent.R $accent.G $accent.B), [Math]::Max(2, 9 * $scale))
        $graphics.DrawEllipse($halo, $ring)
        $halo.Dispose()
    }

    $ringPen = [System.Drawing.Pen]::new($dim, $stroke)
    if ($state -eq 'recognizing') {
        $graphics.DrawArc($ringPen, $ring, -82, 286)
        $graphics.DrawArc($ringPen, $ring, 218, 22)
        $center = [System.Drawing.PointF]::new($ring.Left + $ring.Width / 2, $ring.Top + $ring.Height / 2)
        $radius = $ring.Width / 2
        $dotBrush = [System.Drawing.SolidBrush]::new($accent)
        foreach ($angle in @(-64, -48, -32, -16)) {
            $radians = $angle * [Math]::PI / 180
            $x = $center.X + [float][Math]::Cos($radians) * $radius
            $y = $center.Y + [float][Math]::Sin($radians) * $radius
            $dot = [Math]::Max(1.2, 2.6 * $scale)
            $graphics.FillEllipse($dotBrush, $x - $dot / 2, $y - $dot / 2, $dot, $dot)
        }
        $dotBrush.Dispose()
    }
    elseif ($state -eq 'error') {
        $graphics.DrawArc($ringPen, $ring, -48, 250)
        $graphics.DrawArc($ringPen, $ring, 238, 42)
    }
    else { $graphics.DrawEllipse($ringPen, $ring) }
    $ringPen.Dispose()

    $screenPen = [System.Drawing.Pen]::new($accent, [Math]::Max(1.3, 3.6 * $scale))
    $screenPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $secondaryPen = [System.Drawing.Pen]::new($dim, [Math]::Max(1.1, 3.1 * $scale))
    $secondaryPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    Draw-RoundedRectangle $graphics $screenPen ([System.Drawing.RectangleF]::new(15 * $scale, 24 * $scale, 28 * $scale, 19 * $scale)) ([Math]::Max(1, 3 * $scale))
    $graphics.DrawLine($screenPen, 29 * $scale, 43 * $scale, 29 * $scale, 49 * $scale)
    $graphics.DrawLine($screenPen, 23 * $scale, 49 * $scale, 35 * $scale, 49 * $scale)
    if ($state -eq 'error') { $secondaryPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash }
    Draw-RoundedRectangle $graphics $secondaryPen ([System.Drawing.RectangleF]::new(38 * $scale, 32 * $scale, 17 * $scale, 14 * $scale)) ([Math]::Max(1, 2 * $scale))
    $graphics.DrawLine($secondaryPen, 41 * $scale, 48 * $scale, 54 * $scale, 48 * $scale)
    $screenPen.Dispose(); $secondaryPen.Dispose()

    if ($state -eq 'paused') {
        $pause = [System.Drawing.SolidBrush]::new($accent)
        $graphics.FillRectangle($pause, 27 * $scale, 10 * $scale, [Math]::Max(1.5, 4.2 * $scale), 9 * $scale)
        $graphics.FillRectangle($pause, 35 * $scale, 10 * $scale, [Math]::Max(1.5, 4.2 * $scale), 9 * $scale)
        $pause.Dispose()
    }
    elseif ($state -eq 'error') {
        $warning = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(50 * $scale, 42 * $scale),
            [System.Drawing.PointF]::new(60 * $scale, 58 * $scale),
            [System.Drawing.PointF]::new(40 * $scale, 58 * $scale))
        $fill = [System.Drawing.SolidBrush]::new($accent)
        $graphics.FillPolygon($fill, $warning)
        $mark = [System.Drawing.Pen]::new((New-Color 255 36 21 34), [Math]::Max(1.2, 2.5 * $scale))
        $mark.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $mark.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($mark, 50 * $scale, 47 * $scale, 50 * $scale, 52 * $scale)
        $dark = [System.Drawing.SolidBrush]::new((New-Color 255 36 21 34))
        $graphics.FillEllipse($dark, 49 * $scale, 54 * $scale, [Math]::Max(1.4, 2.5 * $scale), [Math]::Max(1.4, 2.5 * $scale))
        $fill.Dispose(); $mark.Dispose(); $dark.Dispose()
    }
    elseif ($state -eq 'normal') {
        $dot = [System.Drawing.SolidBrush]::new($accent)
        $diameter = [Math]::Max(2, 8 * $scale)
        $graphics.FillEllipse($dot, 20 * $scale - $diameter / 2, 52 * $scale - $diameter / 2, $diameter, $diameter)
        $orbitDot = [Math]::Max(1.5, 4 * $scale)
        $graphics.FillEllipse($dot, 49 * $scale - $orbitDot / 2, 12 * $scale - $orbitDot / 2, $orbitDot, $orbitDot)
        $dot.Dispose()
    }
    $graphics.Dispose()
    return $bitmap
}

function Get-PngBytes([System.Drawing.Bitmap]$bitmap) {
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

function Write-Ico([string]$path, [string]$state, [int[]]$sizes) {
    $frames = @()
    foreach ($size in $sizes) {
        $frame = New-Frame $state $size
        try { $frames += ,(Get-PngBytes $frame) } finally { $frame.Dispose() }
    }
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $size = $sizes[$i]
        $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([Byte]0); $writer.Write([Byte]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
    $writer.Flush(); $writer.Dispose(); $stream.Dispose()
}

$sizes = @(16, 20, 24, 32, 48, 64)
foreach ($state in @('normal', 'paused', 'recognizing', 'error')) {
    foreach ($size in $sizes) {
        $frame = New-Frame $state $size
        try { $frame.Save((Join-Path $trayRoot ("tray_{0}_{1}.png" -f $state, $size)), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $frame.Dispose() }
    }
    Write-Ico (Join-Path $trayRoot ("tray_{0}.ico" -f $state)) $state $sizes
}

$referencePath = Join-Path $assetRoot 'TaskbarIconReference.png'
if (Test-Path -LiteralPath $referencePath) {
    $script:ReferenceBitmap = Remove-EdgeBackground $referencePath
    $transparentReferencePath = Join-Path $assetRoot 'TaskbarIconReferenceTransparent.png'
    $script:ReferenceBitmap.Save($transparentReferencePath, [System.Drawing.Imaging.ImageFormat]::Png)
}
else {
    $script:ReferenceBitmap = $null
}

foreach ($size in @(16, 32, 64, 256)) {
    $frame = New-Frame 'taskbar-reference' $size
    try { $frame.Save((Join-Path $assetRoot ("AppIcon_{0}.png" -f $size)), [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $frame.Dispose() }
}
Write-Ico (Join-Path $assetRoot 'AppIcon.ico') 'taskbar-reference' @(16, 20, 24, 32, 48, 64, 256)
if ($null -ne $script:ReferenceBitmap) { $script:ReferenceBitmap.Dispose() }
Write-Output "Generated tray assets in $trayRoot and taskbar AppIcon.ico."
