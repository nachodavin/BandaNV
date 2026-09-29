param(
    [Parameter(Mandatory=$true)][int]$ParentPid,
    [Parameter(Mandatory=$true)][string]$AppPath,
    [Parameter(Mandatory=$true)][string]$StagedExe,
    [Parameter(Mandatory=$true)][string]$BackupPath,
    [Parameter(Mandatory=$true)][string]$ConfirmPath,
    [Parameter(Mandatory=$true)][string]$UpdateToken,
    [string]$ExpectedVersion = '',
    [int]$ParentExitTimeoutSec = 30,
    [int]$ConfirmTimeoutSec = 20
)

$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms

function Show-NVUpdateError([string]$message) {
    try {
        [System.Windows.Forms.MessageBox]::Show(
            $message,
            'BandaNV — NVupdate',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    } catch {}
}

function Wait-NVProcessExit([int]$processId,[int]$timeoutSec) {
    if($processId -le 0){ return $true }
    try {
        $p=Get-Process -Id $processId -ErrorAction Stop
    } catch {
        return $true
    }

    try {
        return $p.WaitForExit([Math]::Max(1,$timeoutSec)*1000)
    } catch {
        return $false
    }
}

function Test-NVConfirmation([string]$path,[string]$token) {
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){ return $false }
    try {
        $data=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        return ([string]$data.token -ceq $token)
    } catch {
        return $false
    }
}

function Stop-NVProcessSafely($process) {
    if($null -eq $process){ return }
    try {
        if(-not $process.HasExited) {
            try { [void]$process.CloseMainWindow() } catch {}
            Start-Sleep -Milliseconds 700
            try { $process.Refresh() } catch {}
            if(-not $process.HasExited) {
                try { $process.Kill() } catch {}
            }
        }
    } catch {}
}

$backupCreated=$false
$newProcess=$null

try {
    $appFull=[IO.Path]::GetFullPath($AppPath)
    $stagedFull=[IO.Path]::GetFullPath($StagedExe)
    $backupFull=[IO.Path]::GetFullPath($BackupPath)
    $confirmFull=[IO.Path]::GetFullPath($ConfirmPath)

    if([IO.Path]::GetFileName($appFull) -ine 'BandaNV.exe'){
        throw 'La ruta objetivo no corresponde a BandaNV.exe.'
    }
    if(-not (Test-Path -LiteralPath $appFull -PathType Leaf)){
        throw 'No se encontró la instalación actual de BandaNV.'
    }
    if(-not (Test-Path -LiteralPath $stagedFull -PathType Leaf)){
        throw 'No se encontró el ejecutable nuevo preparado para instalar.'
    }
    if([string]::IsNullOrWhiteSpace($UpdateToken)){
        throw 'Falta el token de confirmación de la actualización.'
    }

    if(-not (Wait-NVProcessExit $ParentPid $ParentExitTimeoutSec)){
        throw 'BandaNV no terminó de cerrarse a tiempo. No se modificó la instalación.'
    }

    $backupDir=[IO.Path]::GetDirectoryName($backupFull)
    $confirmDir=[IO.Path]::GetDirectoryName($confirmFull)
    if(-not [string]::IsNullOrWhiteSpace($backupDir)){ New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }
    if(-not [string]::IsNullOrWhiteSpace($confirmDir)){ New-Item -ItemType Directory -Force -Path $confirmDir | Out-Null }

    Remove-Item -LiteralPath $confirmFull -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath $appFull -Destination $backupFull -Force
    $backupCreated=$true

    # El archivo preparado ya fue descargado/verificado por BandaNV.
    Copy-Item -LiteralPath $stagedFull -Destination $appFull -Force

    $launchArgs=@(
        '-UpdateConfirmPath', $confirmFull,
        '-UpdateToken', $UpdateToken
    )
    $newProcess=Start-Process -FilePath $appFull -ArgumentList $launchArgs -WorkingDirectory ([IO.Path]::GetDirectoryName($appFull)) -PassThru

    $deadline=(Get-Date).AddSeconds([Math]::Max(3,$ConfirmTimeoutSec))
    $confirmed=$false

    while((Get-Date) -lt $deadline) {
        if(Test-NVConfirmation $confirmFull $UpdateToken) {
            $confirmed=$true
            break
        }

        try {
            $newProcess.Refresh()
            if($newProcess.HasExited){ break }
        } catch { break }

        Start-Sleep -Milliseconds 250
    }

    if(-not $confirmed) {
        throw 'La nueva versión no confirmó un inicio correcto dentro del tiempo esperado.'
    }

    # Confirmación recibida: recién ahora se elimina el backup.
    Remove-Item -LiteralPath $backupFull -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $confirmFull -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stagedFull -Force -ErrorAction SilentlyContinue
    exit 0
}
catch {
    $failure=$_.Exception.Message

    try { Stop-NVProcessSafely $newProcess } catch {}

    if($backupCreated -and (Test-Path -LiteralPath $backupFull -PathType Leaf)) {
        try {
            Copy-Item -LiteralPath $backupFull -Destination $appFull -Force
            Remove-Item -LiteralPath $confirmFull -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath $appFull -WorkingDirectory ([IO.Path]::GetDirectoryName($appFull)) | Out-Null
            Show-NVUpdateError ("La actualización no pudo completarse y BandaNV restauró automáticamente la versión anterior.`r`n`r`nDetalle: " + $failure)
            exit 2
        } catch {
            Show-NVUpdateError ("La actualización falló y tampoco se pudo completar el rollback automático.`r`n`r`nBackup: " + $backupFull + "`r`n`r`nDetalle: " + $_.Exception.Message)
            exit 3
        }
    }

    Show-NVUpdateError ("La actualización no pudo iniciarse.`r`n`r`nNo se modificó BandaNV.`r`n`r`nDetalle: " + $failure)
    exit 1
}
