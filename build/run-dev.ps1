# Run the app with its data folder kept inside the repo (scratch\appdata).
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\TyuStock.App\bin\Debug\net10.0-windows10.0.19041.0\TyuStockApp.exe'
Start-Process $exe -ArgumentList '--data-dir', (Join-Path $root 'scratch\appdata')
