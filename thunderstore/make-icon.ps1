# Draws the mod's icon (Thunderstore needs a 256 x 256 PNG): gold VOID CREW lettering over a VR headset whose glowing
# visor reads VR, on a deep-space background with cyan energy glows, after the game's colours. Drawn at 1024 x 1024 and
# scaled down, so every edge is smooth.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File thunderstore\make-icon.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot "icon.png"),
    [int]$Size = 256
)

Add-Type -AssemblyName System.Drawing
$S = 1024
$bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

function Col([int]$a, [int]$r, [int]$gr, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $gr, $b) }

function Glow([float]$cx, [float]$cy, [float]$rx, [float]$ry, $center, $edge) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddEllipse($cx - $rx, $cy - $ry, 2 * $rx, 2 * $ry)
    $b = New-Object System.Drawing.Drawing2D.PathGradientBrush $p
    $b.CenterPoint = New-Object System.Drawing.PointF $cx, $cy
    $b.CenterColor = $center
    $b.SurroundColors = [System.Drawing.Color[]]@($edge)
    $g.FillPath($b, $p)
    $b.Dispose(); $p.Dispose()
}

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Soft glow around a shape: wide faint strokes first, then narrower brighter ones.
function GlowStroke($path, $color, [float[]]$widths, [int[]]$alphas) {
    for ($i = 0; $i -lt $widths.Count; $i++) {
        $pen = New-Object System.Drawing.Pen (Col $alphas[$i] $color.R $color.G $color.B), $widths[$i]
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }
}

# A smooth glow: many thin layers, each a little narrower, so the light fades out without visible steps.
function SoftGlow($path, $color, [float]$width, [int]$layers, [int]$alpha) {
    for ($i = 0; $i -lt $layers; $i++) {
        $pen = New-Object System.Drawing.Pen (Col $alpha $color.R $color.G $color.B), ($width * (1 - $i / $layers) + 2)
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }
}

# Text as a path, stretched sideways (the game's lettering is wide), fitted and centred in a box.
function TextPath([string]$text, [string]$family, [float]$stretch, [float]$x, [float]$y, [float]$w, [float]$h) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ff = New-Object System.Drawing.FontFamily $family
    $p.AddString($text, $ff, [int][System.Drawing.FontStyle]::Regular, 400, (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    # Bahnschrift's letters are drawn from overlapping outlines; winding fill keeps the overlaps solid.
    $p.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Scale($stretch, 1)
    $p.Transform($m)
    $b = $p.GetBounds()
    $k = [Math]::Min($w / $b.Width, $h / $b.Height)
    $fit = New-Object System.Drawing.Drawing2D.Matrix
    $fit.Translate($x + ($w - $b.Width * $k) / 2, $y + ($h - $b.Height * $k) / 2)
    $fit.Scale($k, $k)
    $fit.Translate(-$b.X, -$b.Y)
    $p.Transform($fit)
    return $p
}

function VerticalBrush($path, [System.Drawing.Color[]]$colors, [single[]]$positions) {
    $r = $path.GetBounds()
    $r.Inflate(2, 2)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, $colors[0], $colors[$colors.Count - 1], ([single]90)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend $colors.Count
    $blend.Colors = $colors
    $blend.Positions = $positions
    $brush.InterpolationColors = $blend
    return $brush
}

# Deep space.
$all = New-Object System.Drawing.Rectangle 0, 0, $S, $S
$space = New-Object System.Drawing.Drawing2D.LinearGradientBrush $all, (Col 255 3 6 18), (Col 255 9 18 46), ([single]90)
$g.FillRectangle($space, $all)
Glow 512 640 640 420 (Col 150 40 110 230) (Col 0 9 18 46)
Glow 180 250 360 260 (Col 70 120 50 190) (Col 0 9 18 46)
Glow 880 900 380 260 (Col 60 30 80 200) (Col 0 9 18 46)

$rnd = New-Object System.Random 11
for ($i = 0; $i -lt 320; $i++) {
    $x = $rnd.NextDouble() * $S
    $y = $rnd.NextDouble() * $S
    $r = 0.8 + [Math]::Pow($rnd.NextDouble(), 4) * 4.5
    $a = [int](50 + $rnd.NextDouble() * 205)
    $star = New-Object System.Drawing.SolidBrush (Col $a 225 238 255)
    $g.FillEllipse($star, [float]($x - $r), [float]($y - $r), [float](2 * $r), [float](2 * $r))
    $star.Dispose()
}

# Bright energy burst behind the headset, and a light streak through it.
Glow 512 630 500 270 (Col 215 120 210 255) (Col 0 30 90 220)
Glow 512 630 260 130 (Col 240 225 250 255) (Col 0 80 170 255)
$streak = New-Object System.Drawing.Drawing2D.GraphicsPath
$streak.AddEllipse(20, 619, 984, 22)
$streakBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush $streak
$streakBrush.CenterColor = Col 230 235 248 255
$streakBrush.SurroundColors = [System.Drawing.Color[]]@((Col 0 120 200 255))
$g.FillPath($streakBrush, $streak)

# Orbit rings.
foreach ($ring in @(@(530, 310, 90), @(440, 255, 60))) {
    $pen = New-Object System.Drawing.Pen (Col $ring[2] 110 200 255), 5
    $g.DrawEllipse($pen, [float](512 - $ring[0]), [float](632 - $ring[1]), [float](2 * $ring[0]), [float](2 * $ring[1]))
    $pen.Dispose()
}

# The headset: straps, body, visor.
$cyan = Col 255 56 214 255
$strapL = RoundRect 56 582 220 100 42
$strapR = RoundRect 748 582 220 100 42
foreach ($strap in @($strapL, $strapR)) {
    $fill = VerticalBrush $strap @((Col 255 52 64 84), (Col 255 22 28 40), (Col 255 10 13 20)) @([single]0, [single]0.5, [single]1)
    $g.FillPath($fill, $strap)
    $rim = New-Object System.Drawing.Pen (Col 230 120 140 170), 5
    $g.DrawPath($rim, $strap)
    $rim.Dispose(); $fill.Dispose()
}

$body = RoundRect 180 445 664 372 128
SoftGlow $body $cyan 90 12 9
GlowStroke $body (Col 255 0 0 0) @(26, 16) @(80, 120)
$bodyFill = VerticalBrush $body @((Col 255 70 84 108), (Col 255 34 42 58), (Col 255 14 18 28), (Col 255 8 10 16)) @([single]0, [single]0.35, [single]0.75, [single]1)
$g.FillPath($bodyFill, $body)
$bodyRim = New-Object System.Drawing.Pen (Col 255 168 190 220), 9
$bodyRim.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPath($bodyRim, $body)
# A soft highlight along the top of the shell.
$g.SetClip((New-Object System.Drawing.RectangleF 180, 445, 664, 120))
$topLight = RoundRect 198 462 628 336 112
$topPen = New-Object System.Drawing.Pen (Col 70 220 235 255), 6
$g.DrawPath($topPen, $topLight)
$g.ResetClip()

$visor = RoundRect 232 498 560 264 96
SoftGlow $visor $cyan 60 14 16
$visorFill = VerticalBrush $visor @((Col 255 14 58 96), (Col 255 6 30 56), (Col 255 3 14 30)) @([single]0, [single]0.55, [single]1)
$g.FillPath($visorFill, $visor)
$visorRim = New-Object System.Drawing.Pen $cyan, 7
$g.DrawPath($visorRim, $visor)

# A reflection across the glass.
$g.SetClip($visor)
$shine = New-Object System.Drawing.SolidBrush (Col 34 200 240 255)
$g.FillPolygon($shine, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 232, 580), (New-Object System.Drawing.PointF 410, 490),
    (New-Object System.Drawing.PointF 515, 490), (New-Object System.Drawing.PointF 232, 672)))
$g.ResetClip()

# VR on the visor, glowing.
$vr = TextPath "VR" "Bahnschrift SemiBold" 1.18 322 548 380 166
SoftGlow $vr $cyan 50 14 18
$vrFill = VerticalBrush $vr @((Col 255 255 255 255), (Col 255 200 248 255), (Col 255 120 228 255)) @([single]0, [single]0.5, [single]1)
$g.FillPath($vrFill, $vr)

# VOID CREW in glowing gold.
$title = TextPath "VOID CREW" "Bahnschrift SemiBold" 1.28 70 110 884 190
SoftGlow $title (Col 255 255 140 20) 64 16 8
$outline = New-Object System.Drawing.Pen (Col 255 46 20 0), 16
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPath($outline, $title)
$gold = VerticalBrush $title @((Col 255 255 248 205), (Col 255 255 214 96), (Col 255 246 160 34), (Col 255 196 96 12)) @([single]0, [single]0.42, [single]0.72, [single]1)
$g.FillPath($gold, $title)
$g.SetClip($title)
$tb = $title.GetBounds()
$glossRect = New-Object System.Drawing.RectangleF $tb.X, ($tb.Y - 2), $tb.Width, ($tb.Height * 0.5 + 2)
$gloss = New-Object System.Drawing.Drawing2D.LinearGradientBrush $glossRect, (Col 110 255 255 255), (Col 0 255 255 255), ([single]90)
$g.FillRectangle($gloss, $glossRect)
$g.ResetClip()

# Four-pointed sparkles, like the stars in the game's art.
function Sparkle([float]$x, [float]$y, [float]$r) {
    Glow $x $y ($r * 1.6) ($r * 1.6) (Col 150 190 230 255) (Col 0 120 200 255)
    $t = $r * 0.16
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $x, ($y - $r)), (New-Object System.Drawing.PointF ($x + $t), ($y - $t)),
        (New-Object System.Drawing.PointF ($x + $r), $y), (New-Object System.Drawing.PointF ($x + $t), ($y + $t)),
        (New-Object System.Drawing.PointF $x, ($y + $r)), (New-Object System.Drawing.PointF ($x - $t), ($y + $t)),
        (New-Object System.Drawing.PointF ($x - $r), $y), (New-Object System.Drawing.PointF ($x - $t), ($y - $t)))
    $white = New-Object System.Drawing.SolidBrush (Col 255 250 253 255)
    $g.FillPolygon($white, $pts)
    $white.Dispose()
}
Sparkle 900 72 30
Sparkle 118 905 26
Sparkle 930 880 18
Sparkle 74 338 14
$g.Dispose()

# Scale down to the icon size.
$small = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gs = [System.Drawing.Graphics]::FromImage($small)
$gs.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gs.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$gs.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$attributes = New-Object System.Drawing.Imaging.ImageAttributes
$attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
$gs.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, $Size, $Size), 0, 0, $S, $S, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
$gs.Dispose()
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$small.Dispose(); $bmp.Dispose()
Write-Output ("Saved " + $Out + " (" + $Size + " x " + $Size + ")")
