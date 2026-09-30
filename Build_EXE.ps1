$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$source = Join-Path $root 'BandaNV.ps1'
$output = Join-Path $root 'BandaNV.exe'
$updaterSource = Join-Path $root 'NVupdate.ps1'
$updaterOutput = Join-Path $root 'NVupdate.exe'
$icon = Join-Path $root 'assets\BandaNV.ico'

Write-Host 'BandaNV v1.0 - Compilador' -ForegroundColor Cyan

try {
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction SilentlyContinue

    if (-not (Get-Module -ListAvailable -Name ps2exe)) {
        Write-Host 'Instalando módulo ps2exe para el usuario actual...'
        Install-Module ps2exe -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
    }

    Import-Module ps2exe -ErrorAction Stop

    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "No se encontró el código fuente: $source"
    }
    if (-not (Test-Path -LiteralPath $updaterSource -PathType Leaf)) {
        throw "No se encontró el código fuente de NVupdate: $updaterSource"
    }
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
        throw "No se encontró el icono de BandaNV: $icon"
    }

    foreach($target in @($output,$updaterOutput)) {
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Force -ErrorAction Stop
        }
    }

    Write-Host 'Compilando BandaNV.exe...'
    Invoke-ps2exe -inputFile $source -outputFile $output -noConsole -iconFile $icon -title 'BandaNV' -product 'BandaNV' -version '1.0.0.100' -description 'BandaNV - Organizador de archivos portable' -ErrorAction Stop

    Write-Host 'Compilando NVupdate.exe...'
    Invoke-ps2exe -inputFile $updaterSource -outputFile $updaterOutput -noConsole -iconFile $icon -title 'NVupdate' -product 'BandaNV' -version '1.0.0.100' -description 'NVupdate - Actualizador seguro de BandaNV' -ErrorAction Stop

    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw 'PS2EXE terminó sin generar BandaNV.exe.'
    }
    if (-not (Test-Path -LiteralPath $updaterOutput -PathType Leaf)) {
        throw 'PS2EXE terminó sin generar NVupdate.exe.'
    }

    Write-Host ''
    Write-Host 'LISTO: BandaNV.exe y NVupdate.exe generados correctamente.' -ForegroundColor Green
    Write-Host "BandaNV: $output"
    Write-Host "NVupdate: $updaterOutput"
    Write-Host 'Conservá config\ y logs\ junto a los ejecutables.'
    Write-Host ''
    Write-Host 'Para generar el ZIP de Release ejecutá:' -ForegroundColor Cyan
    Write-Host 'powershell.exe -ExecutionPolicy Bypass -File ".\Package_Release.ps1"'
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudieron generar los ejecutables de BandaNV.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''
    Write-Host 'Nota: compilá BandaNV desde una carpeta donde tu usuario tenga permisos de escritura (por ejemplo Escritorio, Documentos o C:\BandaNV).' -ForegroundColor Yellow
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
