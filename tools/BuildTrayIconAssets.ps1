param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('normal', 'generic', 'unmatched', 'paused', 'error', 'recognizing')]
    [string[]]$States = @('normal', 'generic', 'unmatched', 'paused', 'error', 'recognizing'),
    [switch]$SkipTaskbarAssets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $RepositoryRoot 'assets'
$trayRoot = Join-Path $assetRoot 'TrayIcons'
New-Item -ItemType Directory -Force -Path $trayRoot | Out-Null
$gold = [System.Drawing.ColorTranslator]::FromHtml('#D2B47A')
$white = [System.Drawing.ColorTranslator]::FromHtml('#F3EBDC')
$sage = [System.Drawing.ColorTranslator]::FromHtml('#A5B5A9')
$copper = [System.Drawing.ColorTranslator]::FromHtml('#D5A085')

function New-Pen([System.Drawing.Color]$color, [float]$width) {
    $pen = [System.Drawing.Pen]::new($color, $width)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Draw-RoundedRectangle($graphics, $pen, [System.Drawing.RectangleF]$rectangle, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 2 * $radius
    $path.AddArc($rectangle.Left, $rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($rectangle.Right - $diameter, $rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($rectangle.Right - $diameter, $rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rectangle.Left, $rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.DrawPath($pen, $path)
    $path.Dispose()
}

function Draw-Screens($graphics, $pen, [float]$scale) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddLine(22 * $scale, 14 * $scale, 22 * $scale, 13 * $scale)
    $path.AddArc(18 * $scale, 11 * $scale, 4 * $scale, 4 * $scale, 0, -90)
    $path.AddLine(20 * $scale, 11 * $scale, 5 * $scale, 11 * $scale)
    $path.AddArc(3 * $scale, 11 * $scale, 4 * $scale, 4 * $scale, 270, -90)
    $path.AddLine(3 * $scale, 13 * $scale, 3 * $scale, 23 * $scale)
    $path.AddArc(3 * $scale, 21 * $scale, 4 * $scale, 4 * $scale, 180, -90)
    $path.AddLine(5 * $scale, 25 * $scale, 18 * $scale, 25 * $scale)
    $graphics.DrawPath($pen, $path)
    $path.Dispose()
    $graphics.DrawLine($pen, 12.5 * $scale, 25 * $scale, 12.5 * $scale, 28 * $scale)
    $graphics.DrawLine($pen, 9 * $scale, 28 * $scale, 16 * $scale, 28 * $scale)
    Draw-RoundedRectangle $graphics $pen ([System.Drawing.RectangleF]::new(20.5 * $scale, 16 * $scale, 8.5 * $scale, 13 * $scale)) (1.5 * $scale)
}

function New-Frame([string]$state, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    if ($state -eq 'brand' -and $size -ge 64) {
        $scale = [float]($size / 256.0)
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(35 * $scale, 223 * $scale),
            [System.Drawing.PointF]::new(205 * $scale, 32 * $scale), $gold, $white)
        $blend = [System.Drawing.Drawing2D.ColorBlend]::new(3)
        $blend.Colors = [System.Drawing.Color[]]@($gold, [System.Drawing.ColorTranslator]::FromHtml('#E1CAA3'), $white)
        $blend.Positions = [float[]]@(0, 0.56, 1)
        $gradient.InterpolationColors = $blend
        $pen = [System.Drawing.Pen]::new($gradient, [float](9 * $scale))
        $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        Draw-RoundedRectangle $graphics $pen ([System.Drawing.RectangleF]::new(28 * $scale, 82 * $scale, 166 * $scale, 108 * $scale)) (12 * $scale)
        $graphics.DrawLine($pen, 111 * $scale, 190 * $scale, 111 * $scale, 209 * $scale)
        $graphics.DrawLine($pen, 83 * $scale, 209 * $scale, 139 * $scale, 209 * $scale)
        Draw-RoundedRectangle $graphics $pen ([System.Drawing.RectangleF]::new(164 * $scale, 118 * $scale, 64 * $scale, 110 * $scale)) (10 * $scale)
        $pen.Width = [float](8 * $scale)
        $graphics.DrawPolygon($pen, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(128 * $scale, 26 * $scale),
            [System.Drawing.PointF]::new(143 * $scale, 41 * $scale),
            [System.Drawing.PointF]::new(128 * $scale, 56 * $scale),
            [System.Drawing.PointF]::new(113 * $scale, 41 * $scale)))
        $pen.Dispose()
        $gradient.Dispose()
    }
    else {
        $scale = [float]($size / 32.0)
        $accent = switch ($state) { 'error' { $copper } 'paused' { $sage } 'unmatched' { $sage } default { $gold } }
        $outline = New-Pen $accent ([Math]::Max(1.1, 1.8 * $scale))
        Draw-Screens $graphics $outline $scale
        $outline.Dispose()
        $markColor = if ($state -eq 'generic') { $white } else { $accent }
        $pen = New-Pen $markColor ([Math]::Max(1.1, 1.8 * $scale))
        switch ($state) {
            'normal' {
                $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
                    [System.Drawing.PointF]::new(12 * $scale, 5 * $scale),
                    [System.Drawing.PointF]::new(15 * $scale, 8 * $scale),
                    [System.Drawing.PointF]::new(21 * $scale, 2 * $scale)))
            }
            { $_ -in @('generic', 'brand') } {
                $graphics.DrawPolygon($pen, [System.Drawing.PointF[]]@(
                    [System.Drawing.PointF]::new(16 * $scale, 1.5 * $scale),
                    [System.Drawing.PointF]::new(19.5 * $scale, 5 * $scale),
                    [System.Drawing.PointF]::new(16 * $scale, 8.5 * $scale),
                    [System.Drawing.PointF]::new(12.5 * $scale, 5 * $scale)))
            }
            'unmatched' { $graphics.DrawLine($pen, 12 * $scale, 5 * $scale, 20 * $scale, 5 * $scale) }
            'paused' {
                $graphics.DrawLine($pen, 13 * $scale, 2 * $scale, 13 * $scale, 8 * $scale)
                $graphics.DrawLine($pen, 19 * $scale, 2 * $scale, 19 * $scale, 8 * $scale)
            }
            'error' {
                $graphics.DrawLine($pen, 16 * $scale, 1.5 * $scale, 16 * $scale, 5 * $scale)
                $brush = [System.Drawing.SolidBrush]::new($accent)
                $dot = [Math]::Max(1, 1.8 * $scale)
                $graphics.FillEllipse($brush, 16 * $scale - $dot / 2, 8.5 * $scale - $dot / 2, $dot, $dot)
                $brush.Dispose()
            }
            'recognizing' {
                $brush = [System.Drawing.SolidBrush]::new($accent)
                $dot = [Math]::Max(1, 2 * $scale)
                foreach ($x in @(11, 16, 21)) {
                    $graphics.FillEllipse($brush, $x * $scale - $dot / 2, 5 * $scale - $dot / 2, $dot, $dot)
                }
                $brush.Dispose()
            }
        }
        $pen.Dispose()
    }
    $graphics.Dispose()
    return $bitmap
}

function Write-Ico([string]$path, [string]$state, [int[]]$sizes) {
    $frames = foreach ($size in $sizes) {
        $frame = New-Frame $state $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            ,$stream.ToArray()
        }
        finally { $frame.Dispose(); $stream.Dispose() }
    }
    $stream = [System.IO.File]::Create($path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        for ($i = 0; $i -lt $frames.Count; $i++) {
            $size = $sizes[$i]
            $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([Byte]0)
            $writer.Write([Byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frames[$i].Length)
            $writer.Write([UInt32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

function Write-TraySvg([string]$state) {
    $accent = switch ($state) { 'error' { '#D5A085' } 'paused' { '#A5B5A9' } 'unmatched' { '#A5B5A9' } default { '#D2B47A' } }
    $mark = switch ($state) {
        'normal' { '<path d="m12 5 3 3 6-6"/>' }
        'generic' { '<path d="m16 1.5 3.5 3.5-3.5 3.5-3.5-3.5Z" stroke="#F3EBDC"/>' }
        'unmatched' { '<path d="M12 5h8"/>' }
        'paused' { '<path d="M13 2v6M19 2v6"/>' }
        'error' { '<path d="M16 1.5V5"/><circle cx="16" cy="8.5" r=".9" fill="#D5A085" stroke="none"/>' }
        'recognizing' { '<g fill="#D2B47A" stroke="none"><circle cx="11" cy="5" r="1"/><circle cx="16" cy="5" r="1"/><circle cx="21" cy="5" r="1"/></g>' }
    }
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 32 32" role="img" aria-labelledby="title desc">
  <title id="title">SyncWallpaper $state tray icon</title>
  <desc id="desc">Hollow landscape and portrait displays with an independent $state state mark. Transparent background and screen interiors.</desc>
  <g fill="none" stroke="$accent" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
    <path d="M22 14v-1a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h13M12.5 25v3M9 28h7"/>
    <rect x="20.5" y="16" width="8.5" height="13" rx="1.5"/>
    $mark
  </g>
</svg>
"@
    [System.IO.File]::WriteAllText((Join-Path $trayRoot "tray_$state.svg"), $svg)
}

$sizes = @(16, 20, 24, 32, 48, 64)
foreach ($state in $States) {
    foreach ($size in $sizes) {
        $frame = New-Frame $state $size
        try { $frame.Save((Join-Path $trayRoot "tray_${state}_$size.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $frame.Dispose() }
    }
    Write-Ico (Join-Path $trayRoot "tray_$state.ico") $state $sizes
    Write-TraySvg $state
}

if (-not $SkipTaskbarAssets) {
    foreach ($size in @(16, 32, 64, 256)) {
        $frame = New-Frame 'brand' $size
        try { $frame.Save((Join-Path $assetRoot "AppIcon_$size.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $frame.Dispose() }
    }
    Write-Ico (Join-Path $assetRoot 'AppIcon.ico') 'brand' @(16, 20, 24, 32, 48, 64, 256)
}
Write-Output "Generated hollow antique-gold icons in $assetRoot."
