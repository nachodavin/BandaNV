$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$testerFolder = Join-Path $root 'dist-updater-e2e\TesterInstall\BandaNV'
$exe = Join-Path $testerFolder 'BandaNV.exe'

try {
    Write-Host ''
    Write-Host 'BandaNV - PRUEBA REAL DEL ACTUALIZADOR' -ForegroundColor Cyan
    Write-Host 'Se abrirá exclusivamente la instalación temporal TESTER v2.0.' -ForegroundColor DarkGray
    Write-Host ''

    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw 'No encontré el tester. Ejecutá primero Prepare_Updater_E2E_V2.ps1.'
    }

    Write-Host "Instalación de prueba: $testerFolder" -ForegroundColor DarkGray
    Write-Host 'La build de ensayo consulta la prerelease v2.0.1 únicamente por el flag --test-updates.' -ForegroundColor Cyan

    Start-Process -FilePath $exe -WorkingDirectory $testerFolder -ArgumentList '--test-updates'

    Write-Host ''
    Write-Host 'Tester iniciado. No se modificó ni abrió tu instalación habitual.' -ForegroundColor Green
    Write-Host 'Esperá el popup de actualización; si no aparece, buscá actualizaciones en Configuración.' -ForegroundColor Yellow
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no pudo iniciarse la prueba.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
