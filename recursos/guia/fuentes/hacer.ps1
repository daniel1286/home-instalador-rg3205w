# SPDX-FileCopyrightText: 2026 The Home Project
# SPDX-License-Identifier: Apache-2.0
#
# Rehace las imágenes de la guía (recursos\guia\*.png) a partir de las páginas de esta carpeta, con Microsoft Edge sin
# ventana. Miden 520 px, lo que mide la columna de la guía en la ventana del instalador, y se dibujan al doble (1040 px)
# para que se vean nítidas con escalado. Cada página se dibuja alta y luego se recorta el fondo que sobra por abajo.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File hacer.ps1   (Bypass vale solo para ese proceso)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$aqui = $PSScriptRoot
$salida = Split-Path $aqui -Parent
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'No se encuentra Microsoft Edge' }

$paginas = 'recovery_1', 'recovery_2', 'recovery_menu', 'driver_1', 'driver_2', 'driver_3', 'driver_4'
$fondo = [System.Drawing.Color]::FromArgb(0x1b, 0x1a, 0x18)
$perfil = Join-Path $env:TEMP 'guia_edge'

foreach ($p in $paginas) {
    $tmp = Join-Path $env:TEMP "guia_$p.png"
    $url = 'file:///' + ((Join-Path $aqui "$p.html") -replace '\\', '/')
    # Edge escribe avisos propios por la salida de errores: se ignoran, lo que vale es que salga la imagen
    # Con un perfil propio, para no chocar con el Edge que esté abierto
    Start-Process -FilePath $edge -Wait -WindowStyle Hidden -ArgumentList @('--headless=new', "`"--user-data-dir=$perfil`"",
        '--no-first-run', '--disable-gpu', '--hide-scrollbars',
        '--allow-file-access-from-files', '--force-device-scale-factor=2', '--window-size=520,900', "`"--screenshot=$tmp`"", $url)
    if (-not (Test-Path $tmp)) { throw "Edge no ha dibujado $p" }
    $img = [System.Drawing.Bitmap]::FromFile($tmp)
    try {
        # La última fila que no es solo fondo, más 14 px de margen (28 al doble)
        $fin = 0
        for ($y = $img.Height - 1; $y -ge 0 -and $fin -eq 0; $y--) {
            for ($x = 0; $x -lt $img.Width; $x += 3) {
                $c = $img.GetPixel($x, $y)
                if ([Math]::Abs($c.R - $fondo.R) + [Math]::Abs($c.G - $fondo.G) + [Math]::Abs($c.B - $fondo.B) -gt 6) { $fin = $y; break }
            }
        }
        $alto = [Math]::Min($img.Height, $fin + 1 + 28)
        $rect = New-Object System.Drawing.Rectangle 0, 0, $img.Width, $alto
        $corte = $img.Clone($rect, $img.PixelFormat)
        $corte.Save((Join-Path $salida "$p.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $corte.Dispose()
        "{0}.png  {1} x {2}" -f $p, $img.Width, $alto
    } finally { $img.Dispose(); Remove-Item $tmp -ErrorAction SilentlyContinue }
}
