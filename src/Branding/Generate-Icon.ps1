# Run with Windows PowerShell in STA mode.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script with powershell.exe -NoProfile -Sta.'
}
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$art = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'icon.xaml')))
$frames = foreach ($size in 16, 24, 32, 48, 256) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try { $context.DrawImage($art, [Windows.Rect]::new(0, 0, $size, $size)) }
    finally { $context.Close() }

    $image = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $png = [IO.MemoryStream]::new()
    try {
        $encoder.Save($png)
        [pscustomobject]@{ Size = $size; Bytes = $png.ToArray() }
    }
    finally { $png.Dispose() }
}

$icon = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($icon)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = [byte]($frame.Size % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'DesktopMotionStudio.ico'), $icon.ToArray())
}
finally { $writer.Dispose() }

$mark = $frames | Where-Object Size -eq 256
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'BrandMark.png'), $mark.Bytes)
