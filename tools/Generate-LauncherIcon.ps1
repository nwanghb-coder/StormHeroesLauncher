# Original geometric artwork for StormHeroesLauncher. No third-party inputs or downloads.
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '..\src\StormHeroesLauncher\Assets'
$sizes = @(16,24,32,48,64,256)
$images = @()
foreach ($size in $sizes) {
    $scale = 4
    $bitmap = [Drawing.Bitmap]::new($size*$scale,$size*$scale)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform(($size*$scale/64.0),($size*$scale/64.0))
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(2,2,16,16,180,90)
    $tile.AddArc(46,2,16,16,270,90)
    $tile.AddArc(46,46,16,16,0,90)
    $tile.AddArc(2,46,16,16,90,90)
    $tile.CloseFigure()
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#25364A'))
    $ink = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#EAF2F8'),3.5)
    $accent = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#62D5CB'))
    $graphics.FillPath($background,$tile)
    $graphics.DrawRectangle($ink,11,14,42,37)
    $graphics.DrawLine($ink,11,24,53,24)
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(26,30),[Drawing.PointF]::new(42,38),[Drawing.PointF]::new(26,46))
    $graphics.FillPolygon($accent,$points)
    $small = [Drawing.Bitmap]::new($size,$size)
    $output = [Drawing.Graphics]::FromImage($small)
    $output.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $output.DrawImage($bitmap,0,0,$size,$size)
    $memory = [IO.MemoryStream]::new()
    $small.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,$memory.ToArray()
    if ($size -eq 256) { $small.Save((Join-Path $destination 'Launcher-preview.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $output.Dispose(); $small.Dispose(); $accent.Dispose(); $ink.Dispose(); $background.Dispose(); $tile.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $destination 'Launcher.ico'))
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
    $writer.Write([Byte]$dimension); $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0); $writer.Write([Byte]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
$writer.Dispose(); $file.Dispose()
