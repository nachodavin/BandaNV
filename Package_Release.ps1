$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appSource = Join-Path $root 'BandaNV.ps1'
$appExe = Join-Path $root 'BandaNV.exe'
$updaterExe = Join-Path $root 'NVupdate.exe'
$readme = Join-Path $root 'LEEME.txt'
$dist = Join-Path $root 'dist'

try {
    foreach($required in @($appSource,$appExe,$updaterExe,$readme)) {
        if(-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Falta un archivo requerido para empaquetar la Release: $required"
        }
    }

    $sourceText = Get-Content -LiteralPath $appSource -Raw -Encoding UTF8
    $match = [regex]::Match($sourceText, "\$script:AppVersion\s*=\s*'([^']+)'")
    if(-not $match.Success) {
        throw 'No se pudo detectar la versión desde BandaNV.ps1.'
    }

    $version = $match.Groups[1].Value.Trim()
    if([string]::IsNullOrWhiteSpace($version)) {
        throw 'La versión detectada está vacía.'
    }

    $versionToken = 'v' + $version.Replace(' ','_')
    $portableName = 'BandaNV_' + $versionToken + '_Portable.zip'
    $autoUpdateName = 'BandaNV_' + $versionToken + '_AutoUpdate.zip'

    if(Test-Path -LiteralPath $dist) {
        Remove-Item -LiteralPath $dist -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    $portableStage = Join-Path $dist '_portable'
    $autoStage = Join-Path $dist '_autoupdate'
    New-Item -ItemType Directory -Force -Path $portableStage,$autoStage | Out-Null

    Copy-Item -LiteralPath $appExe -Destination (Join-Path $portableStage 'BandaNV.exe') -Force
    Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $portableStage 'NVupdate.exe') -Force
    Copy-Item -LiteralPath $readme -Destination (Join-Path $portableStage 'LEEME.txt') -Force

    Copy-Item -LiteralPath $appExe -Destination (Join-Path $autoStage 'BandaNV.exe') -Force

    $portableZip = Join-Path $dist $portableName
    $autoUpdateZip = Join-Path $dist $autoUpdateName

    Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force
    Compress-Archive -Path (Join-Path $autoStage '*') -DestinationPath $autoUpdateZip -CompressionLevel Optimal -Force

    Remove-Item -LiteralPath $portableStage,$autoStage -Recurse -Force

    if(-not (Test-Path -LiteralPath $portableZip -PathType Leaf)) {
        throw 'No se generó Portable.zip.'
    }
    if(-not (Test-Path -LiteralPath $autoUpdateZip -PathType Leaf)) {
        throw 'No se generó AutoUpdate.zip.'
    }

    Write-Host ''
    Write-Host 'LISTO: paquetes de Release generados correctamente.' -ForegroundColor Green
    Write-Host "Portable:   $portableZip"
    Write-Host "AutoUpdate: $autoUpdateZip"
    Write-Host ''
    Write-Host 'Contenido Portable:' -ForegroundColor Cyan
    Write-Host '  BandaNV.exe'
    Write-Host '  NVupdate.exe'
    Write-Host '  LEEME.txt'
    Write-Host ''
    Write-Host 'Contenido AutoUpdate:' -ForegroundColor Cyan
    Write-Host '  BandaNV.exe'
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudieron generar los paquetes de Release.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
