# Regenerates the Kronos application icon.
#
# The previous icon was upstream DLSS Swapper's artwork. This draws a simple K monogram instead,
# rendered once at 256px and downscaled for each size, so every entry in the .ico is the same
# letterform rather than a separately rasterised one that drifts between sizes.
#
# Outputs:
#   src\Assets\icon_256.png   256x256, used for <PackageIcon>
#   src\Assets\icon.ico       16/24/32/48/64/128/256, used for the exe, taskbar and installer
#
# Run:  powershell -ExecutionPolicy Bypass -File extras\generate_icon.ps1

param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\Assets')
)

Add-Type -AssemblyName System.Drawing

$Master = 256
$BackgroundTop = [System.Drawing.Color]::FromArgb(255, 42, 42, 54)
$BackgroundBottom = [System.Drawing.Color]::FromArgb(255, 18, 18, 26)
$Accent = [System.Drawing.Color]::FromArgb(255, 94, 234, 212)

function New-MasterBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.Color]::Transparent)

        # Rounded square, inset slightly so the glyph has room to breathe.
        $inset = [int]($Size * 0.06)
        $box = New-Object System.Drawing.Rectangle($inset, $inset, ($Size - 2 * $inset), ($Size - 2 * $inset))
        $radius = [int]($Size * 0.22)

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $radius * 2
        $path.AddArc($box.X, $box.Y, $d, $d, 180, 90)
        $path.AddArc($box.Right - $d, $box.Y, $d, $d, 270, 90)
        $path.AddArc($box.Right - $d, $box.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($box.X, $box.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $box, $BackgroundTop, $BackgroundBottom,
            [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $graphics.FillPath($brush, $path)

        # A short accent bar under the letterform. Kept as a shape rather than a glyph so it
        # renders identically at 16px.
        $barHeight = [Math]::Max(2, [int]($Size * 0.045))
        $barWidth = [int]($Size * 0.30)
        $barX = [int](($Size - $barWidth) / 2)
        $barY = [int]($Size * 0.755)
        $graphics.FillRectangle((New-Object System.Drawing.SolidBrush($Accent)), $barX, $barY, $barWidth, $barHeight)

        # The K.
        $font = $null
        foreach ($candidate in @('Segoe UI Black', 'Segoe UI Semibold', 'Arial Black', 'Arial')) {
            try {
                $font = New-Object System.Drawing.Font($candidate, [float]($Size * 0.52), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
                break
            } catch { $font = $null }
        }
        if ($font -eq $null) { throw 'No usable font found for the icon glyph.' }

        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center

        $textBox = New-Object System.Drawing.RectangleF(0, ([float]($Size * 0.06)), $Size, ([float]($Size * 0.70)))
        $graphics.DrawString('K', $font, (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)), $textBox, $format)

        $format.Dispose()
        $brush.Dispose()
        $path.Dispose()
        $font.Dispose()
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Get-BmpEntryBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    # GDI+ and some installer tooling will not parse PNG-compressed icon entries below 256px, and
    # fail with a bare "Out of memory". So everything smaller than 256 is written as a classic DIB
    # and only the 256 entry uses PNG, which is the arrangement Windows itself produces.
    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $w * 4
        $pixels = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # BITMAPINFOHEADER. Height is doubled because an icon's DIB holds the colour rows followed by
    # the 1bpp AND mask.
    $writer.Write([UInt32]40)          # biSize
    $writer.Write([Int32]$w)          # biWidth
    $writer.Write([Int32]($h * 2))    # biHeight
    $writer.Write([UInt16]1)          # biPlanes
    $writer.Write([UInt16]32)         # biBitCount
    $writer.Write([UInt32]0)          # biCompression BI_RGB
    $writer.Write([UInt32]0)          # biSizeImage
    $writer.Write([Int32]0)           # biXPelsPerMeter
    $writer.Write([Int32]0)           # biYPelsPerMeter
    $writer.Write([UInt32]0)          # biClrUsed
    $writer.Write([UInt32]0)          # biClrImportant

    # Colour rows, bottom-up, as BGRA.
    for ($y = $h - 1; $y -ge 0; $y--) {
        $rowStart = $y * $stride
        for ($x = 0; $x -lt $w; $x++) {
            $i = $rowStart + ($x * 4)
            $writer.Write($pixels[$i + 0])   # B
            $writer.Write($pixels[$i + 1])   # G
            $writer.Write($pixels[$i + 2])   # R
            $writer.Write($pixels[$i + 3])   # A
        }
    }

    # AND mask, 1bpp, rows padded to a 4 byte boundary. Left all zero: for a 32bpp icon carrying a
    # real alpha channel the mask is ignored, and Windows only falls back to it for icons without
    # one. Writing it as all zeros is what current tooling does and avoids a second source of
    # truth for the shape.
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $writer.Write((New-Object byte[] ($maskStride * $h)), 0, ($maskStride * $h))

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    # The comma is load bearing. Without it PowerShell unrolls the byte array into individual bytes
    # on the way out, and the caller ends up appending thousands of one byte entries instead of one
    # image, producing a directory that describes a file which was never written.
    return ,$bytes
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$master = New-MasterBitmap -Size $Master

try {
    # 256 PNG for <PackageIcon>.
    $pngPath = Join-Path $OutputDirectory 'icon_256.png'
    $master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "wrote $pngPath"

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $images = @()
    foreach ($size in $sizes) {
        $scaled = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g2 = [System.Drawing.Graphics]::FromImage($scaled)
        try {
            $g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g2.Clear([System.Drawing.Color]::Transparent)
            $g2.DrawImage($master, 0, 0, $size, $size)
        }
        finally {
            $g2.Dispose()
        }

        if ($size -ge 256) {
            $ms = New-Object System.IO.MemoryStream
            $scaled.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngBytes = $ms.ToArray()
            $images += ,$pngBytes
            $ms.Dispose()
        }
        else {
            $images += ,(Get-BmpEntryBytes -Bitmap $scaled)
        }
        $scaled.Dispose()
    }

    $icoPath = Join-Path $OutputDirectory 'icon.ico'
    $output = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($output)

    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $data = $images[$i]
        $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $data.Length
    }
    foreach ($data in $images) {
        $writer.Write($data)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($icoPath, $output.ToArray())
    $writer.Dispose()
    $output.Dispose()

    Write-Host "wrote $icoPath ($($sizes.Count) sizes: $($sizes -join ', '))"
}
finally {
    $master.Dispose()
}
