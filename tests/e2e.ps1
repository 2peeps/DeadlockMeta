# End-to-end check without the game: shows a saved hero-select screenshot in a borderless window on the second
# monitor, points the cursor at a few cards, grabs what is on screen and measures the overlay's CPU use.
# -Image picks the screenshot shown as the "game"; -Shots is "name:x:y,..." in that screenshot's pixels.
param([string]$AppPath = "$PSScriptRoot\..\app\DeadlockMeta.exe", [string]$Image = "select_1080.png",
      [string]$Shots = "abrams:105:210,ratking:189:613,empty:780:780")
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class U {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
'@
[U]::SetProcessDPIAware() | Out-Null
$ox = -1920; $oy = 6
$script:img = [Drawing.Image]::FromFile((Resolve-Path (Join-Path $PSScriptRoot $Image)))
$script:showImage = $true
$form = New-Object Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.BackColor = 'Black'
$form.Location = New-Object Drawing.Point $ox, $oy; $form.Size = New-Object Drawing.Size 1920, 1080
$form.Add_Paint({ param($s, $e) if ($script:showImage) { $e.Graphics.DrawImage($script:img, (New-Object Drawing.Rectangle 0, 0, $script:img.Width, $script:img.Height)) } })
$form.Show()
[U]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [U]::SetForegroundWindow($form.Handle) | Out-Null; [U]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
function Pump($ms) { $t = [Diagnostics.Stopwatch]::StartNew(); while ($t.ElapsedMilliseconds -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 } }
function Cpu($label, $ms) { $exe.Refresh(); $c = $exe.TotalProcessorTime.TotalSeconds; Pump $ms; $exe.Refresh(); "{0}: {1:N1}% of one core" -f $label, (($exe.TotalProcessorTime.TotalSeconds - $c) / ($ms / 1000) * 100) }
Pump 300
"foreground is test window: $([U]::GetForegroundWindow() -eq $form.Handle)"
$saved = [Windows.Forms.Cursor]::Position
$env:DEADLOCKMETA_PROCESS = 'powershell'
$exe = Start-Process $AppPath -ArgumentList '--tray' -PassThru
Pump 15000
$points = @($Shots -split "," | % { $p = $_ -split ":"; , @($p[0], [int]$p[1], [int]$p[2]) })
foreach ($s in $points) {
  [U]::SetCursorPos($ox + $s[1], $oy + $s[2]) | Out-Null
  # Long enough for the panel to be drawn before the screen is grabbed.
  Pump 1200
  $bmp = New-Object Drawing.Bitmap 1300, 520
  $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($ox, $oy, 0, 0, $bmp.Size); $g.Dispose()
  $bmp.Save("$PSScriptRoot\e2e_$($s[0]).png"); $bmp.Dispose()
  # The panel must never take the keyboard from the game (Esc and everything else would stop reaching it).
  "{0}: game window still has focus: {1}" -f $s[0], ([U]::GetForegroundWindow() -eq $form.Handle)
}
Cpu 'hero select open' 8000
# A real in-match frame: the watcher keeps searching for the grid on it, which is the cost during a match.
$script:img.Dispose(); $script:img = [Drawing.Image]::FromFile((Resolve-Path "$PSScriptRoot\match_1080.png")); $form.Invalidate()
Cpu 'game in foreground, other screen (like in a match)' 12000
$exe.Refresh(); "memory: working set {0:N0} MB, private {1:N0} MB" -f ($exe.WorkingSet64 / 1MB), ($exe.PrivateMemorySize64 / 1MB)
[U]::SetCursorPos($saved.X, $saved.Y) | Out-Null
Stop-Process -Id $exe.Id -Force
$form.Close(); $script:img.Dispose()
