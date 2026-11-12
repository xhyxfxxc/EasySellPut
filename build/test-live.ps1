# Runs the live (network) smoke tests against the real data sources.
param([string]$Filter = 'Category=Live')
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\env.ps1') | Out-Null
$env:MEILI_LIVE = '1'
dotnet test (Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\Meili.Data.Tests') --filter $Filter -nologo -v q