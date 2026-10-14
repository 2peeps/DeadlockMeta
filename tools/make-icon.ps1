# Draws the app icon (a rising chart line on a dark tile, in the app's colours) and writes assets\icon.ico.
Add-Type -AssemblyName System.Drawing
$sizes = 256, 128, 64, 48, 32, 24, 16
$pngs = foreach ($size in $sizes) {
  $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.ScaleTransform($size / 256, $size / 256)

  $r = 56; $d = $r * 2
  $tile = New-Object Drawing.Drawing2D.GraphicsPath
  $tile.AddArc(4, 4, $d, $d, 180, 90); $tile.AddArc(252 - $d, 4, $d, $d, 270, 90)
  $tile.AddArc(252 - $d, 252 - $d, $d, $d, 0, 90); $tile.AddArc(4, 252 - $d, $d, $d, 90, 90); $tile.CloseFigure()
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0x0F, 0x14, 0x13))), $tile)
  $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(0x2A, 0x36, 0x32)), 8), $tile)

  # Small sizes get a thicker line so it stays readable in the tray.
  $w = if ($size -le 32) { 34 } else { 26 }
  $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(0x6E, 0xE7, 0xC5)), $w
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
  $pts = [Drawing.PointF[]]@((New-Object Drawing.PointF 54, 182), (New-Object Drawing.PointF 106, 122),
    (New-Object Drawing.PointF 146, 152), (New-Object Drawing.PointF 198, 80))
  $g.DrawLines($pen, $pts)
  $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0xEF, 0xE4, 0xCB))), 198 - 24, 80 - 24, 48, 48)
  $g.Dispose()

  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  , $ms.ToArray()
}

$out = Join-Path $PSScriptRoot '..\assets\icon.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$fs = [IO.File]::Create($out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $bw.Write([byte]($sizes[$i] % 256)); $bw.Write([byte]($sizes[$i] % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"wrote $out"
