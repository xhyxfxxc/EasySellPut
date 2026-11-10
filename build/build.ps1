# Build (and optionally test) the solution with the project-local SDK.
#   powershell -ExecutionPolicy Bypass -File build\build.ps1 [-Test] [-Live] [-Configuration Release]
param([switch]$Test, [switch]$Live, [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'tools\env.ps1')
Push-Location $root
try {
  dotnet build TyuStockApp.slnx -c $Configuration -nologo -v q -clp:ErrorsOnly
  if ($LASTEXITCODE -ne 0) { throw "build failed" }
  if ($Test) {
    $filter = if ($Live) { '' } else { '--filter', 'Category!=Live' }
    dotnet test TyuStockApp.slnx -c $Configuration --no-build -nologo @filter
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
  }
} finally { Pop-Location }
