# Launch TyuStockApp with given args, wait, capture its main window (PrintWindow, works when covered), then close it.
param([string]$Exe, [string[]]$AppArgs, [string]$Out, [int]$WaitSeconds = 12)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[W32]::SetProcessDPIAware() | Out-Null
$p = Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
try {
  $deadline = (Get-Date).AddSeconds(30)
  while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300; $p.Refresh() }
  if ($p.MainWindowHandle -eq 0) { throw "window not found" }
  Start-Sleep -Seconds $WaitSeconds
  $r = New-Object W32+RECT; [W32]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  [W32]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
  $bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "saved $Out"
} finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(5000)) { $p.Kill() } }
}