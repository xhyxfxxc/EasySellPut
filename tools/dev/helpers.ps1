# Dev helpers for scripted edits: . C:\tyuStockApp\tools\dev\helpers.ps1
. C:\tyuStockApp\tools\env.ps1 | Out-Null
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$global:U8 = New-Object Text.UTF8Encoding $false
Set-Location C:\tyuStockApp
function global:W([string]$p, [string]$c) {
  $full = Join-Path 'C:\tyuStockApp' $p
  New-Item -ItemType Directory -Force (Split-Path -Parent $full) | Out-Null
  [IO.File]::WriteAllText($full, $c.Replace("`r`n", "`n").Replace("`n", "`r`n"), $global:U8)
}
function global:LogLine([string]$t) {
  [IO.File]::AppendAllText('C:\tyuStockApp\docs\02-中断恢复记录.md', "- $(Get-Date -Format 'yyyy-MM-dd HH:mm') $t`r`n", $global:U8)
}
function global:Checkpoint([string]$msg, [string]$log) { LogLine $log; git add -A; git commit -q -m $msg; git log --oneline -1 }
function global:B { dotnet build TyuStockApp.slnx -v q -nologo 2>&1 | Select-String -Pattern 'error|warning CS|Warn|Error\(s\)' | Select-Object -First 40 | ForEach-Object { $_.Line } }
function global:Patch([string]$p, [string]$old, [string]$new) {
  $f = Join-Path 'C:\tyuStockApp' $p
  $s = [IO.File]::ReadAllText($f).Replace("`r`n", "`n"); $o = $old.Replace("`r`n", "`n"); $n = $new.Replace("`r`n", "`n")
  if (-not $s.Contains($o)) { throw "patch miss in $p : $old" }
  [IO.File]::WriteAllText($f, $s.Replace($o, $n).Replace("`n", "`r`n"), $global:U8)
}