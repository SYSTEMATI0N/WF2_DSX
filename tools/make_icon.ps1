# Builds a multi-resolution Windows .ico (PNG-compressed entries) from a source PNG.
# Center-crops the source to a square first. Usage:
#   pwsh tools/make_icon.ps1 -Source assets/wf2_dsx_icon.png -Output src/Wf2Dsx/app.ico
param(
    [string]$Source = "assets/wf2_dsx_icon.png",
    [string]$Output = "src/Wf2Dsx/app.ico"
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
$side = [Math]::Min($src.Width, $src.Height)
$x = [int](($src.Width - $side) / 2)
$y = [int](($src.Height - $side) / 2)

# Center-crop to a square.
$square = New-Object System.Drawing.Bitmap($side, $side)
$g = [System.Drawing.Graphics]::FromImage($square)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $side, $side)), `
    (New-Object System.Drawing.Rectangle($x, $y, $side, $side)), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $bg = [System.Drawing.Graphics]::FromImage($bmp)
    $bg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $bg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $bg.DrawImage($square, 0, 0, $s, $s)
    $bg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
    $ms.Dispose()
}
$square.Dispose()

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
# ICONDIR
$bw.Write([UInt16]0)            # reserved
$bw.Write([UInt16]1)            # type = icon
$bw.Write([UInt16]$sizes.Count) # image count

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bytes = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in the directory entry
    $bw.Write([Byte]$dim)        # width
    $bw.Write([Byte]$dim)        # height
    $bw.Write([Byte]0)           # palette
    $bw.Write([Byte]0)           # reserved
    $bw.Write([UInt16]1)         # color planes
    $bw.Write([UInt16]32)        # bits per pixel
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $pngs) { $bw.Write($bytes) }
$bw.Flush()

$outPath = Join-Path (Get-Location) $Output
[System.IO.File]::WriteAllBytes($outPath, $out.ToArray())
$bw.Dispose()
$out.Dispose()
Write-Host "Wrote $outPath ($([System.IO.File]::ReadAllBytes($outPath).Length) bytes, sizes: $($sizes -join ', '))"
