# Re-create the local, project-scoped toolchain under C:\tyuStockApp\tools (no admin needed).
#   powershell -ExecutionPolicy Bypass -File tools\setup-toolchain.ps1
$ErrorActionPreference = 'Stop'
$tools = $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
if (-not (Test-Path "$tools\dotnet-install.ps1")) {
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$tools\dotnet-install.ps1" -UseBasicParsing
}
# .NET 10 LTS (primary). .NET 8 reached end of support on 2026-11-10 and is kept only for reference.
& "$tools\dotnet-install.ps1" -Channel 10.0 -InstallDir "$tools\dotnet" -NoPath
# Inno Setup 6 (per-user, into tools\InnoSetup)
if (-not (Test-Path "$tools\InnoSetup\ISCC.exe")) {
  New-Item -ItemType Directory -Force "$tools\_downloads" | Out-Null
  $exe = "$tools\_downloads\innosetup-6.7.3.exe"
  Invoke-WebRequest https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe -OutFile $exe -UseBasicParsing
  Start-Process $exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',"/DIR=$tools\InnoSetup",'/NOICONS' -Wait
}
$isl = "$tools\InnoSetup\Languages\ChineseSimplified.isl"
if (-not (Test-Path $isl)) {
  Invoke-WebRequest https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl -OutFile $isl -UseBasicParsing
}
. "$tools\env.ps1"
dotnet --list-sdks
