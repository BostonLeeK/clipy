param(
    [string]$Version,
    [switch]$SkipPublish,
    [switch]$PortableOnly,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "Clipy\Clipy.csproj"
$publish = Join-Path $root "publish"
$dist = Join-Path $root "dist"
$installerDir = Join-Path $root "installer"
$iss = Join-Path $installerDir "Clipy.iss"
$versionFile = Join-Path $root "version.txt"

function Ensure-InstallerIcon {
    $iconPath = Join-Path $installerDir "clipy.ico"
    if (Test-Path $iconPath) { return $iconPath }

    Add-Type -AssemblyName System.Drawing
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $rect = New-Object System.Drawing.Rectangle 20, 20, 216, 216
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 200, 255, 77),
        [System.Drawing.Color]::FromArgb(255, 139, 124, 246),
        45)
    $g.FillEllipse($brush, $rect)
    $g.FillEllipse(
        (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 255, 255, 255))),
        70, 70, 50, 50)
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    $stream = [System.IO.File]::Create($iconPath)
    $icon.Save($stream)
    $stream.Close()
    $g.Dispose()
    $bmp.Dispose()
    return $iconPath
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-Path $versionFile)) { throw "version.txt not found" }
    $Version = (Get-Content $versionFile -Raw).Trim()
}

if (-not $SkipPublish) {
    Write-Host "Publishing Clipy $Version..."
    dotnet publish $proj -c Release -r win-x64 --self-contained true -o $publish
}

$exe = Join-Path $publish "Clipy.exe"
if (-not (Test-Path $exe)) { throw "Build failed: $exe not found" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$portableZip = Join-Path $dist "Clipy-$Version-win-x64-portable.zip"
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }

$staging = Join-Path $env:TEMP "clipy-portable-$Version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Write-Host "Staging portable files..."
Get-Process -Name Clipy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
Copy-Item -Path (Join-Path $publish "*") -Destination $staging -Recurse -Force

Write-Host "Creating portable zip..."
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $portableZip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Portable: $portableZip"

if ($PortableOnly) {
    if ($Launch) { Start-Process $exe }
    exit 0
}

function Find-InnoCompiler {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

$iscc = Find-InnoCompiler
if (-not $iscc) {
    Write-Host "Inno Setup not found. Trying winget install..."
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
        $iscc = Find-InnoCompiler
    }
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup is required for Setup.exe."
    Write-Host "Install: winget install JRSoftware.InnoSetup"
    Write-Host "Or publish only the portable zip: .\build-installer.ps1 -PortableOnly"
    Write-Host ""
    exit 1
}

Write-Host "Building installer with Inno Setup..."
$publishAbs = (Resolve-Path $publish).Path
$distAbs = (Resolve-Path $dist).Path
$installerAbs = (Resolve-Path $installerDir).Path
$iconPath = Ensure-InstallerIcon
& $iscc $iss "/DAppVersion=$Version" "/DPublishDir=$publishAbs" "/DOutputDir=$distAbs" "/DSourcePath=$installerAbs" "/DIconFile=$(Split-Path -Leaf $iconPath)"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

$setup = Join-Path $dist "Clipy-Setup-$Version-x64.exe"
if (-not (Test-Path $setup)) { throw "Installer not found: $setup" }

Write-Host ""
Write-Host "Done:"
Write-Host "  Setup:    $setup"
Write-Host "  Portable: $portableZip"
Write-Host ""

if ($Launch) { Start-Process $setup }
