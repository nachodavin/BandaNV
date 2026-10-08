$ErrorActionPreference = 'Stop'
param(
    [switch]$KeepArtifacts
)

# Prueba aislada del motor de instalación/rollback de NVupdate.
# No usa ni modifica ninguna instalación real de BandaNV.
# Ejecutar con PowerShell en Windows y .NET 10 SDK instalado.

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$updaterProject = Join-Path $root 'src\BandaNV.Updater\BandaNV.Updater.csproj'
$tempBase = Join-Path ([IO.Path]::GetTempPath()) 'BandaNV'
$testRoot = Join-Path $tempBase ('updater-smoke-' + [guid]::NewGuid().ToString('N'))
$probeProject = Join-Path $testRoot 'probe\BandaNV.Probe.csproj'
$probeProgram = Join-Path $testRoot 'probe\Program.cs'
$probePublish = Join-Path $testRoot 'probe-publish'
$updaterPublish = Join-Path $testRoot 'updater-publish'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$passed = 0
$failed = 0

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Write-Utf8([string]$path, [string]$content) {
    $directory = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [IO.File]::WriteAllText($path, $content, $script:utf8)
}

function Save-Manifest([string]$folder, [string]$version, [string[]]$files) {
    $payload = [ordered]@{
        Format = 'BandaNV.UpdateManifest.v1'
        Version = $version
        Files = @($files)
    }
    Write-Utf8 (Join-Path $folder 'bandanv_update_manifest.json') (
        $payload | ConvertTo-Json -Depth 5
    )
}

function Create-Installation([string]$folder) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $script:probePublish 'BandaNV.exe') -Destination (Join-Path $folder 'BandaNV.exe')
    Copy-Item -LiteralPath (Join-Path $script:updaterPublish 'NVupdate.exe') -Destination (Join-Path $folder 'NVupdate.exe')
    Write-Utf8 (Join-Path $folder 'probe-release.txt') 'v2.0|ok'
    Write-Utf8 (Join-Path $folder 'old-managed.txt') 'Archivo gestionado por la versión anterior'
    Write-Utf8 (Join-Path $folder 'config\preferencias.txt') 'NO MODIFICAR CONFIG'
    Write-Utf8 (Join-Path $folder 'logs\actividad.txt') 'NO MODIFICAR LOG'
    Write-Utf8 (Join-Path $folder 'history\ejecucion.json') 'NO MODIFICAR HISTORIAL'
    Save-Manifest $folder 'v2.0' @(
        'BandaNV.exe',
        'NVupdate.exe',
        'probe-release.txt',
        'old-managed.txt',
        'bandanv_update_manifest.json'
    )
}

function Create-Stage(
    [string]$folder,
    [string]$mode,
    [string]$version
) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $script:probePublish 'BandaNV.exe') -Destination (Join-Path $folder 'BandaNV.exe')
    Copy-Item -LiteralPath (Join-Path $script:updaterPublish 'NVupdate.exe') -Destination (Join-Path $folder 'NVupdate.exe')
    Write-Utf8 (Join-Path $folder 'probe-release.txt') ($version + '|' + $mode)
    Write-Utf8 (Join-Path $folder 'new-managed.txt') 'Archivo gestionado por la nueva versión'
    Save-Manifest $folder $version @(
        'BandaNV.exe',
        'NVupdate.exe',
        'probe-release.txt',
        'new-managed.txt',
        'bandanv_update_manifest.json'
    )
}

function Quote-Argument([string]$value) {
    return '"' + $value.Replace('"', '\"') + '"'
}

function Invoke-UpdateScenario([string]$name, [string]$mode, [bool]$badManifest) {
    $case = Join-Path $script:testRoot $name
    $app = Join-Path $case 'installation'
    Create-Installation $app

    $token = [guid]::NewGuid().ToString('N')
    $workspace = Join-Path $script:tempBase ('update-' + $token)
    $staged = Join-Path $workspace 'extracted\BandaNV'
    $backup = Join-Path $workspace 'backup'
    $confirmation = Join-Path $workspace 'confirmed.json'
    $runner = Join-Path $script:updaterPublish 'NVupdate.exe'

    Create-Stage $staged $mode 'v2.0.1'

    if ($badManifest) {
        Write-Utf8 (Join-Path $staged 'bandanv_update_manifest.json') 'JSON INVALIDO'
    }

    New-Item -ItemType Directory -Path $backup -Force | Out-Null

    # Un PID muy alto no existente simula que la ventana de BandaNV ya cerró.
    # Se invoca exactamente el actualizador compilado de este repositorio.
    $arguments = @(
        '--parent-pid', '2147483000',
        '--app-dir', (Quote-Argument $app),
        '--staged-dir', (Quote-Argument $staged),
        '--backup-dir', (Quote-Argument $backup),
        '--confirm-path', (Quote-Argument $confirmation),
        '--token', $token,
        '--expected-version', 'v2.0.1',
        '--release-tag', 'v2.0.1'
    ) -join ' '

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $runner
    $psi.WorkingDirectory = $workspace
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false

    if ($mode -eq 'fail' -or $badManifest) {
        Write-Host '   NVupdate mostrará el aviso de error previsto. Cerralo con Aceptar para continuar.' -ForegroundColor Yellow
    }

    $process = [Diagnostics.Process]::Start($psi)
    Require ($null -ne $process) ('No pudo iniciarse NVupdate: ' + $name)
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $process.Dispose()

    Require ((Get-Content -LiteralPath (Join-Path $app 'config\preferencias.txt') -Raw).Trim() -eq 'NO MODIFICAR CONFIG') 'Se modificó la configuración portable.'
    Require ((Get-Content -LiteralPath (Join-Path $app 'logs\actividad.txt') -Raw).Trim() -eq 'NO MODIFICAR LOG') 'Se modificaron los logs.'
    Require ((Get-Content -LiteralPath (Join-Path $app 'history\ejecucion.json') -Raw).Trim() -eq 'NO MODIFICAR HISTORIAL') 'Se modificó el historial.'

    $versionFile = Join-Path $app 'probe-release.txt'
    $currentMode = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    $oldManaged = Test-Path -LiteralPath (Join-Path $app 'old-managed.txt')
    $newManaged = Test-Path -LiteralPath (Join-Path $app 'new-managed.txt')
    $statePath = Join-Path $app 'config\bandanv_update_state.json'

    if ($badManifest) {
        Require ($exitCode -eq 1) 'Un manifest inválido debía devolver el código 1 (sin modificar instalación).'
        Require ($currentMode -eq 'v2.0|ok') 'Una actualización inválida alteró la versión instalada.'
        Require ($oldManaged -and -not $newManaged) 'Una actualización inválida modificó archivos gestionados.'
        Require (Test-Path -LiteralPath $statePath) 'No se registró la actualización fallida.'
    }
    elseif ($mode -eq 'fail') {
        Require ($exitCode -eq 2) 'Un arranque fallido debía activar rollback y devolver código 2.'
        Require ($currentMode -eq 'v2.0|ok') 'El rollback no restauró la versión anterior.'
        Require ($oldManaged -and -not $newManaged) 'El rollback no restauró exactamente los archivos gestionados.'
        Require (Test-Path -LiteralPath $statePath) 'El rollback no guardó el tag fallido.'
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        Require ($state.FailedUpdateTag -eq 'v2.0.1') 'El tag fallido no fue registrado correctamente.'
    }
    else {
        Require ($exitCode -eq 0) 'La actualización válida debía devolver código 0.'
        Require ($currentMode -eq 'v2.0.1|ok') 'La nueva versión no quedó instalada.'
        Require (-not $oldManaged -and $newManaged) 'La actualización no reemplazó correctamente los archivos gestionados.'
        Require (-not (Test-Path -LiteralPath $statePath)) 'Una actualización exitosa no debería registrar fallos.'
    }

    Require (-not (Test-Path -LiteralPath $workspace)) 'NVupdate no limpió su workspace temporal.'
    Write-Host ('[OK] ' + $name) -ForegroundColor Green
    $script:passed++
}

try {
    Write-Host ''
    Write-Host 'BandaNV v2.0 - Prueba aislada del actualizador NVupdate' -ForegroundColor Cyan
    Write-Host 'No se modificará tu instalación de BandaNV ni se publicará ninguna Release.' -ForegroundColor DarkGray
    Write-Host ''

    if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
        throw 'Este test requiere Windows.'
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'No se encontró dotnet. Necesitás .NET 10 SDK.'
    }
    Require (Test-Path -LiteralPath $updaterProject) 'No se encontró el proyecto NVupdate.'

    New-Item -ItemType Directory -Path (Split-Path -Parent $probeProject) -Force | Out-Null

    $projectContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <AssemblyName>BandaNV</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@

    $programContent = @'
using System.Text.Json;
using System.Text;

var modePath = Path.Combine(AppContext.BaseDirectory, "probe-release.txt");
if (!File.Exists(modePath))
{
    return;
}

var config = File.ReadAllText(modePath).Trim().Split('|');
if (config.Length != 2)
{
    return;
}

var version = config[0];
var mode = config[1];

string? GetArg(string name)
{
    for (int i = 0; i + 1 < args.Length; i += 2)
    {
        if (args[i] == name)
        {
            return args[i + 1];
        }
    }

    return null;
}

var confirmPath = GetArg("--update-confirm-path");
var token = GetArg("--update-token");

if (mode == "fail")
{
    Environment.Exit(4);
}

if (!string.IsNullOrWhiteSpace(confirmPath) &&
    !string.IsNullOrWhiteSpace(token))
{
    var payload = new
    {
        Token = token,
        Version = version,
        ConfirmedAt = DateTime.Now,
        ProcessId = Environment.ProcessId
    };

    var temporary = confirmPath + ".tmp";
    File.WriteAllText(
        temporary,
        JsonSerializer.Serialize(payload),
        new UTF8Encoding(false));
    File.Move(temporary, confirmPath, true);
    Thread.Sleep(3000);
}
'@

    Write-Utf8 $probeProject $projectContent
    Write-Utf8 $probeProgram $programContent

    Write-Host 'Compilando NVupdate de la rama actual...' -ForegroundColor DarkCyan
    & dotnet publish $updaterProject -c Release -r win-x64 --self-contained true -o $updaterPublish
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de NVupdate.' }

    Write-Host 'Compilando app simulada para probar el handshake...' -ForegroundColor DarkCyan
    & dotnet publish $probeProject -c Release -r win-x64 --self-contained true -o $probePublish
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la app simulada.' }

    Require (Test-Path -LiteralPath (Join-Path $updaterPublish 'NVupdate.exe')) 'NVupdate.exe no fue generado.'
    Require (Test-Path -LiteralPath (Join-Path $probePublish 'BandaNV.exe')) 'La app de prueba no fue generada.'

    Write-Host ''
    Write-Host 'Ejecutando escenarios de seguridad...' -ForegroundColor Cyan

    Invoke-UpdateScenario 'actualizacion-exitosa' 'ok' $false
    Invoke-UpdateScenario 'rollback-por-arranque-fallido' 'fail' $false
    Invoke-UpdateScenario 'paquete-manifest-invalido' 'ok' $true

    Write-Host ''
    Write-Host ('Resultado: ' + $passed + ' OK · ' + $failed + ' error(es)') -ForegroundColor Green
    Write-Host 'Este test verifica NVupdate en Windows. La descarga GitHub/SHA-256 y la interfaz requieren la prueba final integrada.' -ForegroundColor DarkGray
}
catch {
    $failed++
    Write-Host ''
    Write-Host 'ERROR: prueba del actualizador incompleta.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    if (-not $KeepArtifacts) {
        try {
            Remove-Item -LiteralPath $testRoot -Force -Recurse -ErrorAction SilentlyContinue
        }
        catch {
            Write-Host 'No se pudo eliminar alguna carpeta temporal; podés revisarla manualmente.' -ForegroundColor Yellow
        }
    }
    Write-Host ''
    Read-Host 'Presioná ENTER para cerrar'
}
