$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tool = Join-Path $root "tools\IconGen\IconGen.csproj"

Write-Host "Generating Clipy icons..."
dotnet run --project $tool -c Release
Write-Host "Done."
