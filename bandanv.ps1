# 1. CONFIGURACION DE RUTAS
$global:source = "E:\Downloads" # CARPETA QUE SE VA A ORDENAR
$global:masterFolder = Join-Path $global:source "ORGANIZADO" # CARPETA DONDE VAN A SER ORGANIZADOS LOS ARCHIVOS
$global:logDir = "C:\Users\Nacho\Scripts\BandaNV" # CARPETA DONDE VA A ESTAR EL SCRIPT GUARDADO
$global:logFile = Join-Path $global:logDir "bandanv_log.txt"

$nombreDelPrograma = "bandanv.bat" 

if (-not (Test-Path -LiteralPath $global:logDir)) { New-Item -ItemType Directory -Path $global:logDir -Force | Out-Null }

if (-not (Test-Path -LiteralPath $global:masterFolder)) { New-Item -ItemType Directory -Path $global:masterFolder -Force | Out-Null }

# 2. DICCIONARIO DE CARPETAS QUE DESEAS QUE SE CREEN
$global:folders = @{
    "1 - RAR"        = @(".zip",".rar",".7z")
    "2 - INSTALLERS" = @(".exe",".msi",".bat")
    "3 - DOCUMENTS"  = @(".pdf",".docx",".xlsx",".txt")
    "4 - IMAGES"     = @(".jpg",".jpeg",".png",".webp",".avif")
    "5 - GIF"       = @(".gif")
    "6 - VIDEOS"     = @(".mp4",".mkv",".mov",".avi")
    "7 - AUDIO"      = @(".mp3",".wav",".flac",".aac",".ogg")
    "8 - EDITABLES"  = @(".ai",".psd",".eps")
}


function Get-UniquePath {
    param([string]$path)
    $dir = [System.IO.Path]::GetDirectoryName($path)
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($path)
    $ext = [System.IO.Path]::GetExtension($path)
    $newPath = $path
    $counter = 1
    while (Test-Path -LiteralPath $newPath) {
        $newPath = Join-Path $dir "$baseName($counter)$ext"
        $counter++
    }
    return $newPath
}


function Write-Log {
    param([string]$message)
    $timestamp = Get-Date -Format "dd-MM-yyyy HH:mm"
    $newLine = "[$timestamp] $message"
    
    if (Test-Path -LiteralPath $global:logFile) {
        
        $oldLog = Get-Content -LiteralPath $global:logFile
        
        $newLog = @($newLine) + $oldLog
        
        $newLog | Set-Content -LiteralPath $global:logFile
    } else {
        
        $newLine | Set-Content -LiteralPath $global:logFile
    }
}


function Sort-Now {
    $contador = 0

    Write-Log "==========================================================================================="
    Write-Log "           EJECUCION MANUAL: Moviendo Archivos"
    Write-Log "==========================================================================================="

    foreach ($folder in $global:folders.Keys) {
        $destFolder = Join-Path $global:masterFolder $folder
        if (-not (Test-Path -LiteralPath $destFolder)) { New-Item -ItemType Directory -Path $destFolder -Force | Out-Null }
        
        
        Get-ChildItem -LiteralPath $global:source -File | Where-Object {
            $_.Name -ne $nombreDelPrograma -and 
            $global:folders[$folder] -contains $_.Extension.ToLower()
        } | ForEach-Object {
            $finalDest = Get-UniquePath (Join-Path $destFolder $_.Name)
            Move-Item -LiteralPath $_.FullName -Destination $finalDest -Force
            
            $nombreFinal = [System.IO.Path]::GetFileName($finalDest)
            Write-Log "ORGANIZADO: $nombreFinal -> $folder"

            $contador++
        }
    }
    Write-Log "==========================================================================================="
    Write-Log "           EJECUCION MANUAL: Finalizada ($contador archivo/s)"
    Write-Log "==========================================================================================="
}

# BandaNV v1.0.0 by Nacho

Sort-Now