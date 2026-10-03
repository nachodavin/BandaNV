$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'BandaNV.slnx'

try {
    Write-Host ''
    Write-Host 'BandaNV v2.0 - Build de desarrollo' -ForegroundColor Cyan
    Write-Host ''

    if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'No se encontró dotnet. Instalá Visual Studio 2026 con .NET desktop development / Windows App SDK o el .NET 10 SDK.'
    }

    if(-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
        throw "No se encontró la solución: $solution"
    }

    Push-Location $root
    try {
        dotnet restore $solution
        if($LASTEXITCODE -ne 0){ throw 'dotnet restore falló.' }

        dotnet build $solution -c Debug -p:Platform=x64
        if($LASTEXITCODE -ne 0){ throw 'dotnet build falló.' }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    Write-Host 'LISTO: BandaNV v2.0 compiló correctamente.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudo compilar BandaNV v2.0.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
