[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../src/Moye/Assets'),
    [string]$Name = 'Penroam'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $SourcePng).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null
$source = [System.Drawing.Bitmap]::new($sourcePath)

function Resize-IconBitmap {
    param([System.Drawing.Image]$Image, [int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.PageUnit = [System.Drawing.GraphicsUnit]::Pixel
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $graphics.DrawImage($Image, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size),
            0, 0, $Image.Width, $Image.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    }
    finally {
        $attributes.Dispose()
        $graphics.Dispose()
    }
    return $bitmap
}

try {
    if ($source.Width -ne $source.Height) {
        throw 'The icon source must have a square canvas.'
    }

    $master = Resize-IconBitmap -Image $source -Size 1024
    try {
        $master.Save((Join-Path $outputPath "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $master.Dispose()
    }

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $frames = foreach ($size in $sizes) {
        $bitmap = Resize-IconBitmap -Image $source -Size $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            if ($size -eq 256) {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            else {
                # A 32-bit DIB plus an AND mask keeps small sizes compatible with
                # Windows icon readers. The largest frame uses PNG compression.
                $writer = [System.IO.BinaryWriter]::new($stream, [System.Text.Encoding]::UTF8, $true)
                try {
                    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                    $writer.Write([uint32]40)
                    $writer.Write([int32]$size)
                    $writer.Write([int32]($size * 2))
                    $writer.Write([uint16]1)
                    $writer.Write([uint16]32)
                    $writer.Write([uint32]0)
                    $writer.Write([uint32]($size * $size * 4 + $maskStride * $size))
                    $writer.Write([int32]0)
                    $writer.Write([int32]0)
                    $writer.Write([uint32]0)
                    $writer.Write([uint32]0)
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        for ($x = 0; $x -lt $size; $x++) {
                            $pixel = $bitmap.GetPixel($x, $y)
                            $writer.Write([byte]$pixel.B)
                            $writer.Write([byte]$pixel.G)
                            $writer.Write([byte]$pixel.R)
                            $writer.Write([byte]$pixel.A)
                        }
                    }
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        $mask = [byte[]]::new($maskStride)
                        for ($x = 0; $x -lt $size; $x++) {
                            if ($bitmap.GetPixel($x, $y).A -eq 0) {
                                $byteIndex = [int][Math]::Floor($x / 8.0)
                                $mask[$byteIndex] = $mask[$byteIndex] -bor (1 -shl (7 - ($x % 8)))
                            }
                        }
                        $writer.Write($mask)
                    }
                }
                finally {
                    $writer.Dispose()
                }
            }
            [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $iconPath = Join-Path $outputPath "$Name.ico"
    $file = [System.IO.File]::Create($iconPath)
    $iconWriter = [System.IO.BinaryWriter]::new($file)
    try {
        $iconWriter.Write([uint16]0)
        $iconWriter.Write([uint16]1)
        $iconWriter.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
            $iconWriter.Write($dimension)
            $iconWriter.Write($dimension)
            $iconWriter.Write([byte]0)
            $iconWriter.Write([byte]0)
            $iconWriter.Write([uint16]1)
            $iconWriter.Write([uint16]32)
            $iconWriter.Write([uint32]$frame.Bytes.Length)
            $iconWriter.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) {
            $iconWriter.Write([byte[]]$frame.Bytes)
        }
    }
    finally {
        $iconWriter.Dispose()
        $file.Dispose()
    }
    Write-Output "Created $Name.png (1024 x 1024) and $Name.ico ($($sizes -join ', ') px) in $outputPath"
}
finally {
    $source.Dispose()
}
