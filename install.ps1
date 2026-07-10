param(
    [switch]$RegisterAutostart
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "Clipy\Clipy.csproj"

Write-Host "Building Clipy (WinUI)..."
& (Join-Path $root "generate-icons.ps1")
dotnet publish $proj -c Release -r win-x64 --self-contained true -o (Join-Path $root "publish")

$exe = Join-Path $root "publish\Clipy.exe"
if (-not (Test-Path $exe)) { throw "Build failed: $exe not found" }

if ($RegisterAutostart) {
    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $regPath -Name "Clipy" -Value "`"$exe`""
    Write-Host "Autostart registered"
}

Write-Host "Done: $exe"
Start-Process $exe
