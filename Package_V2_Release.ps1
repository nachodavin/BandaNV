$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
} catch {}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root 'src\BandaNV.App\BandaNV.App.csproj'
$updaterProject = Join-Path $root 'src\BandaNV.Updater\BandaNV.Updater.csproj'
$versionSource = Join-Path $root 'src\BandaNV.Core\Infrastructure\AppVersionInfo.cs'
$dist = Join-Path $root 'dist-v2'

function Get-RelativeUnixPath([string]$base,[string]$path) {
    $baseFull = [IO.Path]::GetFullPath($base).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $pathFull = [IO.Path]::GetFullPath($path)

    $baseUri = New-Object System.Uri($baseFull)
    $pathUri = New-Object System.Uri($pathFull)

    $relative = [Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($pathUri).ToString()
    )

    return $relative.Replace('\','/')
}

try {
    Write-Host ''
    Write-Host 'BandaNV v2 - Paquete oficial de Release' -ForegroundColor Cyan
    Write-Host ''

    foreach($required in @($appProject,$updaterProject,$versionSource)) {
        if(-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Falta un archivo requerido: $required"
        }
    }

    if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'No se encontró dotnet.'
    }

    $versionRaw = Get-Content -LiteralPath $versionSource -Raw -Encoding UTF8

    if($versionRaw -notmatch 'public const string Version = "(?<version>\d+\.\d+(?:\.\d+)?)";') {
        throw 'No se pudo detectar AppVersionInfo.Version.'
    }

    $version = [string]$Matches['version']
    $tag = 'v' + $version
    $packageName = "BandaNV_${tag}.zip"

    if(Test-Path -LiteralPath $dist) {
        Remove-Item -LiteralPath $dist -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    $work = Join-Path $dist '_work'
    $appPublish = Join-Path $work 'app-publish'
    $updaterPublish = Join-Path $work 'updater-publish'
    $stage = Join-Path $work 'release'
    $stageApp = Join-Path $stage 'BandaNV'

    New-Item -ItemType Directory -Force -Path $appPublish,$updaterPublish,$stageApp | Out-Null

    Push-Location $root
    try {
        Write-Host 'Publicando BandaNV...' -ForegroundColor DarkCyan
        dotnet publish $appProject -c Release -r win-x64 --self-contained true -o $appPublish
        if($LASTEXITCODE -ne 0) {
            throw 'dotnet publish de BandaNV falló.'
        }

        Write-Host 'Publicando NVupdate...' -ForegroundColor DarkCyan
        dotnet publish $updaterProject -c Release -r win-x64 --self-contained true -o $updaterPublish
        if($LASTEXITCODE -ne 0) {
            throw 'dotnet publish de NVupdate falló.'
        }
    }
    finally {
        Pop-Location
    }

    $appExe = Join-Path $appPublish 'BandaNV.exe'
    $updaterExe = Join-Path $updaterPublish 'NVupdate.exe'

    if(-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
        throw "La publicación no generó BandaNV.exe: $appExe"
    }

    if(-not (Test-Path -LiteralPath $updaterExe -PathType Leaf)) {
        throw "La publicación no generó NVupdate.exe: $updaterExe"
    }

    Copy-Item -Path (Join-Path $appPublish '*') -Destination $stageApp -Recurse -Force

    Get-ChildItem -LiteralPath $stageApp -Filter '*.pdb' -File -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $stageApp 'NVupdate.exe') -Force

    $manifestName = 'bandanv_update_manifest.json'
    $manifestPath = Join-Path $stageApp $manifestName

    $managedFiles = @(
        Get-ChildItem -LiteralPath $stageApp -File -Recurse |
            Where-Object { $_.FullName -ne $manifestPath } |
            ForEach-Object { Get-RelativeUnixPath $stageApp $_.FullName }
    )

    $managedFiles += $manifestName
    $managedFiles = @($managedFiles | Sort-Object -Unique)

    foreach($relative in $managedFiles) {
        $first = ($relative -split '/')[0]
        if($first -in @('config','logs','history')) {
            throw "El paquete intentó incluir una carpeta portable protegida: $relative"
        }
    }

    $manifest = [ordered]@{
        Format = 'BandaNV.UpdateManifest.v1'
        Version = $tag
        Files = $managedFiles
    }

    $manifest |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8

    $readme = @"
BandaNV $tag

Aplicación portable para Windows x64.

Instalación manual:
1. Extraé la carpeta BandaNV donde quieras.
2. Evitá carpetas protegidas como Program Files.
3. Ejecutá BandaNV.exe.

Actualizaciones:
BandaNV consulta GitHub Releases cuando la opción de actualización automática está activa.
El paquete se verifica mediante SHA-256 antes de instalarse.
NVupdate realiza backup y rollback automático si la nueva versión no confirma un inicio correcto.

Los datos del usuario permanecen junto a la aplicación en:
- config\
- logs\
- history\

Esas carpetas no forman parte del paquete administrado por el updater.
"@

    $readme |
        Set-Content -LiteralPath (Join-Path $stage 'LEEME IMPORTANTE.txt') -Encoding UTF8

    $packagePath = Join-Path $dist $packageName

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $packagePath -CompressionLevel Optimal -Force

    if(-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw 'No se generó el ZIP de Release.'
    }

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

    Remove-Item -LiteralPath $work -Recurse -Force

    Write-Host ''
    Write-Host 'LISTO: paquete oficial generado.' -ForegroundColor Green
    Write-Host "Versión:  $tag"
    Write-Host "Paquete:  $packagePath"
    Write-Host "SHA-256:  $hash"
    Write-Host ''
    Write-Host 'Subí este ZIP como asset de una GitHub Release con tag:' -ForegroundColor Cyan
    Write-Host "  $tag"
    Write-Host ''
    Write-Host 'El nombre del asset debe conservarse exactamente:' -ForegroundColor Cyan
    Write-Host "  $packageName"
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
