# Dot-source this: . C:\tyuStockApp\tools\env.ps1
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $root 'tools\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $root 'tools\.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root 'tools\nuget-packages'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
