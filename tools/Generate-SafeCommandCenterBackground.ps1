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

function Draw-Trace([float]$x1, [float]$y1, [float]$x2, [float]$y2, [string]$color) {
    $pen = Pen-Hex $color 2.1 210
    $mid = ($x1 + $x2) / 2
    $g.DrawLine($pen, $x1, $y1, $mid, $y1)
    $g.DrawLine($pen, $mid, $y1, $mid, $y2)
    $g.DrawLine($pen, $mid, $y2, $x2, $y2)
    $g.FillEllipse((Brush-Hex $color 220), $x2 - 3, $y2 - 3, 6, 6)
}

function Draw-Glyph([string]$kind, [float]$x, [float]$y, [float]$s, [string]$color) {
    $pen = Pen-Hex $color 2.5 235
    $brush = Brush-Hex $color 190
    switch ($kind) {
        "window" {
            $g.DrawRectangle($pen, $x, $y, $s, $s * 0.75)
            $g.DrawLine($pen, $x + 6, $y + 12, $x + $s - 6, $y + 12)
            $g.DrawLine($pen, $x + ($s * 0.46), $y + 12, $x + ($s * 0.46), $y + ($s * 0.75) - 5)
            $g.DrawLine($pen, $x + 6, $y + ($s * 0.42), $x + $s - 6, $y + ($s * 0.42))
        }
        "terminal" {
            $g.DrawRectangle($pen, $x, $y + 4, $s, $s * 0.66)
            $g.DrawLine($pen, $x + 8, $y + 16, $x + 18, $y + 25)
            $g.DrawLine($pen, $x + 18, $y + 25, $x + 8, $y + 34)
            $g.DrawLine($pen, $x + 28, $y + 36, $x + 44, $y + 36)
        }
        "shield" {
            [System.Drawing.PointF[]]$pts = @(
                [System.Drawing.PointF]::new($x + ($s * 0.5), $y),
                [System.Drawing.PointF]::new($x + $s, $y + ($s * 0.18)),
                [System.Drawing.PointF]::new($x + ($s * 0.84), $y + ($s * 0.74)),
                [System.Drawing.PointF]::new($x + ($s * 0.5), $y + $s),
                [System.Drawing.PointF]::new($x + ($s * 0.16), $y + ($s * 0.74)),
                [System.Drawing.PointF]::new($x, $y + ($s * 0.18)))
            $g.DrawPolygon($pen, $pts)
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.2), $x + ($s * 0.5), $y + ($s * 0.76))
            $g.DrawLine($pen, $x + ($s * 0.32), $y + ($s * 0.48), $x + ($s * 0.68), $y + ($s * 0.48))
        }
        "leaf" {
            $g.DrawEllipse($pen, $x + 8, $y + 2, $s * 0.65, $s * 0.85)
            $g.DrawLine($pen, $x + ($s * 0.22), $y + ($s * 0.78), $x + ($s * 0.74), $y + ($s * 0.18))
            $g.DrawLine($pen, $x + ($s * 0.42), $y + ($s * 0.55), $x + ($s * 0.22), $y + ($s * 0.42))
        }
        "usb" {
            Draw-RoundedRect $x ($y + 8) ($s * 0.52) ($s * 0.72) 5 $color "07101D" 120 2
            $g.DrawRectangle($pen, $x + 12, $y, 18, 12)
            $g.DrawLine($pen, $x + 18, $y + 28, $x + 18, $y + 50)
            $g.DrawLine($pen, $x + 12, $y + 39, $x + 24, $y + 39)
        }
        "disk" {
            $g.DrawEllipse($pen, $x, $y, $s, $s)
            $g.DrawEllipse($pen, $x + ($s * 0.34), $y + ($s * 0.34), $s * 0.32, $s * 0.32)
            $g.DrawArc($pen, $x + 6, $y + 6, $s - 12, $s - 12, 210, 70)
        }
        "partition" {
            for ($ix = 0; $ix -lt 3; $ix++) {
                for ($iy = 0; $iy -lt 3; $iy++) {
                    $g.DrawRectangle($pen, $x + ($ix * $s / 3), $y + ($iy * $s / 3), ($s / 3) - 2, ($s / 3) - 2)
                }
            }
        }
        "health" {
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
        "chip" {
            $g.DrawRectangle($pen, $x + 8, $y + 8, $s - 16, $s - 16)
            for ($i = 0; $i -lt 4; $i++) {
                $p = $y + 14 + ($i * 10)
                $g.DrawLine($pen, $x, $p, $x + 8, $p)
                $g.DrawLine($pen, $x + $s - 8, $p, $x + $s, $p)
            }
            $g.FillRectangle($brush, $x + 24, $y + 24, 14, 14)
        }
        "remote" {
            $g.DrawRectangle($pen, $x, $y + 6, $s, $s * 0.56)
            $g.DrawLine($pen, $x + ($s * 0.42), $y + ($s * 0.7), $x + ($s * 0.58), $y + ($s * 0.7))
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.62), $x + ($s * 0.5), $y + ($s * 0.82))
            $g.DrawEllipse($pen, $x + 18, $y + 18, $s - 36, $s - 36)
        }
        "radar" {
            $g.DrawEllipse($pen, $x + 4, $y + 4, $s - 8, $s - 8)
            $g.DrawEllipse($pen, $x + 18, $y + 18, $s - 36, $s - 36)
            $g.DrawLine($pen, $x + ($s * 0.5), $y + ($s * 0.5), $x + ($s * 0.86), $y + ($s * 0.22))
            $g.FillEllipse($brush, $x + ($s * 0.72), $y + ($s * 0.28), 7, 7)
        }
        "folder" {
            [System.Drawing.PointF[]]$pts = @(
                [System.Drawing.PointF]::new($x, $y + 18),
                [System.Drawing.PointF]::new($x + 20, $y + 18),
                [System.Drawing.PointF]::new($x + 28, $y + 10),
                [System.Drawing.PointF]::new($x + $s, $y + 10),
                [System.Drawing.PointF]::new($x + $s, $y + $s - 8),
                [System.Drawing.PointF]::new($x, $y + $s - 8))
            $g.DrawPolygon($pen, $pts)
        }
        "plus" {
            Draw-RoundedRect ($x + 2) ($y + 8) ($s - 4) ($s * 0.72) 8 $color "07101D" 100 2
            $g.DrawLine($pen, $x + ($s * 0.5), $y + 20, $x + ($s * 0.5), $y + $s - 18)
            $g.DrawLine($pen, $x + 18, $y + ($s * 0.5), $x + $s - 18, $y + ($s * 0.5))
        }
        default {
            $g.DrawEllipse($pen, $x, $y, $s, $s)
            $g.FillEllipse($brush, $x + ($s * 0.36), $y + ($s * 0.36), $s * 0.28, $s * 0.28)
        }
    }
}

function Draw-Item([float]$x, [float]$y, [float]$w, [string]$label, [string]$kind, [string]$color) {
    Draw-RoundedRect $x $y $w 48 8 $color "07101D" 85 1.4
    Draw-Glyph $kind ($x + 14) ($y + 7) 34 $color
    Draw-LeftText $label ($x + 58) ($y + 13) 19 "F8FAFC" ([System.Drawing.FontStyle]::Regular)
    $g.DrawLine((Pen-Hex $color 1.5 170), $x + $w - 32, $y + 17, $x + $w - 22, $y + 24)
    $g.DrawLine((Pen-Hex $color 1.5 170), $x + $w - 22, $y + 24, $x + $w - 32, $y + 31)
}

function Draw-Panel([float]$cx, [float]$cy, [string]$title, [string]$kind, [string]$color) {
    Draw-Octagon $cx $cy 74 $color "06111D" 118
    Draw-Glyph $kind ($cx - 30) ($cy - 38) 60 $color
    Draw-CenteredText $title ($cx - 62) ($cy + 30) 124 36 19 "F8FAFC" ([System.Drawing.FontStyle]::Bold)
}

$bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Rectangle]::new(0, 0, $width, $height),
    (Color-Hex "02050A"),
    (Color-Hex "061525"),
    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
$g.FillRectangle($bg, 0, 0, $width, $height)
$bg.Dispose()

$rand = [Random]::new(7321)
for ($i = 0; $i -lt 190; $i++) {
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
}

$borderPen = Pen-Hex "0284C7" 2 180
$g.DrawRectangle($borderPen, 15, 22, $width - 30, $height - 44)
$g.DrawRectangle((Pen-Hex "0EA5E9" 1 80), 38, 50, $width - 76, $height - 100)
Draw-CenteredText "FORGER DIGITAL SOLUTIONS" 0 18 $width 30 20 "7DD3FC" ([System.Drawing.FontStyle]::Regular)

Draw-Panel 454 224 "OS IMAGES" "window" "38BDF8"
Draw-Panel 432 430 "LINUX MEDIA" "terminal" "84CC16"
Draw-Panel 438 670 "IMAGING" "disk" "F97316"
Draw-Panel 448 840 "USB TOOLS" "usb" "D946EF"
Draw-Panel 1090 224 "DIAG" "health" "22C55E"
Draw-Panel 1088 434 "RECOVERY" "plus" "FACC15"
Draw-Panel 1090 655 "WIN TOOLS" "folder" "06B6D4"
Draw-Panel 768 175 "MULTIBOOT`nCORE" "usb" "06B6D4"
Draw-Panel 768 844 "MEDIC USB" "plus" "EC4899"

$leftX = 78
Draw-LeftText "OS IMAGES" 104 78 20 "67E8F9" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 118 214 "Desktop Image" "window" "38BDF8"
Draw-Item $leftX 174 214 "Modern Image" "window" "38BDF8"
Draw-Item $leftX 230 214 "Server Image" "window" "38BDF8"
Draw-LeftText "LINUX MEDIA" 104 337 20 "A3E635" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 372 214 "Live Terminal" "terminal" "84CC16"
Draw-Item $leftX 428 214 "Security Live" "shield" "84CC16"
Draw-Item $leftX 484 214 "Desktop Live" "leaf" "84CC16"
Draw-LeftText "RESCUE & IMAGING" 132 548 20 "FB923C" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 584 214 "Image Restore" "disk" "F97316"
Draw-Item $leftX 640 214 "Disk Clone" "disk" "F97316"
Draw-Item $leftX 696 214 "Recovery Kit" "plus" "F97316"
Draw-LeftText "USB BUILDERS" 140 766 20 "E879F9" ([System.Drawing.FontStyle]::Bold)
Draw-Item $leftX 802 214 "Multiboot USB" "usb" "D946EF"
Draw-Item $leftX 858 214 "Boot Writer" "usb" "D946EF"
Draw-Item $leftX 914 214 "Image Flasher" "partition" "D946EF"

$rightX = 1246
Draw-LeftText "DIAGNOSTICS" 1260 78 20 "86EFAC" ([System.Drawing.FontStyle]::Bold)
Draw-Item $rightX 118 214 "Memory Check" "chip" "22C55E"
Draw-Item $rightX 174 214 "Hardware Info" "chip" "22C55E"
Draw-Item $rightX 230 214 "Disk Health" "health" "22C55E"
Draw-LeftText "RECOVERY TOOLS" 1256 338 20 "FACC15" ([System.Drawing.FontStyle]::Bold)
Draw-Item $rightX 374 214 "Disk Clone" "disk" "F59E0B"
Draw-Item $rightX 430 214 "Partition Grid" "partition" "F59E0B"
Draw-Item $rightX 486 214 "System Rescue" "plus" "F59E0B"
Draw-LeftText "WINDOWS TOOLS" 1258 590 20 "22D3EE" ([System.Drawing.FontStyle]::Bold)
Draw-Item $rightX 626 214 "Driver Store" "folder" "06B6D4"
Draw-Item $rightX 682 214 "Remote Screen" "remote" "06B6D4"
Draw-Item $rightX 738 214 "Network Radar" "radar" "06B6D4"

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
$g.DrawEllipse((Pen-Hex "F59E0B" 4 235), 646, 486, 244, 244)
$g.DrawEllipse((Pen-Hex "FDE68A" 1.5 180), 630, 470, 276, 276)
Draw-RoundedRect 724 438 88 206 28 "E5E7EB" "111827" 220 2.2
$g.DrawRectangle((Pen-Hex "CBD5E1" 2 230), 744, 430, 48, 42)
$g.FillRectangle((Brush-Hex "111827" 210), 756, 448, 12, 12)
$g.FillRectangle((Brush-Hex "111827" 210), 778, 448, 12, 12)
[System.Drawing.PointF[]]$shield = @(
    [System.Drawing.PointF]::new(768, 505),
    [System.Drawing.PointF]::new(808, 522),
    [System.Drawing.PointF]::new(796, 594),
    [System.Drawing.PointF]::new(768, 617),
    [System.Drawing.PointF]::new(740, 594),
    [System.Drawing.PointF]::new(728, 522))
$g.FillPolygon((Brush-Hex "111827" 230), $shield)
$g.DrawPolygon((Pen-Hex "E5E7EB" 2 210), $shield)
Draw-CenteredText "FE" 736 534 64 52 31 "FDE68A" ([System.Drawing.FontStyle]::Bold)
$g.FillEllipse((Brush-Hex "22D3EE" 240), 764, 624, 9, 9)

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
