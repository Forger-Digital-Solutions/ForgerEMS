param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\ForgerEMS.Wpf\Assets\ForgerEMS_CommandCenterBackground.png")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$width = 1536
$height = 1024
$bmp = [System.Drawing.Bitmap]::new($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

function Color-Hex([string]$hex, [int]$alpha = 255) {
    $value = $hex.TrimStart("#")
    return [System.Drawing.Color]::FromArgb(
        $alpha,
        [Convert]::ToInt32($value.Substring(0, 2), 16),
        [Convert]::ToInt32($value.Substring(2, 2), 16),
        [Convert]::ToInt32($value.Substring(4, 2), 16))
}

function Brush-Hex([string]$hex, [int]$alpha = 255) {
    return [System.Drawing.SolidBrush]::new((Color-Hex $hex $alpha))
}

function Pen-Hex([string]$hex, [float]$size = 1, [int]$alpha = 255) {
    return [System.Drawing.Pen]::new((Color-Hex $hex $alpha), $size)
}

function Font-New([float]$size, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    return [System.Drawing.Font]::new("Segoe UI", $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
}

function Rounded-Path([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r, [string]$stroke, [string]$fill, [int]$fillAlpha = 80, [float]$strokeWidth = 1.5) {
    $path = Rounded-Path $x $y $w $h $r
    $g.FillPath((Brush-Hex $fill $fillAlpha), $path)
    $g.DrawPath((Pen-Hex $stroke $strokeWidth 220), $path)
    $path.Dispose()
}

function Draw-Octagon([float]$cx, [float]$cy, [float]$r, [string]$stroke, [string]$fill, [int]$fillAlpha = 55) {
    $pts = [System.Collections.Generic.List[System.Drawing.PointF]]::new()
    for ($i = 0; $i -lt 8; $i++) {
        $angle = ((22.5 + ($i * 45)) * [Math]::PI) / 180
        $pts.Add([System.Drawing.PointF]::new($cx + ([Math]::Cos($angle) * $r), $cy + ([Math]::Sin($angle) * $r)))
    }
    $outer = [System.Drawing.PointF[]]$pts.ToArray()
    $inner = [System.Drawing.PointF[]]($pts | ForEach-Object { [System.Drawing.PointF]::new($cx + (($_.X - $cx) * 0.82), $cy + (($_.Y - $cy) * 0.82)) })
    $g.FillPolygon((Brush-Hex $fill $fillAlpha), $outer)
    $g.DrawPolygon((Pen-Hex $stroke 2.5 230), $outer)
    $g.DrawPolygon((Pen-Hex $stroke 0.9 120), $inner)
}

function Draw-CenteredText([string]$text, [float]$x, [float]$y, [float]$w, [float]$h, [float]$size, [string]$color, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    $sf = [System.Drawing.StringFormat]::new()
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($text, (Font-New $size $style), (Brush-Hex $color), [System.Drawing.RectangleF]::new($x, $y, $w, $h), $sf)
    $sf.Dispose()
}

function Draw-LeftText([string]$text, [float]$x, [float]$y, [float]$size, [string]$color, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    $g.DrawString($text, (Font-New $size $style), (Brush-Hex $color), [System.Drawing.PointF]::new($x, $y))
}

function Draw-GlowEllipse([float]$x, [float]$y, [float]$w, [float]$h, [string]$color, [int]$alpha = 120) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddEllipse($x, $y, $w, $h)
    $brush = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
    $brush.CenterColor = Color-Hex $color $alpha
    $brush.SurroundColors = [System.Drawing.Color[]]@(Color-Hex "02050A" 0)
    $g.FillEllipse($brush, $x, $y, $w, $h)
    $brush.Dispose()
    $path.Dispose()
}

function Draw-GlowLine([float]$x1, [float]$y1, [float]$x2, [float]$y2, [string]$color, [float]$width = 2) {
    $g.DrawLine((Pen-Hex $color ($width + 6) 28), $x1, $y1, $x2, $y2)
    $g.DrawLine((Pen-Hex $color ($width + 2) 70), $x1, $y1, $x2, $y2)
    $g.DrawLine((Pen-Hex $color $width 220), $x1, $y1, $x2, $y2)
}

function Draw-Trace([float]$x1, [float]$y1, [float]$x2, [float]$y2, [string]$color) {
    $mid = ($x1 + $x2) / 2
    Draw-GlowLine $x1 $y1 $mid $y1 $color 1.8
    Draw-GlowLine $mid $y1 $mid $y2 $color 1.8
    Draw-GlowLine $mid $y2 $x2 $y2 $color 1.8
    $g.FillEllipse((Brush-Hex $color 220), $x2 - 3, $y2 - 3, 6, 6)
    $g.FillEllipse((Brush-Hex "E0F2FE" 160), $x1 - 2, $y1 - 2, 4, 4)
}

function Draw-LabelPill([string]$text, [float]$cx, [float]$cy, [float]$w, [float]$h, [float]$size, [string]$accent) {
    $x = $cx - ($w / 2)
    $y = $cy - ($h / 2)
    Draw-RoundedRect $x $y $w $h 8 $accent "020814" 190 1.1
    $sf = [System.Drawing.StringFormat]::new()
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap
    $lines = $text -split "`n"
    $lineSize = if ($lines.Length -gt 1) { [Math]::Min($size, 14) } else { $size }
    $lineHeight = $lineSize + 3
    $totalHeight = $lineHeight * $lines.Length
    $startY = $cy - ($totalHeight / 2)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $lineRect = [System.Drawing.RectangleF]::new($x + 7, $startY + ($i * $lineHeight), $w - 14, $lineHeight + 2)
        $g.DrawString($lines[$i], (Font-New $lineSize ([System.Drawing.FontStyle]::Bold)), (Brush-Hex "F8FAFC"), $lineRect, $sf)
    }
    $sf.Dispose()
}

function Draw-FeShield([float]$cx, [float]$cy, [float]$s, [string]$accent = "FDE68A") {
    [System.Drawing.PointF[]]$outer = @(
        [System.Drawing.PointF]::new($cx, $cy - ($s * 0.52)),
        [System.Drawing.PointF]::new($cx + ($s * 0.44), $cy - ($s * 0.34)),
        [System.Drawing.PointF]::new($cx + ($s * 0.36), $cy + ($s * 0.24)),
        [System.Drawing.PointF]::new($cx, $cy + ($s * 0.52)),
        [System.Drawing.PointF]::new($cx - ($s * 0.36), $cy + ($s * 0.24)),
        [System.Drawing.PointF]::new($cx - ($s * 0.44), $cy - ($s * 0.34)))
    [System.Drawing.PointF[]]$inner = @(
        [System.Drawing.PointF]::new($cx, $cy - ($s * 0.40)),
        [System.Drawing.PointF]::new($cx + ($s * 0.31), $cy - ($s * 0.25)),
        [System.Drawing.PointF]::new($cx + ($s * 0.25), $cy + ($s * 0.17)),
        [System.Drawing.PointF]::new($cx, $cy + ($s * 0.38)),
        [System.Drawing.PointF]::new($cx - ($s * 0.25), $cy + ($s * 0.17)),
        [System.Drawing.PointF]::new($cx - ($s * 0.31), $cy - ($s * 0.25)))
    $g.FillPolygon((Brush-Hex "060B12" 232), $outer)
    $g.DrawPolygon((Pen-Hex "CBD5E1" 2.2 225), $outer)
    $g.DrawPolygon((Pen-Hex $accent 1.1 180), $inner)
    Draw-CenteredText "FE" ($cx - ($s * 0.28)) ($cy - ($s * 0.16)) ($s * 0.56) ($s * 0.32) ($s * 0.24) $accent ([System.Drawing.FontStyle]::Bold)
}

function Draw-Glyph([string]$kind, [float]$x, [float]$y, [float]$s, [string]$color) {
    $pen = Pen-Hex $color 2.5 235
    $brush = Brush-Hex $color 190
    switch ($kind) {
        "desktopImage" {
            $g.DrawRectangle($pen, $x, $y, $s, $s * 0.75)
            $g.DrawRectangle((Pen-Hex $color 1.3 170), $x + 7, $y + 8, $s - 14, ($s * 0.75) - 16)
            $g.DrawLine($pen, $x + ($s * 0.34), $y + ($s * 0.75), $x + ($s * 0.66), $y + ($s * 0.75))
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.75), $x + ($s * 0.5), $y + ($s * 0.92))
        }
        "modernImage" {
            $g.DrawRectangle($pen, $x + 4, $y + 4, $s * 0.68, $s * 0.56)
            $g.DrawRectangle((Pen-Hex $color 1.7 190), $x + 15, $y + 16, $s * 0.68, $s * 0.56)
            $g.DrawLine($pen, $x + 12, $y + 35, $x + 44, $y + 35)
            $g.DrawLine($pen, $x + 28, $y + 20, $x + 28, $y + 54)
        }
        "serverImage" {
            for ($i = 0; $i -lt 3; $i++) {
                Draw-RoundedRect ($x + 2) ($y + 4 + ($i * 16)) ($s - 4) 12 3 $color "07101D" 120 1.7
                $g.FillEllipse($brush, $x + 9, $y + 8 + ($i * 16), 4, 4)
                $g.DrawLine((Pen-Hex $color 1.2 160), $x + 22, $y + 10 + ($i * 16), $x + $s - 9, $y + 10 + ($i * 16))
            }
        }
        "terminal" {
            Draw-RoundedRect $x ($y + 4) $s ($s * 0.66) 4 $color "06111D" 130 2
            $g.DrawLine($pen, $x + 8, $y + 16, $x + 18, $y + 25)
            $g.DrawLine($pen, $x + 18, $y + 25, $x + 8, $y + 34)
            $g.DrawLine($pen, $x + 28, $y + 36, $x + 44, $y + 36)
        }
        "securityLive" {
            [System.Drawing.PointF[]]$pts = @(
                [System.Drawing.PointF]::new($x + ($s * 0.5), $y),
                [System.Drawing.PointF]::new($x + $s, $y + ($s * 0.18)),
                [System.Drawing.PointF]::new($x + ($s * 0.84), $y + ($s * 0.74)),
                [System.Drawing.PointF]::new($x + ($s * 0.5), $y + $s),
                [System.Drawing.PointF]::new($x + ($s * 0.16), $y + ($s * 0.74)),
                [System.Drawing.PointF]::new($x, $y + ($s * 0.18)))
            $g.DrawPolygon($pen, $pts)
            Draw-RoundedRect ($x + ($s * 0.34)) ($y + ($s * 0.38)) ($s * 0.32) ($s * 0.24) 3 $color "06111D" 120 1.8
            $g.DrawArc($pen, $x + ($s * 0.36), $y + ($s * 0.24), $s * 0.28, $s * 0.28, 200, 140)
        }
        "desktopLive" {
            $g.DrawRectangle($pen, $x + 2, $y + 7, $s - 4, $s * 0.58)
            $g.DrawLine($pen, $x + ($s * 0.35), $y + ($s * 0.78), $x + ($s * 0.65), $y + ($s * 0.78))
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.65), $x + ($s * 0.5), $y + ($s * 0.86))
            $g.DrawEllipse((Pen-Hex $color 1.5 160), $x + 12, $y + 15, $s - 24, $s - 28)
        }
        "multibootUsb" {
            Draw-RoundedRect ($x + 11) ($y + 10) ($s * 0.42) ($s * 0.68) 6 $color "07101D" 125 2
            $g.DrawRectangle($pen, $x + 18, $y + 1, 16, 12)
            $g.DrawLine($pen, $x + 28, $y + 28, $x + 28, $y + 52)
            $g.DrawLine($pen, $x + 28, $y + 38, $x + 12, $y + 38)
            $g.DrawLine($pen, $x + 28, $y + 38, $x + 44, $y + 38)
            $g.FillEllipse($brush, $x + 8, $y + 34, 7, 7)
            $g.FillEllipse($brush, $x + 41, $y + 34, 7, 7)
        }
        "bootWriter" {
            Draw-RoundedRect ($x + 3) ($y + 26) ($s * 0.64) ($s * 0.24) 4 $color "07101D" 125 2
            $g.DrawLine($pen, $x + ($s * 0.18), $y + ($s * 0.88), $x + ($s * 0.82), $y + ($s * 0.24))
            $g.DrawLine($pen, $x + ($s * 0.72), $y + ($s * 0.12), $x + ($s * 0.92), $y + ($s * 0.32))
            $g.DrawLine($pen, $x + ($s * 0.82), $y + ($s * 0.24), $x + ($s * 0.92), $y + ($s * 0.32))
            $g.FillEllipse($brush, $x + ($s * 0.12), $y + ($s * 0.86), 5, 5)
        }
        "diskClone" {
            $g.DrawEllipse($pen, $x, $y, $s, $s)
            $g.DrawEllipse($pen, $x + ($s * 0.34), $y + ($s * 0.34), $s * 0.32, $s * 0.32)
            $g.DrawEllipse((Pen-Hex $color 1.7 180), $x + 15, $y + 6, $s, $s)
            $g.DrawLine((Pen-Hex $color 1.6 190), $x + ($s * 0.72), $y + ($s * 0.5), $x + ($s * 1.08), $y + ($s * 0.5))
        }
        "imageRestore" {
            $g.DrawArc($pen, $x + 2, $y + 3, $s - 6, $s - 6, 35, 285)
            $g.DrawLine($pen, $x + 12, $y + 13, $x + 7, $y + 31)
            $g.DrawLine($pen, $x + 12, $y + 13, $x + 28, $y + 18)
            $g.DrawRectangle((Pen-Hex $color 1.7 180), $x + 17, $y + 18, 28, 21)
        }
        "partition" {
            for ($ix = 0; $ix -lt 3; $ix++) {
                for ($iy = 0; $iy -lt 3; $iy++) {
                    $g.DrawRectangle($pen, $x + ($ix * $s / 3), $y + ($iy * $s / 3), ($s / 3) - 2, ($s / 3) - 2)
                }
            }
        }
        "recoveryKit" {
            Draw-RoundedRect ($x + 4) ($y + 10) ($s - 8) ($s - 16) 7 $color "07101D" 125 2
            $g.DrawLine($pen, $x + ($s * 0.5), $y + 18, $x + ($s * 0.5), $y + $s - 16)
            $g.DrawLine($pen, $x + 18, $y + ($s * 0.5), $x + $s - 18, $y + ($s * 0.5))
            $g.DrawArc((Pen-Hex $color 1.5 180), $x + 15, $y + 2, $s - 30, 18, 180, 180)
        }
        "memoryCheck" {
            Draw-RoundedRect ($x + 1) ($y + 16) ($s - 2) 24 4 $color "07101D" 130 2
            for ($i = 0; $i -lt 4; $i++) { $g.DrawLine($pen, $x + 9 + ($i * 10), $y + 41, $x + 9 + ($i * 10), $y + 48) }
            $g.DrawLine((Pen-Hex $color 1.6 220), $x + 8, $y + 28, $x + 18, $y + 28)
            $g.DrawLine((Pen-Hex $color 1.6 220), $x + 18, $y + 28, $x + 24, $y + 34)
            $g.DrawLine((Pen-Hex $color 1.6 220), $x + 24, $y + 34, $x + 34, $y + 22)
            $g.DrawLine((Pen-Hex $color 1.6 220), $x + 34, $y + 22, $x + 46, $y + 22)
        }
        "hardwareInfo" {
            $g.DrawRectangle($pen, $x + 8, $y + 8, $s - 16, $s - 16)
            for ($i = 0; $i -lt 4; $i++) {
                $p = $y + 14 + ($i * 10)
                $g.DrawLine($pen, $x, $p, $x + 8, $p)
                $g.DrawLine($pen, $x + $s - 8, $p, $x + $s, $p)
            }
            Draw-CenteredText "i" ($x + 18) ($y + 17) 20 24 22 $color ([System.Drawing.FontStyle]::Bold)
        }
        "diskHealth" {
            $g.DrawRectangle($pen, $x, $y + 8, $s, $s * 0.62)
            [System.Drawing.PointF[]]$points = @(
                [System.Drawing.PointF]::new($x + 7, $y + 36),
                [System.Drawing.PointF]::new($x + 20, $y + 36),
                [System.Drawing.PointF]::new($x + 28, $y + 20),
                [System.Drawing.PointF]::new($x + 40, $y + 50),
                [System.Drawing.PointF]::new($x + 50, $y + 36),
                [System.Drawing.PointF]::new($x + $s - 7, $y + 36))
            $g.DrawLines($pen, $points)
        }
        "driverStore" {
            [System.Drawing.PointF[]]$pts = @(
                [System.Drawing.PointF]::new($x, $y + 18),
                [System.Drawing.PointF]::new($x + 18, $y + 18),
                [System.Drawing.PointF]::new($x + 26, $y + 10),
                [System.Drawing.PointF]::new($x + $s, $y + 10),
                [System.Drawing.PointF]::new($x + $s, $y + $s - 8),
                [System.Drawing.PointF]::new($x, $y + $s - 8))
            $g.DrawPolygon($pen, $pts)
            $g.DrawRectangle((Pen-Hex $color 1.5 170), $x + 21, $y + 27, 18, 15)
            $g.FillRectangle((Brush-Hex $color 155), $x + 25, $y + 31, 10, 7)
        }
        "remoteScreen" {
            $g.DrawRectangle($pen, $x, $y + 6, $s, $s * 0.56)
            $g.DrawLine($pen, $x + ($s * 0.42), $y + ($s * 0.7), $x + ($s * 0.58), $y + ($s * 0.7))
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.62), $x + ($s * 0.5), $y + ($s * 0.82))
            $g.DrawLine((Pen-Hex $color 1.7 200), $x + 16, $y + 28, $x + 28, $y + 18)
            $g.DrawLine((Pen-Hex $color 1.7 200), $x + 28, $y + 18, $x + 40, $y + 28)
        }
        "networkRadar" {
            $g.DrawEllipse($pen, $x + 4, $y + 4, $s - 8, $s - 8)
            $g.DrawEllipse($pen, $x + 18, $y + 18, $s - 36, $s - 36)
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.5), $x + ($s * 0.86), $y + ($s * 0.22))
            $g.FillEllipse($brush, $x + ($s * 0.72), $y + ($s * 0.28), 7, 7)
        }
        default {
            $g.DrawEllipse($pen, $x, $y, $s, $s)
            $g.FillEllipse($brush, $x + ($s * 0.36), $y + ($s * 0.36), $s * 0.28, $s * 0.28)
        }
    }
}

function Draw-Item([float]$x, [float]$y, [float]$w, [string]$label, [string]$kind, [string]$color) {
    Draw-GlowEllipse ($x - 10) ($y - 8) ($w + 20) 66 $color 24
    Draw-RoundedRect $x $y $w 48 8 $color "07101D" 112 1.55
    Draw-RoundedRect ($x + 9) ($y + 7) 39 34 6 $color "020814" 135 1.1
    Draw-Glyph $kind ($x + 13) ($y + 7) 34 $color
    Draw-LeftText $label ($x + 61) ($y + 13) 18 "F8FAFC" ([System.Drawing.FontStyle]::Regular)
    $g.DrawLine((Pen-Hex $color 1.5 170), $x + $w - 32, $y + 17, $x + $w - 22, $y + 24)
    $g.DrawLine((Pen-Hex $color 1.5 170), $x + $w - 22, $y + 24, $x + $w - 32, $y + 31)
}

function Draw-Panel([float]$cx, [float]$cy, [string]$title, [string]$kind, [string]$color, [float]$r = 76) {
    $script:ModuleLabelSafeHeight = 42
    $script:ModuleLabelSafeWidth = $r * 1.72
    Draw-GlowEllipse ($cx - ($r * 1.25)) ($cy - ($r * 1.25)) ($r * 2.5) ($r * 2.5) $color 42
    Draw-Octagon $cx $cy $r $color "06111D" 130
    Draw-Glyph $kind ($cx - 28) ($cy - 44) 56 $color
    Draw-LabelPill $title $cx ($cy + 39) $script:ModuleLabelSafeWidth $script:ModuleLabelSafeHeight 16 $color
}

$bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Rectangle]::new(0, 0, $width, $height),
    (Color-Hex "02050A"),
    (Color-Hex "061525"),
    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
$g.FillRectangle($bg, 0, 0, $width, $height)
$bg.Dispose()

$rand = [Random]::new(7321)
for ($gx = 40; $gx -lt $width; $gx += 48) {
    $g.DrawLine((Pen-Hex "0EA5E9" 0.55 22), $gx, 64, $gx, $height - 70)
}
for ($gy = 72; $gy -lt $height; $gy += 44) {
    $g.DrawLine((Pen-Hex "0EA5E9" 0.55 18), 38, $gy, $width - 38, $gy)
}

Draw-GlowEllipse 462 350 612 430 "06B6D4" 28
Draw-GlowEllipse 550 410 432 350 "F59E0B" 36
Draw-GlowEllipse 332 594 328 292 "F97316" 26
Draw-GlowEllipse 922 164 338 274 "22C55E" 24

for ($i = 0; $i -lt 260; $i++) {
    $x = $rand.Next(30, $width - 30)
    $y = $rand.Next(24, $height - 24)
    $len = $rand.Next(18, 110)
    $color = if ($i % 5 -eq 0) { "22C55E" } elseif ($i % 7 -eq 0) { "F59E0B" } elseif ($i % 11 -eq 0) { "EC4899" } else { "06B6D4" }
    $pen = Pen-Hex $color 1 70
    if ($i % 2 -eq 0) {
        $g.DrawLine($pen, $x, $y, [Math]::Min($width - 30, $x + $len), $y)
    } else {
        $g.DrawLine($pen, $x, $y, $x, [Math]::Min($height - 30, $y + $len))
    }
    $g.FillEllipse((Brush-Hex $color 95), $x - 2, $y - 2, 4, 4)
    if ($i % 9 -eq 0) {
        $g.DrawRectangle((Pen-Hex $color 0.8 55), $x - 4, $y - 4, 8, 8)
    }
}

$borderPen = Pen-Hex "0284C7" 2 180
$g.DrawRectangle($borderPen, 15, 22, $width - 30, $height - 44)
$g.DrawRectangle((Pen-Hex "0EA5E9" 1 80), 38, 50, $width - 76, $height - 100)
Draw-CenteredText "FORGER DIGITAL SOLUTIONS" 0 18 $width 30 20 "7DD3FC" ([System.Drawing.FontStyle]::Regular)

Draw-Panel 454 224 "OS IMAGES" "modernImage" "38BDF8"
Draw-Panel 432 430 "LINUX MEDIA" "terminal" "84CC16"
Draw-Panel 438 670 "IMAGING" "imageRestore" "F97316"
Draw-Panel 448 840 "USB TOOLS" "multibootUsb" "D946EF"
Draw-Panel 1090 224 "DIAGNOSTICS" "diskHealth" "22C55E"
Draw-Panel 1088 434 "RECOVERY" "recoveryKit" "FACC15"
Draw-Panel 1090 655 "WIN TOOLS" "driverStore" "06B6D4"
Draw-Panel 768 175 "MULTIBOOT`nCORE" "multibootUsb" "06B6D4"
Draw-Panel 768 844 "MEDIC USB" "recoveryKit" "EC4899"

$leftX = 78
Draw-LeftText "OS IMAGES" 104 78 20 "67E8F9" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 118 214 "Desktop Image" "desktopImage" "38BDF8"
Draw-Item $leftX 174 214 "Modern Image" "modernImage" "38BDF8"
Draw-Item $leftX 230 214 "Server Image" "serverImage" "38BDF8"
Draw-LeftText "LINUX MEDIA" 104 337 20 "A3E635" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 372 214 "Live Terminal" "terminal" "84CC16"
Draw-Item $leftX 428 214 "Security Live" "securityLive" "84CC16"
Draw-Item $leftX 484 214 "Desktop Live" "desktopLive" "84CC16"
Draw-LeftText "RESCUE & IMAGING" 132 548 20 "FB923C" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 584 214 "Image Restore" "imageRestore" "F97316"
Draw-Item $leftX 640 214 "Disk Clone" "diskClone" "F97316"
Draw-Item $leftX 696 214 "Recovery Kit" "recoveryKit" "F97316"
Draw-LeftText "USB BUILDERS" 140 766 20 "E879F9" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 802 214 "Multiboot USB" "multibootUsb" "D946EF"
Draw-Item $leftX 858 214 "Boot Writer" "bootWriter" "D946EF"
Draw-Item $leftX 914 214 "Image Flasher" "partition" "D946EF"

$rightX = 1246
Draw-LeftText "DIAGNOSTICS" 1260 78 20 "86EFAC" ([System.Drawing.FontStyle]::Bold)
Draw-Item $rightX 118 214 "Memory Check" "memoryCheck" "22C55E"
Draw-Item $rightX 174 214 "Hardware Info" "hardwareInfo" "22C55E"
Draw-Item $rightX 230 214 "Disk Health" "diskHealth" "22C55E"
Draw-LeftText "RECOVERY TOOLS" 1256 338 20 "FACC15" ([System.Drawing.FontStyle]::Bold)
Draw-Item $rightX 374 214 "Disk Clone" "diskClone" "F59E0B"
Draw-Item $rightX 430 214 "Partition Grid" "partition" "F59E0B"
Draw-Item $rightX 486 214 "System Rescue" "recoveryKit" "F59E0B"
Draw-LeftText "WINDOWS TOOLS" 1258 590 20 "22D3EE" ([System.Drawing.FontStyle]::Bold)
Draw-Item $rightX 626 214 "Driver Store" "driverStore" "06B6D4"
Draw-Item $rightX 682 214 "Remote Screen" "remoteScreen" "06B6D4"
Draw-Item $rightX 738 214 "Network Radar" "networkRadar" "06B6D4"

$nodeLines = @(
    @(292,142,380,224,"38BDF8"), @(292,198,380,224,"38BDF8"), @(292,254,380,224,"38BDF8"),
    @(292,396,370,430,"84CC16"), @(292,452,370,430,"84CC16"), @(292,508,370,430,"84CC16"),
    @(292,608,370,670,"F97316"), @(292,664,370,670,"F97316"), @(292,720,370,670,"F97316"),
    @(292,826,374,840,"D946EF"), @(292,882,374,840,"D946EF"), @(292,938,374,840,"D946EF"),
    @(1016,224,842,175,"06B6D4"), @(516,224,694,175,"06B6D4"), @(516,430,700,520,"84CC16"),
    @(516,670,706,550,"F97316"), @(526,840,710,760,"D946EF"), @(1016,434,836,550,"FACC15"),
    @(1016,655,840,560,"06B6D4"), @(1246,142,1160,224,"22C55E"), @(1246,198,1160,224,"22C55E"),
    @(1246,254,1160,224,"22C55E"), @(1246,398,1160,434,"F59E0B"), @(1246,454,1160,434,"F59E0B"),
    @(1246,510,1160,434,"F59E0B"), @(1246,650,1160,655,"06B6D4"), @(1246,706,1160,655,"06B6D4"),
    @(1246,762,1160,655,"06B6D4"), @(768,249,768,430,"06B6D4"), @(768,770,768,620,"EC4899")
)
foreach ($line in $nodeLines) {
    Draw-Trace ([float]$line[0]) ([float]$line[1]) ([float]$line[2]) ([float]$line[3]) ([string]$line[4])
}

$ringBrush = [System.Drawing.Drawing2D.PathGradientBrush]::new([System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(604, 514),
    [System.Drawing.PointF]::new(932, 514),
    [System.Drawing.PointF]::new(932, 706),
    [System.Drawing.PointF]::new(604, 706)))
$ringBrush.CenterColor = Color-Hex "F59E0B" 150
$ringBrush.SurroundColors = [System.Drawing.Color[]]@(Color-Hex "02050A" 0)
$g.FillEllipse($ringBrush, 600, 438, 336, 336)
$ringBrush.Dispose()
Draw-GlowEllipse 610 452 316 316 "F59E0B" 92
$g.DrawEllipse((Pen-Hex "F59E0B" 5.5 235), 646, 486, 244, 244)
$g.DrawEllipse((Pen-Hex "FDE68A" 1.8 190), 630, 470, 276, 276)
$g.DrawEllipse((Pen-Hex "06B6D4" 1.4 130), 670, 510, 196, 196)
for ($i = 0; $i -lt 36; $i++) {
    $angle = ($i * 10) * [Math]::PI / 180
    $x1 = 768 + ([Math]::Cos($angle) * 132)
    $y1 = 608 + ([Math]::Sin($angle) * 132)
    $x2 = 768 + ([Math]::Cos($angle) * 146)
    $y2 = 608 + ([Math]::Sin($angle) * 146)
    $g.DrawLine((Pen-Hex "FDE68A" 1 110), $x1, $y1, $x2, $y2)
}

Draw-RoundedRect 716 424 104 226 30 "E5E7EB" "0A101A" 235 2.4
Draw-RoundedRect 730 494 76 142 22 "38BDF8" "111827" 135 1.3
$g.DrawRectangle((Pen-Hex "CBD5E1" 2.1 235), 742, 414, 52, 44)
$g.FillRectangle((Brush-Hex "0A101A" 225), 754, 433, 12, 12)
$g.FillRectangle((Brush-Hex "0A101A" 225), 778, 433, 12, 12)
$g.DrawLine((Pen-Hex "94A3B8" 1.2 150), 742, 458, 794, 458)
Draw-FeShield 768 566 94 "FDE68A"
$g.FillEllipse((Brush-Hex "22D3EE" 245), 764, 625, 9, 9)
$g.DrawEllipse((Pen-Hex "22D3EE" 1.5 160), 758, 619, 21, 21)

Draw-CenteredText "FORGEREMS" 1348 920 96 24 14 "94A3B8" ([System.Drawing.FontStyle]::Bold)
Draw-CenteredText "FE" 1360 943 72 42 27 "CBD5E1" ([System.Drawing.FontStyle]::Bold)

$outFull = [System.IO.Path]::GetFullPath($OutputPath)
$outDir = [System.IO.Path]::GetDirectoryName($outFull)
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    [System.IO.Directory]::CreateDirectory($outDir) | Out-Null
}

$bmp.Save($outFull, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Host "Generated safe ForgerEMS background: $outFull"
