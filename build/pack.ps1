#requires -Version 5.1
<#
.Synopsis
  Build tests, the portable self-contained exe, and the per-user Inno Setup installer into dist\.
.Notes
  Run after `. C:\tyuStockApp\tools\env.ps1`. Does not launch the installer UI or the app.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $env:DOTNET_ROOT) {
    . (Join-Path $Root "tools\env.ps1")
}

$ProductName = "美丽稻谷卖方绿泡群专用终端"
$ExeFileName = "$ProductName.exe"
$PortableLabel = "$ProductName-便携版"
$SetupLabel = "$ProductName-安装程序"
$Version = "0.3.0"

$PublishDir = Join-Path $Root "build\out\publish"
$PayloadDir = Join-Path $Root "build\out\payload"
$PortableDir = Join-Path $Root "dist\$PortableLabel"
$DistDir = Join-Path $Root "dist"
$Iscc = Join-Path $Root "tools\InnoSetup\ISCC.exe"
$AppProj = Join-Path $Root "src\TyuStock.App\TyuStock.App.csproj"
$Sln = Join-Path $Root "TyuStockApp.slnx"

function Write-Utf8([string]$Path, [string]$Content, [bool]$Bom = $true) {
    $enc = New-Object System.Text.UTF8Encoding $Bom
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

if (-not $SkipTests) {
    Write-Host "=== tests ==="
    dotnet test $Sln -c Release -nologo --verbosity minimal --filter "Category!=Live"
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)" }
}

Write-Host "=== publish self-contained win-x64 (single-file, compressed) ==="
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir, $PayloadDir, $DistDir | Out-Null

dotnet publish $AppProj -c Release -r win-x64 --self-contained true -o $PublishDir -nologo --verbosity minimal `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$published = Join-Path $PublishDir "MeiliTerminal.exe"
if (-not (Test-Path $published)) { throw "Published exe not found: $published" }

Get-ChildItem $PayloadDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
New-Item -ItemType Directory -Force -Path $PayloadDir | Out-Null
Copy-Item $published (Join-Path $PayloadDir $ExeFileName) -Force

# Verify user-visible file metadata contains no "tyu".
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PayloadDir $ExeFileName))
Write-Host "FileDescription=$($info.FileDescription)"
Write-Host "ProductName=$($info.ProductName)"
Write-Host "CompanyName=$($info.CompanyName)"
Write-Host "FileVersion=$($info.FileVersion)"
foreach ($label in @($info.FileDescription, $info.ProductName, $info.CompanyName, $ExeFileName)) {
    if ($label -match 'tyu') { throw "Visible metadata contains tyu: $label" }
}
if ($info.ProductName -ne $ProductName) { Write-Warning "ProductName is '$($info.ProductName)', expected '$ProductName'" }

# Portable folder: exe + portable.flag (data will be created beside the exe at first run).
if (Test-Path $PortableDir) { Remove-Item $PortableDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PortableDir | Out-Null
Copy-Item (Join-Path $PayloadDir $ExeFileName) (Join-Path $PortableDir $ExeFileName) -Force
Write-Utf8 (Join-Path $PortableDir "portable.flag") "" $false
$portableReadme = @"
这是「$ProductName」的便携版（$Version）。

使用方法：
1. 把本文件夹整份拷到任意位置（例如 U 盘或文档目录）。
2. 双击「$ExeFileName」即可运行，无需安装，也不需要管理员权限。
3. 设置、自选和提醒会保存在本文件夹里的 data\ 目录，不会写入系统安装位置。
4. 关闭主窗口会最小化到托盘。要退出程序，请右键点击托盘图标，选择「退出」。

请勿删除同目录下的 portable.flag，否则程序会改用当前用户的本地数据目录。
"@
Write-Utf8 (Join-Path $PortableDir "使用说明.txt") $portableReadme

$portableZip = Join-Path $DistDir "$PortableLabel.zip"
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
# Zip the folder contents so extracting yields the exe and flag at the top level of the named folder.
Push-Location $DistDir
try {
    Compress-Archive -Path $PortableLabel -DestinationPath $portableZip -CompressionLevel Optimal
} finally { Pop-Location }

# Also place a copy of the exe at dist root with the portable label, plus a sibling portable.flag,
# so a single-folder drop works. The flagged folder above is the recommended layout.
Copy-Item (Join-Path $PortableDir $ExeFileName) (Join-Path $DistDir "$PortableLabel.exe") -Force
# Do not put portable.flag in dist\ root (the installer sits there too). Use the subfolder / zip.

Write-Host "=== Inno Setup installer ==="
if (-not (Test-Path $Iscc)) { throw "ISCC.exe not found at $Iscc" }
& $Iscc (Join-Path $Root "build\installer\setup.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$readme = @"
美丽稻谷卖方绿泡群专用终端（版本 $Version）
================================

本目录有两种使用方式，请任选其一。都不必使用管理员权限。

1）安装包
    文件：${SetupLabel}.exe
    双击运行即可安装到当前用户目录（%LOCALAPPDATA%\Programs\美丽稻谷终端）。
    可选择是否创建桌面快捷方式。开始菜单和“应用和功能”里显示的名称都是「$ProductName」。
    卸载也可在系统设置的应用列表中完成。
    设置和自选数据保存在 %LOCALAPPDATA%\美丽稻谷终端，卸载时默认保留。

2）便携版（免安装）
    推荐：解压「${PortableLabel}.zip」，或直接打开「$PortableLabel」文件夹，双击「$ExeFileName」。
    也可单独复制「${PortableLabel}.exe」，但请同时带上 portable.flag，并放在同一目录，数据才会写到程序旁的 data\ 文件夹。
    没有 portable.flag 时，便携版 exe 会改用上面的当前用户数据目录。

使用提示
    关闭主窗口会最小化到托盘，并不会退出。要退出，请右键点击托盘图标，选择「退出」。
    本软件仅供个人参考，不构成投资建议。行情和期权数据来自第三方公开接口，可能延迟或不准确。

公司：美丽稻谷
"@
Write-Utf8 (Join-Path $DistDir "使用说明.txt") $readme

Write-Host "=== artifacts ==="
Get-ChildItem $DistDir -Recurse -File | Where-Object { $_.FullName -notmatch '\\data\\' } | ForEach-Object {
    "{0,12:N0}  {1}" -f $_.Length, $_.FullName.Replace($Root + '\', '')
} | Write-Host