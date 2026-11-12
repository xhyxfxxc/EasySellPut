# Launch the dev build with a given page/language/theme and save a window screenshot to scratch\screens.
#   build\shot.ps1 -Name m0-settings-en-light -Page Settings -Language en-US -Theme Light [-WaitSeconds 6]
param([Parameter(Mandatory)][string]$Name, [string]$Page = 'Dashboard', [string]$Language = 'zh-CN',
      [ValidateSet('System','Light','Dark')][string]$Theme = 'Dark', [int]$WaitSeconds = 6)
$root = Split-Path -Parent $PSScriptRoot
$data = Join-Path $root 'scratch\appdata'
New-Item -ItemType Directory -Force $data | Out-Null
$sf = Join-Path $data 'settings.json'
$s = if (Test-Path $sf) { Get-Content $sf -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
$s | Add-Member -Force NoteProperty language $Language
$s | Add-Member -Force NoteProperty theme $Theme
$s | Add-Member -Force NoteProperty window $null
[IO.File]::WriteAllText($sf, ($s | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
Get-Process MeiliTerminal -ErrorAction SilentlyContinue | Stop-Process -Force
$exe = Join-Path $root 'src\TyuStock.App\bin\Debug\net10.0-windows10.0.19041.0\MeiliTerminal.exe'
$p = Start-Process $exe -ArgumentList '--data-dir', $data, '--page', $Page -PassThru
Start-Sleep $WaitSeconds
if ($p.HasExited) { throw "app exited (code $($p.ExitCode))" }
& (Join-Path $PSScriptRoot 'capture-window.ps1') -ProcessId $p.Id -OutFile (Join-Path $root "scratch\screens\$Name.png")
$p | Stop-Process -Force
$crash = Join-Path $data 'logs\crash.log'
if (Test-Path $crash) { "CRASH LOG:"; Get-Content $crash | Select -Last 15 }
