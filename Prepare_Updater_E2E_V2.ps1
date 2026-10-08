$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
} catch {}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root 'src\BandaNV.App\BandaNV.App.csproj'
$updaterProject = Join-Path $root 'src\BandaNV.Updater\BandaNV.Updater.csproj'
$dist = Join-Path $root 'dist-updater-e2e'
$work = Join-Path $dist '_work'
$testerInstall = Join-Path $dist 'TesterInstall'
$utf8 = [Text.UTF8Encoding]::new($false)

function Write-Utf8([string]$path, [string]$contents) {
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($path, $contents, $script:utf8)
}

function Get-RelativeUnixPath([string]$base, [string]$path) {
    return [IO.Path]::GetRelativePath(
        [IO.Path]::GetFullPath($base),
        [IO.Path]::GetFullPath($path)
    ).Replace('\', '/')
}

function Write-Package(
    [string]$publishedApp,
    [string]$publishedUpdater,
    [string]$tag,
    [string]$archiveName,
    [string]$scenario
) {
    $packageRoot = Join-Path $script:work ('package-' + $scenario)
    $stageApp = Join-Path $packageRoot 'BandaNV'
    New-Item -ItemType Directory -Force -Path $stageApp | Out-Null

    Copy-Item -Path (Join-Path $publishedApp '*') -Destination $stageApp -Recurse -Force
    Get-ChildItem -LiteralPath $stageApp -Filter '*.pdb' -File -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath (Join-Path $publishedUpdater 'NVupdate.exe') -Destination (Join-Path $stageApp 'NVupdate.exe') -Force

    foreach ($required in @('BandaNV.exe','NVupdate.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stageApp $required) -PathType Leaf)) {
            throw "El paquete de prueba no contiene $required"
        }
    }

    $manifestName = 'bandanv_update_manifest.json'
    $files = @(
        Get-ChildItem -LiteralPath $stageApp -File -Recurse |
            ForEach-Object { Get-RelativeUnixPath $stageApp $_.FullName }
    )
    $files += $manifestName
    $files = @($files | Sort-Object -Unique)

    foreach ($relative in $files) {
        $firstSegment = ($relative -split '/')[0]
        if ($firstSegment -in @('config','logs','history')) {
            throw "El paquete de prueba incluye datos portables: $relative"
        }
    }

    $manifest = [ordered]@{
        Format = 'BandaNV.UpdateManifest.v1'
        Version = $tag
        Files = $files
    }
    Write-Utf8 (Join-Path $stageApp $manifestName) ($manifest | ConvertTo-Json -Depth 5)

    Write-Utf8 (Join-Path $packageRoot 'PRUEBA E2E - LEEME.txt') (
        @(
            "BandaNV $tag - SOLO PRUEBA DE ACTUALIZACION",
            "No distribuir como Release oficial.",
            "No contiene config, logs ni history.",
            "La build destino debe publicarse como prerelease v2.0.1."
        ) -join [Environment]::NewLine
    )

    $zipPath = Join-Path $script:dist $archiveName
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "No pudo generarse el paquete $archiveName"
    }

    $sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ''
    Write-Host "Generado: $zipPath" -ForegroundColor Green
    Write-Host "SHA-256:  $sha"

    return $zipPath
}

try {
    Write-Host ''
    Write-Host 'BandaNV v2.0 - Preparar prueba REAL del updater' -ForegroundColor Cyan
    Write-Host 'No crea ni modifica ninguna GitHub Release.' -ForegroundColor DarkGray
    Write-Host ''

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'No se encontró .NET SDK (dotnet).'
    }

    if (-not (Test-Path -LiteralPath $appProject -PathType Leaf) -or
        -not (Test-Path -LiteralPath $updaterProject -PathType Leaf)) {
        throw 'No se encontró el código fuente de BandaNV o NVupdate.'
    }

    if ((Get-Process -Name BandaNV -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) {
        throw 'Cerrá primero todas las ventanas de BandaNV antes de generar el tester.'
    }

    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }

    $updaterPublish = Join-Path $work 'updater'
    $basePublish = Join-Path $work 'base-publish'
    $targetPublish = Join-Path $work 'target-publish'

    Write-Host '1/3 Publicando NVupdate real...' -ForegroundColor DarkCyan
    & dotnet publish $updaterProject -c Release -r win-x64 --self-contained true -o $updaterPublish
    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de NVupdate.' }

    Write-Host '2/3 Publicando TESTER v2.0 con canal opt-in...' -ForegroundColor DarkCyan
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:BandaNVUpdaterE2EBase=true -o $basePublish
    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación del tester v2.0.' }

    $baseZip = Write-Package $basePublish $updaterPublish 'v2.0' 'BandaNV_UpdaterTest_Base_v2.0.zip' 'base'

    Write-Host '3/3 Publicando prerelease v2.0.1 con identidad de ensayo...' -ForegroundColor DarkCyan
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:BandaNVUpdaterE2ETarget=true -o $targetPublish
    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de la prerelease v2.0.1.' }

    $targetZip = Write-Package $targetPublish $updaterPublish 'v2.0.1' 'BandaNV_v2.0.1.zip' 'target'

    # Nunca sobreescribir una instalación de test ya existente:
    # podría contener preferencias o logs que el usuario quiere verificar.
    if (-not (Test-Path -LiteralPath $testerInstall)) {
        Expand-Archive -LiteralPath $baseZip -DestinationPath $testerInstall

        if (-not (Test-Path -LiteralPath (Join-Path $testerInstall 'BandaNV\BandaNV.exe') -PathType Leaf)) {
            throw 'La extracción del tester no dejó BandaNV.exe disponible.'
        }

        Write-Host ''
        Write-Host "Tester aislado preparado: $testerInstall" -ForegroundColor Green
    }
    else {
        Write-Host ''
        Write-Host 'Se conservó tu TesterInstall existente (no se reemplazó ningún archivo).' -ForegroundColor Yellow
        Write-Host 'Si necesitás una nueva instalación de ensayo, renombrá esa carpeta y ejecutá nuevamente el script.' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host 'LISTO: se generaron los dos paquetes, sin alterar la instalación oficial.' -ForegroundColor Green
    Write-Host 'IMPORTANTE: publicá solamente BandaNV_v2.0.1.zip como prerelease v2.0.1.' -ForegroundColor Yellow
    Write-Host 'NO publiques BandaNV_UpdaterTest_Base_v2.0.zip.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Luego ejecutá Start_Updater_E2E_V2.ps1 para probar la descarga desde GitHub.' -ForegroundColor Cyan
}
catch {
    Write-Host ''
    Write-Host 'ERROR: no se pudieron preparar los paquetes E2E.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path -LiteralPath $work) {
        try {
            Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Host 'Los temporales de compilación no pudieron limpiarse completamente.' -ForegroundColor Yellow
        }
    }

    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
