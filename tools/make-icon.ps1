<#
.SYNOPSIS
    Draws src/DesktopDeck/DesktopDeck.ico: a rounded tile in the dusk theme's terracotta accent with a light dashboard grid.

.DESCRIPTION
    Sizes 16, 24, 32, 48, 64 and 256, each drawn at its own size (so the small ones stay crisp) and stored as PNG inside the .ico.
    Run it again after changing the look; the colors are the dusk theme's accent (#C4684F) and the text color on it (#FFF6EC).
#>
[CmdletBinding()]
param([string]$Output)

$ErrorActionPreference = 'Stop'
if (-not $Output) { $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\DesktopDeck\DesktopDeck.ico' }
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # The tile: accent fill with a slightly darker lower edge.
    $tile = New-RoundedPath 0 0 $size $size ($size * 0.22)
    $top = [System.Drawing.ColorTranslator]::FromHtml('#D2775E')
    $bottom = [System.Drawing.ColorTranslator]::FromHtml('#B65A43')
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $size)), $top, $bottom)
    $g.FillPath($fill, $tile)

    # The glyph: four cards in a 2x2 grid, the top-left one wide (a dashboard). Light parchment on the accent.
    $light = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#FFF6EC'))
    $soft = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 246, 236))
    $margin = $size * 0.22
    $gap = $size * 0.07
    $inner = $size - 2 * $margin
    $cell = ($inner - $gap) / 2
    $radius = [Math]::Max(1, $size * 0.05)
    $x0 = $margin; $x1 = $margin + $cell + $gap
    $y0 = $margin; $y1 = $margin + $cell + $gap
    $cards = @(
        @($x0, $y0, $inner, $cell, $light),     # wide card on top
        @($x0, $y1, $cell, $cell, $soft),
        @($x1, $y1, $cell, $cell, $light)
    )
    foreach ($c in $cards) {
        $p = New-RoundedPath $c[0] $c[1] $c[2] $c[3] $radius
        $g.FillPath($c[4], $p)
        $p.Dispose()
    }
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $stream.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = foreach ($s in $sizes) { , (New-IconPng $s) }

# ICO container: header, one directory entry per image, then the PNG data.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

$full = [System.IO.Path]::GetFullPath($Output)
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
Write-Host "Wrote $full ($($out.Length) bytes)"
