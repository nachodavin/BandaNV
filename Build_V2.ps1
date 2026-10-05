$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
} catch {}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root 'src\BandaNV.App\BandaNV.App.csproj'
$updaterProject = Join-Path $root 'src\BandaNV.Updater\BandaNV.Updater.csproj'

try {
    Write-Host ''
    Write-Host 'BandaNV v2.0.1 - Build de desarrollo' -ForegroundColor Cyan
    Write-Host ''

    if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'No se encontró dotnet. Instalá Visual Studio 2026 con WinUI application development o el .NET 10 SDK.'
    }

    if(-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
        throw "No se encontró el proyecto: $appProject"
    }

    $running = @(
        Get-Process -Name 'BandaNV' -ErrorAction SilentlyContinue
        Get-Process -Name 'BandaNV.App' -ErrorAction SilentlyContinue
    )
    if($running.Count -gt 0) {
        $ids = ($running | ForEach-Object { $_.Id }) -join ', '
        throw "BandaNV v2.0.1 está abierto (PID: $ids). Cerralo antes de compilar para que Windows pueda reemplazar sus binarios."
    }

    if(-not (Test-Path -LiteralPath $updaterProject -PathType Leaf)) {
        throw "No se encontró el proyecto del updater: $updaterProject"
    }

    Push-Location $root
    try {
        # Compilamos el proyecto de la app directamente.
        # BandaNV.Core se compila automáticamente por ProjectReference.
        # Esto evita forzar una configuración Debug|x64 inexistente a nivel solución.
        dotnet restore $appProject
        if($LASTEXITCODE -ne 0){ throw 'dotnet restore de BandaNV falló.' }

        dotnet restore $updaterProject
        if($LASTEXITCODE -ne 0){ throw 'dotnet restore de NVupdate falló.' }

        dotnet build $appProject -c Debug --no-restore
        if($LASTEXITCODE -ne 0){ throw 'dotnet build de BandaNV falló.' }

        dotnet build $updaterProject -c Debug --no-restore
        if($LASTEXITCODE -ne 0){ throw 'dotnet build de NVupdate falló.' }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    Write-Host 'LISTO: BandaNV v2.0.1 compiló correctamente.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudo compilar BandaNV v2.0.1.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
