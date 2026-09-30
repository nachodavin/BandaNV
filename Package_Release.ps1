$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appSource = Join-Path $root 'BandaNV.ps1'
$appExe = Join-Path $root 'BandaNV.exe'
$updaterExe = Join-Path $root 'NVupdate.exe'
$readme = Join-Path $root 'LEEME IMPORTANTE.txt'
$dist = Join-Path $root 'dist'

try {
    foreach($required in @($appSource,$appExe,$updaterExe,$readme)) {
        if(-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Falta un archivo requerido para empaquetar la Release: $required"
        }
    }

    $versionLine = Get-Content -LiteralPath $appSource -Encoding UTF8 |
        Where-Object { $_.TrimStart().StartsWith('$script:AppVersion') } |
        Select-Object -First 1

    if([string]::IsNullOrWhiteSpace($versionLine)) {
        throw 'No se pudo detectar la versión desde BandaNV.ps1.'
    }

    $parts = $versionLine -split '=', 2
    if($parts.Count -ne 2) {
        throw 'La línea de versión de BandaNV.ps1 no tiene el formato esperado.'
    }

    $version = $parts[1].Trim().Trim("'").Trim('"')
    if([string]::IsNullOrWhiteSpace($version)) {
        throw 'La versión detectada está vacía.'
    }

    if($version -match '^(?<base>\d+\.\d+)\s+RC(?<rc>\d+)$') {
        $versionToken = 'v' + $Matches['base'] + '-rc' + $Matches['rc']
    } else {
        $versionToken = 'v' + $version.Replace(' ','-').ToLowerInvariant()
    }

    $packageName = 'BandaNV_' + $versionToken + '.zip'

    if(Test-Path -LiteralPath $dist) {
        Remove-Item -LiteralPath $dist -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    $stage = Join-Path $dist '_release'
    $appStage = Join-Path $stage 'BandaNV'
    New-Item -ItemType Directory -Force -Path $appStage | Out-Null

    Copy-Item -LiteralPath $appExe -Destination (Join-Path $appStage 'BandaNV.exe') -Force
    Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $appStage 'NVupdate.exe') -Force
    Copy-Item -LiteralPath $readme -Destination (Join-Path $stage 'LEEME IMPORTANTE.txt') -Force

    $packageZip = Join-Path $dist $packageName
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $packageZip -CompressionLevel Optimal -Force

    Remove-Item -LiteralPath $stage -Recurse -Force

    if(-not (Test-Path -LiteralPath $packageZip -PathType Leaf)) {
        throw 'No se generó el ZIP de Release.'
    }

    Write-Host ''
    Write-Host 'LISTO: paquete de Release generado correctamente.' -ForegroundColor Green
    Write-Host "Paquete: $packageZip"
    Write-Host ''
    Write-Host 'Contenido:' -ForegroundColor Cyan
    Write-Host '  BandaNV\'
    Write-Host '    BandaNV.exe'
    Write-Host '    NVupdate.exe'
    Write-Host '  LEEME IMPORTANTE.txt'
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudo generar el paquete de Release.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
