[CmdletBinding()]
param(
    [string]$Source,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $root 'assets\Explorer3DPreview-icon-source.png'
}
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'assets\Explorer3DPreview.ico'
}
if (-not (Test-Path -LiteralPath $Source)) {
    throw "Icon source was not found: $Source"
}

Add-Type -AssemblyName System.Drawing
$sourceImage = [Drawing.Bitmap]::FromFile($Source)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [Collections.Generic.List[byte[]]]::new()

try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($sourceImage, [Drawing.Rectangle]::new(0, 0, $size, $size))
            } finally {
                $graphics.Dispose()
            }
            $stream = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                $images.Add($stream.ToArray())
            } finally {
                $stream.Dispose()
            }
        } finally {
            $bitmap.Dispose()
        }
    }
} finally {
    $sourceImage.Dispose()
}

$outputDirectory = Split-Path -Parent $Output
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$file = [IO.File]::Create($Output)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $dimension = if ($size -eq 256) { [byte]0 } else { [byte]$size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) {
        $writer.Write($image)
    }
} finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Icon ready: $Output" -ForegroundColor Green
