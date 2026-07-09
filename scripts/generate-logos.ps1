Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot "..\Clipy\Assets"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

function Save-Logo {
    param([int]$Size, [string]$Name)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $margin = [int]($Size * 0.08)
    $rect = New-Object System.Drawing.Rectangle $margin, $margin, ($Size - 2 * $margin), ($Size - 2 * $margin)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 200, 255, 77),
        [System.Drawing.Color]::FromArgb(255, 139, 124, 246),
        45)
    $g.FillEllipse($brush, $rect)
    $bmp.Save((Join-Path $dir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

Save-Logo -Size 44 -Name "Square44x44Logo.png"
Save-Logo -Size 150 -Name "Square150x150Logo.png"
