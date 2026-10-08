$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
} catch {}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root 'src\BandaNV.App\BandaNV.App.csproj'
$updaterProject = Join-Path $root 'src\BandaNV.Updater\BandaNV.Updater.csproj'
$smokeProject = Join-Path $root 'tests\BandaNV.Core.SmokeTests\BandaNV.Core.SmokeTests.csproj'
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

    foreach($required in @($appProject,$updaterProject,$smokeProject,$versionSource)) {
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

    # Version y Tag deben coincidir en cada Release estable.
    $tagMatch = [regex]::Match(
        $versionRaw,
        'public const string Tag = "v(?<tagVersion>\d+\.\d+(?:\.\d+)?)";'
    )
    if(-not $tagMatch.Success -or
       $tagMatch.Groups['tagVersion'].Value -ne $version) {
        throw "AppVersionInfo.Tag no coincide con AppVersionInfo.Version ($version)."
    }

    # Evita mezclar un tag nuevo con un binario que conserva metadatos viejos.
    $appProjectRaw = Get-Content -LiteralPath $appProject -Raw -Encoding UTF8
    if($appProjectRaw -notmatch [regex]::Escape("<Version>$version</Version>") -or
       $appProjectRaw -notmatch [regex]::Escape("<InformationalVersion>$version</InformationalVersion>")) {
        throw "Los metadatos del proyecto BandaNV.App no coinciden con $tag."
    }

    # Evita distribuir accidentalmente código de la build E2E.
    $productionSources = @(
        $appProject,
        (Join-Path $root 'src\BandaNV.Core\BandaNV.Core.csproj'),
        (Join-Path $root 'src\BandaNV.Core\Services\UpdateService.cs'),
        (Join-Path $root 'src\BandaNV.App\App.xaml.cs'),
        (Join-Path $root 'src\BandaNV.App\Program.cs'),
        (Join-Path $root 'src\BandaNV.App\MainWindow.xaml.cs')
    )
    foreach($source in $productionSources) {
        if(Select-String -LiteralPath $source -Pattern 'BANDANV_UPDATER_E2E_|UseE2EPrereleaseChannel|--test-updates' -Quiet) {
            throw "Se detectó código E2E no retirado en: $source"
        }
    }

    Write-Host 'Ejecutando smoke tests oficiales del motor...' -ForegroundColor DarkCyan
    & dotnet run --project $smokeProject -c Release
    if($LASTEXITCODE -ne 0) {
        throw 'Los smoke tests del motor fallaron: se canceló el paquete oficial.'
    }

    $packageName = "BandaNV_${tag}.zip"

    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    $work = Join-Path $dist '_work'

    if(Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }

    $existingPackage = Join-Path $dist $packageName
    if(Test-Path -LiteralPath $existingPackage -PathType Leaf) {
        Remove-Item -LiteralPath $existingPackage -Force
    }
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
BandaNV consulta GitHub Releases para comprobar si existe una versión estable más reciente.
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

    # Revisamos el ZIP final, no sólo los archivos de staging,
    # antes de declararlo listo para publicar.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name })
        $entryPaths = @($entries | ForEach-Object { $_.FullName.Replace('\','/') })
        if($entryPaths -notcontains 'BandaNV/BandaNV.exe' -or
           $entryPaths -notcontains 'BandaNV/NVupdate.exe' -or
           $entryPaths -notcontains 'BandaNV/bandanv_update_manifest.json') {
            throw 'El ZIP final no contiene los ejecutables y manifest obligatorios.'
        }

        foreach($relative in $entryPaths) {
            if($relative -match '^BandaNV/(config|logs|history)(/|$)' -or
               $relative -match '(^|/)(TESTER|UPDATER_E2E|Prepare_Updater_E2E|Start_Updater_E2E)') {
                throw "El ZIP oficial incluye un recurso temporal o portable: $relative"
            }
        }

        # ZipFile.GetEntry requiere una coincidencia textual exacta.
        # Compress-Archive en Windows puede crear entradas con '\' en
        # FullName. Buscamos sobre rutas normalizadas para ambos casos.
        $manifestEntry = $entries |
            Where-Object {
                $_.FullName.Replace('\','/') -eq 'BandaNV/bandanv_update_manifest.json'
            } |
            Select-Object -First 1

        if($null -eq $manifestEntry) {
            throw 'El ZIP no contiene una entrada legible para bandanv_update_manifest.json.'
        }

        $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
        try {
            $packedManifest = $reader.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
        }
        finally {
            $reader.Dispose()
        }
        if($null -eq $packedManifest -or
           $packedManifest.Format -ne 'BandaNV.UpdateManifest.v1' -or
           $packedManifest.Version -ne $tag) {
            throw "El manifest interno del ZIP no corresponde a BandaNV $tag."
        }

        $expectedFiles = @(
            $packedManifest.Files |
                ForEach-Object { 'BandaNV/' + $_.Replace('\','/') }
        )
        if(@($expectedFiles).Count -eq 0 -or
           @($expectedFiles | Where-Object { $entryPaths -notcontains $_ }).Count -gt 0 -or
           @($entryPaths | Where-Object {
               $_ -like 'BandaNV/*' -and $expectedFiles -notcontains $_
           }).Count -gt 0) {
            throw 'Los archivos del ZIP no coinciden con el manifest de actualización.'
        }
    }
    finally {
        $zip.Dispose()
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
    if($_.InvocationInfo -and $_.InvocationInfo.ScriptLineNumber) {
        Write-Host ("Línea del script: " + $_.InvocationInfo.ScriptLineNumber) -ForegroundColor Yellow
        Write-Host $_.InvocationInfo.Line.Trim() -ForegroundColor Yellow
    }
    exit 1
}
finally {
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
