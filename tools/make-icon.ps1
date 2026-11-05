# Regenerates assets/icons/EveLocalMon.ico (the exe icon) and EveLocalMon.png (256 px, the window icon): the title-bar radar glyph on a dark tile, at the usual
# Windows sizes. The hex values are Palette.cs's (Accent #14b0d0, TitleBar #101414, Hairline #2a3030);
# if the palette changes, change them here and rerun:  pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\assets\icons\EveLocalMon.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$accent = [System.Drawing.ColorTranslator]::FromHtml('#14b0d0')
$tile = [System.Drawing.ColorTranslator]::FromHtml('#101414')
$edge = [System.Drawing.ColorTranslator]::FromHtml('#2a3030')

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Square tile, like everything else in the app; a hairline edge only where it is big enough to see.
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $tile), 0, 0, $size, $size)
    if ($size -ge 32) {
        $g.DrawRectangle((New-Object System.Drawing.Pen $edge, ([single][Math]::Max(1, $size / 64))), 0.5, 0.5, $size - 1, $size - 1)
    }

    # The glyph (16-unit design grid, as in Icons.cs): rings r=6.5 and r=3 around (8,8), sweep to (13,3.5).
    $u = $size / 16.0
    $stroke = [single][Math]::Max(1.3, $size / 13.0)
    $pen = New-Object System.Drawing.Pen $accent, $stroke
    $pen.StartCap = 'Flat'; $pen.EndCap = 'Flat'
    foreach ($r in 6.5, 3.0) {
        $g.DrawEllipse($pen, [single]((8 - $r) * $u), [single]((8 - $r) * $u), [single](2 * $r * $u), [single](2 * $r * $u))
    }
    $g.DrawLine($pen, [single](8 * $u), [single](8 * $u), [single](13 * $u), [single](3.5 * $u))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (New-IconPng $s) }

# ICO container: header, one directory entry per image, then the PNG payloads (PNG-in-ICO, Vista+).
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
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
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + '\EveLocalMon.ico', $ms.ToArray())
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + '\EveLocalMon.png', $images[$sizes.Count - 1])
Write-Output "wrote $out ($($ms.Length) bytes, sizes: $($sizes -join ', '))"
