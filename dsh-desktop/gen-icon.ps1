# gen-icon.ps1 — 生成应用图标 app.ico（多尺寸 PNG 内嵌，16/32/48/64/128/256）
# 设计：深色圆角方块背景 + 蓝色圆 + 白色 "D"
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File gen-icon.ps1

[CmdletBinding()]
param()

Add-Type -AssemblyName System.Drawing

function New-AppIconBitmap {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    # 圆角矩形背景
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]$Size * 0.16
    $rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 30, 37))
    $g.FillPath($bg, $path)
    # 蓝色圆
    $cx = $Size / 2.0; $cy = $Size / 2.0; $cr = $Size * 0.34
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 154, 255))
    $g.FillEllipse($blue, [float]($cx - $cr), [float]($cy - $cr), [float]($cr * 2), [float]($cr * 2))
    # 白色字母 D
    $font = New-Object System.Drawing.Font('Segoe UI', [float]($Size * 0.42), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
    $g.DrawString('D', $font, $white, $textRect, $sf)
    $g.Dispose()
    return $bmp
}

function Save-IcoPng {
    param([string]$Path, [int[]]$Sizes)
    $images = @()
    $streams = @()
    foreach ($s in $Sizes) {
        $bmp = New-AppIconBitmap $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $streams += , $ms
        $images += , $bmp
    }
    $count = $Sizes.Count
    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    # ICONDIR
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$count)
    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $s = $Sizes[$i]
        $w = if ($s -ge 256) { 0 } else { $s }
        $h = $w
        $bw.Write([Byte]$w); $bw.Write([Byte]$h)          # width / height (0 = 256)
        $bw.Write([Byte]0); $bw.Write([Byte]0)            # colorCount / reserved
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)       # planes / bitCount
        $bw.Write([UInt32]$streams[$i].Length)            # bytesInRes
        $bw.Write([UInt32]$offset)                        # imageOffset
        $offset += [int]$streams[$i].Length
    }
    for ($i = 0; $i -lt $count; $i++) {
        $bw.Write($streams[$i].ToArray())
        $streams[$i].Dispose()
        $images[$i].Dispose()
    }
    $bw.Dispose()
    $fs.Dispose()
}

$out = Join-Path $PSScriptRoot 'app.ico'
Save-IcoPng $out @(16, 32, 48, 64, 128, 256)
Write-Output ("已生成: " + $out + " (" + (Get-Item $out).Length + " bytes)")
