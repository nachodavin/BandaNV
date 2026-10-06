$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
} catch {}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$testProject = Join-Path $root 'tests\BandaNV.Core.SmokeTests\BandaNV.Core.SmokeTests.csproj'

try {
    Write-Host ''
    Write-Host 'BandaNV v2.0 - Tests del motor' -ForegroundColor Cyan
    Write-Host ''

    if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'No se encontró dotnet. Instalá el .NET 10 SDK.'
    }

    if(-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
        throw "No se encontró el proyecto de pruebas: $testProject"
    }

    Push-Location $root
    try {
        dotnet restore $testProject
        if($LASTEXITCODE -ne 0) {
            throw 'dotnet restore de las pruebas falló.'
        }

        dotnet run --project $testProject -c Debug --no-restore
        if($LASTEXITCODE -ne 0) {
            throw 'Uno o más tests del motor fallaron.'
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    Write-Host 'LISTO: todos los tests del motor pasaron.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'ERROR: los tests del motor no terminaron correctamente.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
