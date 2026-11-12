#requires -Version 5.1
<#
.Synopsis
  B3 acceptance run: launches the app's built-in self-check (--smoke) against a throw-away data folder under
  scratch\acceptance, checks the exe's file properties, optionally the portable layout, and writes a Markdown report.
.Notes
  Run after `. C:\tyuStockApp\tools\env.ps1`. The app window stays off-screen and never activates; no toasts, no tray
  balloon, no installer changes. Installer install/uninstall is NOT automated here (it would add Start-menu entries
  to the real user profile) — see docs\05 for the manual checklist.
.Example
  .\build\acceptance.ps1                       # Debug build output
  .\build\acceptance.ps1 -Exe dist\...\x.exe -Portable
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [switch]$Portable,
    [switch]$Offline,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $Root "src\TyuStock.App\bin\Debug\net10.0-windows10.0.19041.0\MeiliTerminal.exe" }
$Exe = (Resolve-Path $Exe).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$Work = Join-Path $Root "scratch\acceptance\$stamp"
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Name, [string]$Status, [string]$Detail) { $results.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail }) }

# 1. File properties (Explorer "Details" tab, Task Manager): no developer prefix anywhere.
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Exe)
$fields = @($info.OriginalFilename, $info.InternalName, $info.FileDescription, $info.ProductName, $info.CompanyName, [IO.Path]::GetFileName($Exe))
$bad = $fields | Where-Object { $_ -and $_ -match '(?i)tyu' }
if ($bad) { Add-Result "file-properties" "Fail" ("contains 'tyu': " + ($bad -join ", ")) }
else { Add-Result "file-properties" "Pass" ("OriginalFilename=" + $info.OriginalFilename + "; Product=" + $info.ProductName + "; Version=" + $info.FileVersion) }

function Invoke-Smoke([string]$ExePath, [string[]]$ExtraArgs, [string]$Label) {
    $report = Join-Path $Work "$Label-smoke.json"
    $argList = @("--smoke", "--smoke-report", "`"$report`"") + $ExtraArgs
    if ($Offline) { $argList += "--smoke-offline" }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $ExePath -ArgumentList $argList -PassThru
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Add-Result "$Label" "Fail" "timed out after $TimeoutSeconds s (process killed)"
        return $null
    }
    $code = $p.ExitCode
    if (-not (Test-Path $report)) { Add-Result "$Label" "Fail" "exit code $code, no report written"; return $null }
    $json = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($c in $json.Checks) { Add-Result "$Label/$($c.Name)" $c.Status ("{0} ({1} ms)" -f $c.Detail, $c.Milliseconds) }
    foreach ($e in $json.UnhandledErrors) { Add-Result "$Label/unhandled" "Fail" $e }
    $status = if ($code -eq 0) { "Pass" } else { "Fail" }
    Add-Result "$Label" $status ("exit code $code in {0:0.0} s, {1} warnings" -f $sw.Elapsed.TotalSeconds, $json.Warnings)
    return $json
}

# 2. Self-check with an explicit data folder.
$data = Join-Path $Work "data"
Invoke-Smoke $Exe @("--data-dir", "`"$data`"") "smoke" | Out-Null

# 3. Portable layout: exe + portable.flag in a folder => data\ beside the exe, nothing in %LOCALAPPDATA%.
if ($Portable) {
    $pdir = Join-Path $Work "portable"
    New-Item -ItemType Directory -Force -Path $pdir | Out-Null
    $pexe = Join-Path $pdir ([IO.Path]::GetFileName($Exe))
    Copy-Item $Exe $pexe
    Set-Content -Path (Join-Path $pdir "portable.flag") -Value "" -Encoding ASCII
    Invoke-Smoke $pexe @() "portable" | Out-Null
    if (Test-Path (Join-Path $pdir "data\settings.json")) { Add-Result "portable/data-beside-exe" "Pass" (Join-Path $pdir "data") }
    else { Add-Result "portable/data-beside-exe" "Fail" "data\settings.json not created beside the exe" }
}

# 4. Leftover processes from this run (never touches other instances).
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$Work*" } | Stop-Process -Force -ErrorAction SilentlyContinue

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# 验收运行 $stamp")
[void]$md.AppendLine("")
[void]$md.AppendLine("- 程序：$Exe")
[void]$md.AppendLine("- 工作目录：$Work")
[void]$md.AppendLine("")
[void]$md.AppendLine("| 检查 | 结果 | 说明 |")
[void]$md.AppendLine("|---|---|---|")
foreach ($r in $results) { [void]$md.AppendLine("| $($r.Name) | $($r.Status) | $($r.Detail -replace '\|', '/') |") }
$mdPath = Join-Path $Work "acceptance.md"
[IO.File]::WriteAllText($mdPath, $md.ToString(), (New-Object System.Text.UTF8Encoding $true))
$results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
Write-Host "report: $mdPath"
$fails = @($results | Where-Object { $_.Status -eq "Fail" }).Count
exit ([int]($fails -gt 0))
