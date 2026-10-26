$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$assetDirectory = Join-Path $PSScriptRoot '..\Assets'
$assetDirectory = [System.IO.Path]::GetFullPath($assetDirectory)
$paths = @(
    'M 501,302 C 517,302 531,307 546,316 L 825,477 C 859,497 880,535 880,574 C 880,618 853,654 812,670 L 555,765 C 523,777 498,801 481,831 C 472,847 466,866 466,889 C 464,869 464,849 466,830 C 471,782 495,748 539,724 L 727,621 C 741,613 747,604 747,592 C 747,577 739,567 723,560 L 544,495 C 487,474 466,430 466,362 C 466,325 476,302 501,302 Z',
    'M 405,478 C 413,478 421,481 429,486 L 499,527 C 510,534 515,543 515,555 L 515,629 C 515,641 510,649 500,655 L 420,701 C 402,711 385,701 385,681 L 385,500 C 385,486 393,478 405,478 Z',
    'M 731,570 C 779,583 818,599 840,619 C 857,636 858,654 846,673 C 837,687 821,695 802,702 L 585,790 C 533,811 494,849 478,899 C 470,882 466,861 466,840 C 466,796 489,761 530,738 L 727,628 C 749,616 757,594 731,570 Z'
)

function New-LogoDrawing {
    $group = [System.Windows.Media.DrawingGroup]::new()
    $group.Transform = [System.Windows.Media.MatrixTransform]::new([System.Windows.Media.Matrix]::new(0.75, 0, 0, 0.75, -218, -200))
    $colors = @(
        @('#FF21DDF3', '#FF0758FF', '#FF19D8F1'),
        @('#FF29E0F1', '#FF0758FF'),
        @('#FF9367FF', '#FF111DFF', '#FF18D9F2')
    )
    for ($index = 0; $index -lt $paths.Count; $index++) {
        $brush = [System.Windows.Media.LinearGradientBrush]::new()
        $brush.StartPoint = [System.Windows.Point]::new(0, 0)
        $brush.EndPoint = [System.Windows.Point]::new(1, 1)
        for ($stop = 0; $stop -lt $colors[$index].Count; $stop++) {
            $offset = if ($colors[$index].Count -eq 1) { 0 } else { $stop / ($colors[$index].Count - 1) }
            $brush.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString($colors[$index][$stop]), $offset))
        }
        $group.Children.Add([System.Windows.Media.GeometryDrawing]::new($brush, $null, [System.Windows.Media.Geometry]::Parse($paths[$index])))
    }
    return $group
}

function New-RenderedBitmap([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.DrawDrawing((New-LogoDrawing))
    $context.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return $bitmap
}

$logo = New-RenderedBitmap 512
$pngEncoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$pngEncoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($logo))
$pngStream = [System.IO.File]::Create((Join-Path $assetDirectory 'AppFlowLogo.png'))
try { $pngEncoder.Save($pngStream) } finally { $pngStream.Dispose() }

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = [System.Collections.Generic.List[object]]::new()
foreach ($size in $sizes) {
    $bitmap = New-RenderedBitmap $size
    if ($size -eq 256) {
        $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $stream = [System.IO.MemoryStream]::new()
        $encoder.Save($stream)
        $bytes = $stream.ToArray()
        $stream.Dispose()
        $entries.Add([pscustomobject]@{ Size = $size; Data = $bytes })
        continue
    }

    $stride = $size * 4
    $pixels = [byte[]]::new($stride * $size)
    $bitmap.CopyPixels($pixels, $stride, 0)
    $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
    $mask = [byte[]]::new($maskStride * $size)
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $pixel = (($y * $size) + $x) * 4
            if ($pixels[$pixel + 3] -eq 0) {
                $maskIndex = (($size - 1 - $y) * $maskStride) + [int][math]::Floor($x / 8)
                $maskIndex = [int]$maskIndex
                if ($maskIndex -lt 0 -or $maskIndex -ge $mask.Length) { throw "Invalid icon mask index $maskIndex (size=$size, stride=$maskStride, length=$($mask.Length), x=$x, y=$y)" }
                $mask[$maskIndex] = [byte]($mask[$maskIndex] -bor (128 -shr ($x % 8)))
            }
        }
    }
    $dataStream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($dataStream)
    $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]($pixels.Length + $mask.Length)); $writer.Write([int32]0); $writer.Write([int32]0)
    $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $stride, $stride) }
    $writer.Write($mask)
    $writer.Flush()
    $entries.Add([pscustomobject]@{ Size = $size; Data = $dataStream.ToArray() })
    $writer.Dispose(); $dataStream.Dispose()
}

$iconPath = Join-Path $assetDirectory 'AppFlow.ico'
$iconStream = [System.IO.File]::Create($iconPath)
$iconWriter = [System.IO.BinaryWriter]::new($iconStream)
$iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$entries.Count)
$offset = 6 + (16 * $entries.Count)
foreach ($entry in $entries) {
    $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
    $iconWriter.Write([byte]$dimension); $iconWriter.Write([byte]$dimension)
    $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
    $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
    $iconWriter.Write([uint32]$entry.Data.Length); $iconWriter.Write([uint32]$offset)
    $offset += $entry.Data.Length
}
foreach ($entry in $entries) { $iconWriter.Write([byte[]]$entry.Data) }
$iconWriter.Flush(); $iconWriter.Dispose(); $iconStream.Dispose()
Write-Output "Generated AppFlowLogo.png and AppFlow.ico in $assetDirectory"
