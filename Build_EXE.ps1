$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'BandaNV.ps1'
$output = Join-Path $root 'BandaNV.exe'

Write-Host 'BandaNV v1.0 RC1.3.2 - Compilador' -ForegroundColor Cyan

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

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force -ErrorAction Stop
    }

    Invoke-ps2exe -inputFile $source -outputFile $output -noConsole -title 'BandaNV' -product 'BandaNV' -version '1.0.0.32' -description 'BandaNV - Organizador de archivos portable' -ErrorAction Stop

    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw 'PS2EXE terminó sin generar BandaNV.exe.'
    }

    Write-Host ''
    Write-Host 'LISTO: BandaNV.exe generado correctamente.' -ForegroundColor Green
    Write-Host "Ubicación: $output"
    Write-Host 'Conservá config\ y logs\ junto al ejecutable.'
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudo generar BandaNV.exe.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''
    Write-Host 'Nota: compilá BandaNV desde una carpeta donde tu usuario tenga permisos de escritura (por ejemplo Escritorio, Documentos o C:\BandaNV).' -ForegroundColor Yellow
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
