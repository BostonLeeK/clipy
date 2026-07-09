$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "publish\Clipy.exe"

if (-not (Test-Path $exe)) {
    Write-Host "Clipy.exe not found. Run install.ps1 first."
    & (Join-Path $root "install.ps1")
    exit $LASTEXITCODE
}

Get-Process -Name Clipy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Start-Process $exe
Write-Host "Started: $exe"
