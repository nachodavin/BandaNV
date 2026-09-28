Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# BandaNV v1.0 RC1
$script:AppVersion = '1.0 RC1.3.2'

# Determina la carpeta real de BandaNV tanto al ejecutar el .ps1 como el .exe compilado con PS2EXE.
$script:AppDir = $null
if ($PSScriptRoot -and (Test-Path -LiteralPath $PSScriptRoot -PathType Container)) {
    $script:AppDir = $PSScriptRoot
}
if ([string]::IsNullOrWhiteSpace($script:AppDir)) {
    try {
        $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        if (-not [string]::IsNullOrWhiteSpace($exePath)) {
            $script:AppDir = [System.IO.Path]::GetDirectoryName($exePath)
        }
    } catch {}
}
if ([string]::IsNullOrWhiteSpace($script:AppDir)) {
    [System.Windows.Forms.MessageBox]::Show('No se pudo determinar la carpeta de BandaNV.', 'BandaNV', 'OK', 'Error') | Out-Null
    exit 1
}
$script:ConfigDir = Join-Path $script:AppDir 'config'
$script:LogsDir = Join-Path $script:AppDir 'logs'
$script:ConfigPath = Join-Path $script:ConfigDir 'bandanv_config.json'

function Get-DefaultConfig {
    [ordered]@{
        autoDownloads = $true
        source = ''
        organizedFolder = 'ORGANIZADO'
        categories = [ordered]@{
            'RAR'        = @('.zip','.rar','.7z','.tar','.gz')
            'INSTALLERS' = @('.exe','.msi','.msix','.appx')
            'DOCUMENTS'  = @('.pdf','.doc','.docx','.xls','.xlsx','.ppt','.pptx','.txt','.csv','.rtf','.odt','.ods')
            'IMAGES'     = @('.jpg','.jpeg','.png','.gif','.bmp','.webp','.svg','.ico','.heic')
            'VIDEOS'     = @('.mp4','.mkv','.avi','.mov','.wmv','.webm','.m4v')
            'AUDIO'      = @('.mp3','.wav','.flac','.aac','.ogg','.m4a','.wma')
            'TORRENTS'   = @('.torrent')
        }
    }
}

function Ensure-AppData {
    New-Item -ItemType Directory -Force -Path $script:ConfigDir,$script:LogsDir | Out-Null
    if (-not (Test-Path $script:ConfigPath)) { Save-Config (Get-DefaultConfig) }
}
function Save-Config($cfg) {
    New-Item -ItemType Directory -Force -Path $script:ConfigDir | Out-Null
    $cfg | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $script:ConfigPath -Encoding UTF8
}
function Load-Config {
    Ensure-AppData
    try { return (Get-Content -LiteralPath $script:ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json) }
    catch {
        $bad = Join-Path $script:ConfigDir ('bandanv_config_corrupt_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.json')
        try { Copy-Item $script:ConfigPath $bad -Force } catch {}
        $d = Get-DefaultConfig; Save-Config $d
        [System.Windows.Forms.MessageBox]::Show("La configuración estaba dañada. Se creó una copia de seguridad y se restauraron los valores predeterminados.", 'BandaNV', 'OK', 'Warning') | Out-Null
        return (Get-Content -LiteralPath $script:ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    }
}
function Get-DownloadsFolder {
    try {
        $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
        $v = (Get-ItemProperty -Path $key -ErrorAction Stop).'{374DE290-123F-4565-9164-39C4925E467B}'
        if ($v) { return [Environment]::ExpandEnvironmentVariables($v) }
    } catch {}
    $fallback = Join-Path $HOME 'Downloads'
    return $fallback
}
function Resolve-Source($cfg) {
    if ($cfg.autoDownloads) { return Get-DownloadsFolder }
    return [string]$cfg.source
}
function Get-UniqueDestination([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $path }
    $dir=[IO.Path]::GetDirectoryName($path); $name=[IO.Path]::GetFileNameWithoutExtension($path); $ext=[IO.Path]::GetExtension($path); $i=2
    do { $candidate=Join-Path $dir ("$name ($i)$ext"); $i++ } while (Test-Path -LiteralPath $candidate)
    return $candidate
}
function New-LogPath {
    $base='BandaNV_' + (Get-Date -Format 'yyyy-MM-dd____HH-mm')
    $p=Join-Path $script:LogsDir ($base+'.txt'); $i=2
    while (Test-Path -LiteralPath $p) { $p=Join-Path $script:LogsDir ("$base($i).txt"); $i++ }
    return $p
}
function Get-LastRun {
    Ensure-AppData
    $f=Get-ChildItem -LiteralPath $script:LogsDir -Filter 'BandaNV_*.txt' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $f) { return 'Sin ejecuciones registradas' }
    if ($f.BaseName -match '^BandaNV_(\d{4})-(\d{2})-(\d{2})____(\d{2})-(\d{2})') { return "$($Matches[3])/$($Matches[2])/$($Matches[1]) - $($Matches[4]):$($Matches[5])" }
    return $f.LastWriteTime.ToString('dd/MM/yyyy - HH:mm')
}

function Get-CategoryDisplayName([string]$name) {
    if ($name -match '^\s*\d+\s*-\s*(.+)$') { return $Matches[1].Trim() }
    return $name.Trim()
}
function Get-CategoryFolderName([int]$index, [string]$name) {
    return ("{0} - {1}" -f $index, (Get-CategoryDisplayName $name))
}
function Get-OrderedCategoryCards($cards) {
    # La posicion visible manda. En FlowLayoutPanel el Z-order y el orden de Controls
    # pueden no coincidir despues de varios SetChildIndex, pero Top refleja lo que ve el usuario.
    return @($cards.Controls | Where-Object { $_ -is [Windows.Forms.GroupBox] } | Sort-Object Top,Left)
}

function Sync-CategoryFolders($oldCfg, $cards) {
    $oldSource=Resolve-Source $oldCfg
    if ([string]::IsNullOrWhiteSpace($oldSource) -or -not (Test-Path -LiteralPath $oldSource -PathType Container)) { return }

    $oldRoot=Join-Path $oldSource ([string]$oldCfg.organizedFolder)
    if (-not (Test-Path -LiteralPath $oldRoot -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $oldRoot | Out-Null
    }

    # Mapa del orden anterior, usado como una de varias pistas para localizar cada carpeta.
    $oldPositions=@{}
    $oldIndex=1
    foreach($p in $oldCfg.categories.PSObject.Properties) {
        $display=Get-CategoryDisplayName ([string]$p.Name)
        $oldPositions[$display]=Get-CategoryFolderName $oldIndex $display
        $oldIndex++
    }

    # Escaneamos las carpetas reales. Esto hace que la sincronizacion no dependa de que
    # una RC anterior haya dejado exactamente el nombre que esperabamos.
    $existing=@(Get-ChildItem -LiteralPath $oldRoot -Directory -ErrorAction SilentlyContinue)
    $moves=New-Object System.Collections.Generic.List[object]
    $reservedOld=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    $index=1
    foreach($g in (Get-OrderedCategoryCards $cards)) {
        $meta=$g.Tag
        $display=Get-CategoryDisplayName $meta.NameBox.Text.Trim()
        $newName=Get-CategoryFolderName $index $display
        $finalPath=Join-Path $oldRoot $newName
        $oldDisplay=Get-CategoryDisplayName ([string]$meta.OriginalFolder)
        if([string]::IsNullOrWhiteSpace($oldDisplay)){ $oldDisplay=$display }

        $candidateNames=New-Object System.Collections.Generic.List[string]
        if($oldPositions.ContainsKey($oldDisplay)){[void]$candidateNames.Add([string]$oldPositions[$oldDisplay])}
        if(-not [string]::IsNullOrWhiteSpace([string]$meta.OriginalFolder)){[void]$candidateNames.Add([string]$meta.OriginalFolder)}
        [void]$candidateNames.Add($oldDisplay)

        $oldPath=$null
        foreach($candidate in $candidateNames){
            $candidatePath=Join-Path $oldRoot $candidate
            if((Test-Path -LiteralPath $candidatePath -PathType Container) -and -not $reservedOld.Contains($candidatePath)){
                $oldPath=$candidatePath; break
            }
        }
        if($null -eq $oldPath){
            foreach($dir in $existing){
                if($reservedOld.Contains($dir.FullName)){continue}
                if((Get-CategoryDisplayName $dir.Name) -ieq $oldDisplay){$oldPath=$dir.FullName;break}
            }
        }

        if($null -ne $oldPath){
            [void]$reservedOld.Add($oldPath)
            [void]$moves.Add([PSCustomObject]@{Old=$oldPath;Final=$finalPath;Temp=$null;Name=$display})
        } else {
            # Categoria nueva: la carpeta se crea al guardar, no hace falta esperar a ORGANIZAR.
            [void]$moves.Add([PSCustomObject]@{Old=$null;Final=$finalPath;Temp=$null;Name=$display})
        }
        $index++
    }

    # Fase 1: todo lo que deba cambiar de nombre va primero a un nombre temporal unico.
    # Asi 2 -> 3 y 3 -> 2 nunca colisionan entre si.
    foreach($m in $moves) {
        if($null -eq $m.Old -or $m.Old -ieq $m.Final){continue}
        $temp=Join-Path $oldRoot ('__BANDANV_TEMP_' + [guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $m.Old -Destination $temp -ErrorAction Stop
        $m.Temp=$temp
    }

    # Fase 2: aplicamos los nombres definitivos segun el orden VISUAL actual.
    foreach($m in $moves) {
        if(-not [string]::IsNullOrWhiteSpace([string]$m.Temp)){
            if(Test-Path -LiteralPath $m.Final) { throw "Ya existe una carpeta que impide reorganizar: $($m.Final)" }
            Move-Item -LiteralPath $m.Temp -Destination $m.Final -ErrorAction Stop
        } elseif($null -eq $m.Old) {
            if(-not (Test-Path -LiteralPath $m.Final -PathType Container)){
                New-Item -ItemType Directory -Force -Path $m.Final | Out-Null
            }
        }
    }
}

function Invoke-Organizer($statusLabel,$lastLabel,$button) {
    $cfg=Load-Config; $source=Resolve-Source $cfg
    if ([string]::IsNullOrWhiteSpace($source) -or -not (Test-Path -LiteralPath $source -PathType Container)) {
        [System.Windows.Forms.MessageBox]::Show("La carpeta de origen no existe:`n$source`n`nRevisá Configuración.", 'BandaNV', 'OK', 'Error') | Out-Null; return
    }
    $organized=[string]$cfg.organizedFolder
    if ([string]::IsNullOrWhiteSpace($organized) -or $organized.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        [System.Windows.Forms.MessageBox]::Show('El nombre de la carpeta de organización no es válido.', 'BandaNV', 'OK', 'Error') | Out-Null; return
    }
    $destRoot=Join-Path $source $organized
    New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
    $map=@{}
    $categoryIndex=1
    foreach($p in $cfg.categories.PSObject.Properties) {
        # El JSON guarda solo el nombre humano; la carpeta fisica se numera por posicion.
        $cat=Get-CategoryFolderName $categoryIndex ([string]$p.Name)
        New-Item -ItemType Directory -Force -Path (Join-Path $destRoot $cat) | Out-Null
        foreach($e in $p.Value) { $x=([string]$e).Trim().ToLower(); if($x -and -not $x.StartsWith('.')){$x='.$'+$x}; if($x){$map[$x]=$cat} }
        $categoryIndex++
    }
    $start=Get-Date; $log=New-LogPath; $lines=New-Object System.Collections.Generic.List[string]
    $lines.Add('=================================================='); $lines.Add('BandaNV - Registro de ejecución'); $lines.Add('=================================================='); $lines.Add('')
    $lines.Add('Inicio: '+$start.ToString('dd/MM/yyyy HH:mm:ss')); $lines.Add('Origen: '+$source); $lines.Add('Destino: '+$destRoot); $lines.Add(''); $lines.Add('--------------------------------------------------'); $lines.Add('')
    $button.Enabled=$false; $statusLabel.Text='Estado: Organizando...'; [System.Windows.Forms.Application]::DoEvents()
    $count=0; $errors=0
    try {
        foreach($file in Get-ChildItem -LiteralPath $source -File -ErrorAction Stop) {
            $ext=$file.Extension.ToLower(); if(-not $map.ContainsKey($ext)){continue}
            $cat=$map[$ext]; $target=Get-UniqueDestination (Join-Path (Join-Path $destRoot $cat) $file.Name)
            try { Move-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop; $count++; $lines.Add("[MOVIDO] $($file.Name)"); $lines.Add("         -> $cat"); $lines.Add(''); $statusLabel.Text="Estado: Organizando... $count archivo(s)"; [System.Windows.Forms.Application]::DoEvents() }
            catch { $errors++; $lines.Add("[ERROR] $($file.Name) - $($_.Exception.Message)"); $lines.Add('') }
        }
    } catch { $errors++; $lines.Add('[ERROR] No se pudo enumerar la carpeta: '+$_.Exception.Message) }
    $end=Get-Date; $duration=[math]::Round(($end-$start).TotalSeconds,2)
    $lines.Add('--------------------------------------------------'); $lines.Add(''); $lines.Add("Archivos procesados: $count"); $lines.Add("Errores: $errors"); if($count -eq 0 -and $errors -eq 0){$lines.Add('Estado: Sin archivos para organizar.')} elseif($errors -eq 0){$lines.Add('Estado: Finalizada correctamente.')} else {$lines.Add('Estado: Finalizada con errores.')}
    $lines.Add('Finalización: '+$end.ToString('dd/MM/yyyy HH:mm:ss')); $lines.Add("Duración: $duration segundos"); $lines.Add(''); $lines.Add('==================================================')
    $lines | Set-Content -LiteralPath $log -Encoding UTF8
    $button.Enabled=$true; $lastLabel.Text=Get-LastRun
    if($errors -gt 0){$statusLabel.Text="Estado: $count organizados, $errors error(es)"} elseif($count -eq 0){$statusLabel.Text='Estado: ✓ Todo limpio'} else {$statusLabel.Text="Estado: ✓ $count archivo(s) organizado(s)"}
}

function Get-LogSummary([System.IO.FileInfo]$file) {
    $raw = ''
    try { $raw = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 } catch {}
    $count = 0; $errors = 0; $statusText = 'Ejecución registrada'
    if ($raw -match 'Archivos procesados:\s*(\d+)') { $count = [int]$Matches[1] }
    if ($raw -match 'Errores:\s*(\d+)') { $errors = [int]$Matches[1] }
    if ($errors -gt 0) { $statusText = "$count archivo(s) - $errors error(es)" }
    elseif ($count -eq 0) { $statusText = '0 archivos - Todo limpio' }
    else { $statusText = "$count archivo(s) organizado(s)" }
    $dateText = $file.LastWriteTime.ToString('dd/MM/yyyy - HH:mm')
    if ($file.BaseName -match '^BandaNV_(\d{4})-(\d{2})-(\d{2})____(\d{2})-(\d{2})') {
        $dateText = "$($Matches[3])/$($Matches[2])/$($Matches[1]) - $($Matches[4]):$($Matches[5])"
    }
    $source=''; $destination=''; $duration=''; $moved=New-Object System.Collections.Generic.List[string]; $categoryCounts=@{}; $pendingFile=$null
    foreach($line in ($raw -split "`r?`n")) {
        if($line -match '^Origen:\s*(.+)$'){$source=$Matches[1].Trim();continue}
        if($line -match '^Destino:\s*(.+)$'){$destination=$Matches[1].Trim();continue}
        if($line -match '^Duración:\s*(.+)$'){$duration=$Matches[1].Trim();continue}
        if($line -match '^\[MOVIDO\]\s*(.+)$'){$pendingFile=$Matches[1].Trim();continue}
        if($null -ne $pendingFile -and $line -match '^\s*->\s*(.+)$'){$cat=$Matches[1].Trim(); [void]$moved.Add("$pendingFile  ->  $cat"); if($categoryCounts.ContainsKey($cat)){$categoryCounts[$cat]++}else{$categoryCounts[$cat]=1}; $pendingFile=$null}
    }
    $friendly=New-Object System.Collections.Generic.List[string]
    if($source){$friendly.Add('Origen: '+$source)}; if($destination){$friendly.Add('Destino: '+$destination)}; $friendly.Add(''); $friendly.Add('Resultado: '+$statusText); if($duration){$friendly.Add('Duración: '+$duration)}
    if($categoryCounts.Count -gt 0){$friendly.Add(''); $friendly.Add('Por categoría:'); foreach($k in ($categoryCounts.Keys | Sort-Object)){$friendly.Add(('  {0}: {1}' -f $k, $categoryCounts[$k]))}}
    if($moved.Count -gt 0){$friendly.Add(''); $friendly.Add('Archivos movidos:'); foreach($m in $moved){$friendly.Add('  '+$m)}}
    if($errors -gt 0){$friendly.Add(''); $friendly.Add('Para revisar los errores completos, abrí el archivo de log.')}
    return [PSCustomObject]@{ File=$file; Date=$dateText; Count=$count; Errors=$errors; Status=$statusText; Raw=($friendly -join [Environment]::NewLine) }
}

function Show-HistoryDetail($summary, $owner) {
    $form=New-Object Windows.Forms.Form; $form.Text='BandaNV — Detalle de ejecución'; $form.Size=New-Object Drawing.Size(650,560); $form.StartPosition='CenterParent'; $form.MinimumSize=New-Object Drawing.Size(580,460)
    $title=New-Object Windows.Forms.Label; $title.Text=$summary.Date; $title.Font=New-Object Drawing.Font('Segoe UI',16,[Drawing.FontStyle]::Bold); $title.Location=New-Object Drawing.Point(22,20); $title.AutoSize=$true
    $state=New-Object Windows.Forms.Label; $state.Text=$summary.Status; $state.Font=New-Object Drawing.Font('Segoe UI',10); $state.Location=New-Object Drawing.Point(25,58); $state.AutoSize=$true
    $detail=New-Object Windows.Forms.TextBox; $detail.Location=New-Object Drawing.Point(25,95); $detail.Size=New-Object Drawing.Size(585,365); $detail.Multiline=$true; $detail.ReadOnly=$true; $detail.ScrollBars='Vertical'; $detail.Anchor='Top,Bottom,Left,Right'; $detail.Font=New-Object Drawing.Font('Consolas',9); $detail.Text=$summary.Raw
    $close=New-Object Windows.Forms.Button; $close.Text='CERRAR'; $close.Location=New-Object Drawing.Point(490,475); $close.Size=New-Object Drawing.Size(120,32); $close.Anchor='Bottom,Right'; $close.Add_Click({$form.Close()})
    $form.Controls.AddRange(@($title,$state,$detail,$close)); [void]$form.ShowDialog($owner)
}

function Show-History {
    Ensure-AppData
    $form=New-Object Windows.Forms.Form; $form.Text='BandaNV — Historial'; $form.Size=New-Object Drawing.Size(650,560); $form.StartPosition='CenterParent'; $form.MinimumSize=New-Object Drawing.Size(580,460)
    $title=New-Object Windows.Forms.Label; $title.Text='HISTORIAL'; $title.Font=New-Object Drawing.Font('Segoe UI',18,[Drawing.FontStyle]::Bold); $title.Location=New-Object Drawing.Point(22,18); $title.AutoSize=$true
    $hint=New-Object Windows.Forms.Label; $hint.Text='Doble clic en una ejecución para ver el detalle.'; $hint.Location=New-Object Drawing.Point(25,58); $hint.AutoSize=$true
    $list=New-Object Windows.Forms.ListView; $list.Location=New-Object Drawing.Point(25,88); $list.Size=New-Object Drawing.Size(585,365); $list.View='Details'; $list.FullRowSelect=$true; $list.GridLines=$false; $list.HideSelection=$false; $list.Anchor='Top,Bottom,Left,Right'
    [void]$list.Columns.Add('Fecha y hora',190); [void]$list.Columns.Add('Resultado',355)
    $files=@(Get-ChildItem -LiteralPath $script:LogsDir -Filter 'BandaNV_*.txt' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    foreach($f in $files){
        $s=Get-LogSummary $f; $item=New-Object Windows.Forms.ListViewItem($s.Date); [void]$item.SubItems.Add($s.Status); $item.Tag=$s; [void]$list.Items.Add($item)
    }
    if($list.Items.Count -eq 0){ $item=New-Object Windows.Forms.ListViewItem('Sin ejecuciones'); [void]$item.SubItems.Add('Todavía no hay actividad registrada.'); $item.ForeColor=[Drawing.Color]::Gray; [void]$list.Items.Add($item) }
    $list.Add_DoubleClick({ if($list.SelectedItems.Count -gt 0 -and $null -ne $list.SelectedItems[0].Tag){ Show-HistoryDetail $list.SelectedItems[0].Tag $form } })
    $detailBtn=New-Object Windows.Forms.Button; $detailBtn.Text='Ver detalle'; $detailBtn.Location=New-Object Drawing.Point(25,470); $detailBtn.Size=New-Object Drawing.Size(120,32); $detailBtn.Anchor='Bottom,Left'; $detailBtn.Add_Click({if($list.SelectedItems.Count -gt 0 -and $null -ne $list.SelectedItems[0].Tag){Show-HistoryDetail $list.SelectedItems[0].Tag $form}})
    $open=New-Object Windows.Forms.Button; $open.Text='Abrir carpeta de logs'; $open.Location=New-Object Drawing.Point(160,470); $open.Size=New-Object Drawing.Size(155,32); $open.Anchor='Bottom,Left'; $open.Add_Click({Start-Process explorer.exe -ArgumentList ('"'+$script:LogsDir+'"')})
    $close=New-Object Windows.Forms.Button; $close.Text='CERRAR'; $close.Location=New-Object Drawing.Point(490,470); $close.Size=New-Object Drawing.Size(120,32); $close.Anchor='Bottom,Right'; $close.Add_Click({$form.Close()})
    $form.Controls.AddRange(@($title,$hint,$list,$detailBtn,$open,$close)); [void]$form.ShowDialog()
}

function Add-ExtensionField($panel, [string]$value, $markDirty) {
    $holder=New-Object Windows.Forms.Panel; $holder.Size=New-Object Drawing.Size(112,30); $holder.Margin=New-Object Windows.Forms.Padding(0,0,7,6)
    $box=New-Object Windows.Forms.TextBox; $box.Location=New-Object Drawing.Point(0,2); $box.Size=New-Object Drawing.Size(80,23); $box.Text=$value
    if($null -ne $markDirty){$box.Add_TextChanged($markDirty)}
    $remove=New-Object Windows.Forms.Button; $remove.Text='×'; $remove.Location=New-Object Drawing.Point(82,1); $remove.Size=New-Object Drawing.Size(28,25); $remove.Tag=[PSCustomObject]@{Holder=$holder;Dirty=$markDirty}
    $remove.Add_Click({ param($sender,$e) $x=$sender.Tag; $p=$x.Holder; if($null -ne $p -and $null -ne $p.Parent){$p.Parent.Controls.Remove($p); $p.Dispose(); if($null -ne $x.Dirty){& $x.Dirty}} })
    $holder.Controls.AddRange(@($box,$remove)); $holder.Tag=$box; [void]$panel.Controls.Add($holder)
}

function Update-CategoryNumbers($cards) {
    $i=1
    foreach($g in @($cards.Controls)) {
        if($g -is [Windows.Forms.GroupBox]) { $g.Text="Categoría $i"; $g.Tag.NumberLabel.Text=[string]$i; $i++ }
    }
}

function Move-CategoryCard($cards,$group,[int]$delta,$markDirty) {
    $idx=$cards.Controls.GetChildIndex($group)
    $new=$idx+$delta
    if($new -lt 0 -or $new -ge $cards.Controls.Count){return}
    $cards.Controls.SetChildIndex($group,$new)
    Update-CategoryNumbers $cards
    if($null -ne $markDirty){& $markDirty}
}

function Add-CategoryCard($cards, [string]$name, $extensions, [string]$originalFolder, $markDirty) {
    $group=New-Object Windows.Forms.GroupBox; $group.Text='Categoría'; $group.Size=New-Object Drawing.Size(680,155); $group.Margin=New-Object Windows.Forms.Padding(3,3,3,10)
    $num=New-Object Windows.Forms.Label; $num.Location=New-Object Drawing.Point(15,25); $num.Size=New-Object Drawing.Size(28,24); $num.Font=New-Object Drawing.Font('Segoe UI',10,[Drawing.FontStyle]::Bold); $num.TextAlign='MiddleCenter'
    $nameLbl=New-Object Windows.Forms.Label; $nameLbl.Text='Nombre'; $nameLbl.Location=New-Object Drawing.Point(50,27); $nameLbl.AutoSize=$true
    $nameBox=New-Object Windows.Forms.TextBox; $nameBox.Location=New-Object Drawing.Point(110,24); $nameBox.Size=New-Object Drawing.Size(360,24); $nameBox.Text=(Get-CategoryDisplayName $name)
    if($null -ne $markDirty){$nameBox.Add_TextChanged($markDirty)}
    $up=New-Object Windows.Forms.Button; $up.Text='↑'; $up.Location=New-Object Drawing.Point(480,22); $up.Size=New-Object Drawing.Size(38,28)
    $down=New-Object Windows.Forms.Button; $down.Text='↓'; $down.Location=New-Object Drawing.Point(522,22); $down.Size=New-Object Drawing.Size(38,28)
    $delete=New-Object Windows.Forms.Button; $delete.Text='Eliminar'; $delete.Location=New-Object Drawing.Point(568,22); $delete.Size=New-Object Drawing.Size(87,28)
    $extLbl=New-Object Windows.Forms.Label; $extLbl.Text='Extensiones'; $extLbl.Location=New-Object Drawing.Point(15,65); $extLbl.AutoSize=$true
    $extPanel=New-Object Windows.Forms.FlowLayoutPanel; $extPanel.Location=New-Object Drawing.Point(90,62); $extPanel.Size=New-Object Drawing.Size(505,76); $extPanel.AutoScroll=$true; $extPanel.WrapContents=$true
    $plus=New-Object Windows.Forms.Button; $plus.Text='+'; $plus.Location=New-Object Drawing.Point(605,62); $plus.Size=New-Object Drawing.Size(50,28)
    $meta=[PSCustomObject]@{NameBox=$nameBox;ExtPanel=$extPanel;OriginalFolder=$originalFolder;NumberLabel=$num}
    $group.Tag=$meta
    $up.Tag=[PSCustomObject]@{Cards=$cards;Group=$group;Dirty=$markDirty}; $up.Add_Click({param($sender,$e) $x=$sender.Tag; Move-CategoryCard $x.Cards $x.Group -1 $x.Dirty})
    $down.Tag=[PSCustomObject]@{Cards=$cards;Group=$group;Dirty=$markDirty}; $down.Add_Click({param($sender,$e) $x=$sender.Tag; Move-CategoryCard $x.Cards $x.Group 1 $x.Dirty})
    $delete.Tag=[PSCustomObject]@{Group=$group;Dirty=$markDirty;Cards=$cards}; $delete.Add_Click({param($sender,$e) $x=$sender.Tag; if($null -ne $x.Group.Parent){$x.Group.Parent.Controls.Remove($x.Group);$x.Group.Dispose();Update-CategoryNumbers $x.Cards;if($null -ne $x.Dirty){& $x.Dirty}}})
    $plus.Tag=[PSCustomObject]@{Panel=$extPanel;Dirty=$markDirty}; $plus.Add_Click({param($sender,$e) $x=$sender.Tag; Add-ExtensionField $x.Panel '' $x.Dirty; if($null -ne $x.Dirty){& $x.Dirty}})
    foreach($ext in @($extensions)){ Add-ExtensionField $extPanel ([string]$ext) $markDirty }
    if($extPanel.Controls.Count -eq 0){Add-ExtensionField $extPanel '' $markDirty}
    $group.Controls.AddRange(@($num,$nameLbl,$nameBox,$up,$down,$delete,$extLbl,$extPanel,$plus)); [void]$cards.Controls.Add($group)
    Update-CategoryNumbers $cards
}

function Show-Config {
    $cfg=Load-Config
    $form=New-Object Windows.Forms.Form; $form.Text='BandaNV — Configuración'; $form.Size=New-Object Drawing.Size(800,720); $form.StartPosition='CenterParent'; $form.MinimumSize=New-Object Drawing.Size(750,620)
    $script:ConfigDirty=$false; $script:ConfigSaving=$false
    $markDirty={ $script:ConfigDirty=$true; if($null -ne $save){$save.Enabled=$true} }

    $title=New-Object Windows.Forms.Label; $title.Text='CONFIGURACIÓN'; $title.Font=New-Object Drawing.Font('Segoe UI',18,[Drawing.FontStyle]::Bold); $title.Location=New-Object Drawing.Point(22,18); $title.AutoSize=$true
    $auto=New-Object Windows.Forms.RadioButton; $auto.Text='Detectar Descargas automáticamente'; $auto.Location=New-Object Drawing.Point(25,62); $auto.AutoSize=$true; $auto.Checked=[bool]$cfg.autoDownloads
    $manual=New-Object Windows.Forms.RadioButton; $manual.Text='Usar otra carpeta'; $manual.Location=New-Object Drawing.Point(25,92); $manual.AutoSize=$true; $manual.Checked=-not [bool]$cfg.autoDownloads
    $source=New-Object Windows.Forms.TextBox; $source.Location=New-Object Drawing.Point(165,90); $source.Size=New-Object Drawing.Size(485,24); $source.Text=[string]$cfg.source; $source.Anchor='Top,Left,Right'
    $browse=New-Object Windows.Forms.Button; $browse.Text='Examinar...'; $browse.Location=New-Object Drawing.Point(660,88); $browse.Size=New-Object Drawing.Size(95,28); $browse.Anchor='Top,Right'
    $orgLbl=New-Object Windows.Forms.Label; $orgLbl.Text='Nombre de carpeta organizada'; $orgLbl.Location=New-Object Drawing.Point(25,132); $orgLbl.AutoSize=$true
    $org=New-Object Windows.Forms.TextBox; $org.Location=New-Object Drawing.Point(25,154); $org.Size=New-Object Drawing.Size(730,24); $org.Text=[string]$cfg.organizedFolder; $org.Anchor='Top,Left,Right'
    $catsLbl=New-Object Windows.Forms.Label; $catsLbl.Text='CATEGORÍAS — usá ↑ y ↓ para definir el orden de las carpetas'; $catsLbl.Font=New-Object Drawing.Font('Segoe UI',11,[Drawing.FontStyle]::Bold); $catsLbl.Location=New-Object Drawing.Point(25,194); $catsLbl.AutoSize=$true
    $cards=New-Object Windows.Forms.FlowLayoutPanel; $cards.Location=New-Object Drawing.Point(25,222); $cards.Size=New-Object Drawing.Size(730,370); $cards.FlowDirection='TopDown'; $cards.WrapContents=$false; $cards.AutoScroll=$true; $cards.Anchor='Top,Bottom,Left,Right'
    $save=New-Object Windows.Forms.Button; $save.Text='GUARDAR'; $save.Font=New-Object Drawing.Font('Segoe UI',9,[Drawing.FontStyle]::Bold); $save.Size=New-Object Drawing.Size(120,34); $save.Location=New-Object Drawing.Point(635,608); $save.Anchor='Bottom,Right'; $save.Enabled=$false

    $auto.Add_CheckedChanged($markDirty); $manual.Add_CheckedChanged($markDirty); $source.Add_TextChanged($markDirty); $org.Add_TextChanged($markDirty)
    $toggle={ $source.Enabled=$manual.Checked; $browse.Enabled=$manual.Checked }; $auto.Add_CheckedChanged($toggle); $manual.Add_CheckedChanged($toggle); & $toggle
    $browse.Add_Click({$d=New-Object Windows.Forms.FolderBrowserDialog; if($d.ShowDialog() -eq 'OK'){$source.Text=$d.SelectedPath}})

    foreach($p in $cfg.categories.PSObject.Properties){
        $original=[string]$p.Name
        Add-CategoryCard $cards (Get-CategoryDisplayName $p.Name) $p.Value $original $markDirty
    }
    # La creación inicial de controles no cuenta como edición.
    $script:ConfigDirty=$false; $save.Enabled=$false

    $addCat=New-Object Windows.Forms.Button; $addCat.Text='+ Agregar categoría'; $addCat.Location=New-Object Drawing.Point(25,608); $addCat.Size=New-Object Drawing.Size(150,32); $addCat.Anchor='Bottom,Left'; $addCat.Add_Click({Add-CategoryCard $cards '' @('') '' $markDirty; & $markDirty})

    $save.Add_Click({
        if($manual.Checked -and ([string]::IsNullOrWhiteSpace($source.Text) -or -not (Test-Path -LiteralPath $source.Text -PathType Container))){[Windows.Forms.MessageBox]::Show('Elegí una carpeta manual válida.','BandaNV','OK','Warning')|Out-Null;return}
        if([string]::IsNullOrWhiteSpace($org.Text) -or $org.Text.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0){[Windows.Forms.MessageBox]::Show('El nombre de la carpeta organizada no es válido.','BandaNV','OK','Warning')|Out-Null;return}
        $newCats=[ordered]@{}; $used=@{}
        foreach($g in (Get-OrderedCategoryCards $cards)){
            $meta=$g.Tag; $catName=(Get-CategoryDisplayName $meta.NameBox.Text)
            if([string]::IsNullOrWhiteSpace($catName)){[Windows.Forms.MessageBox]::Show('Todas las categorías necesitan un nombre.','BandaNV','OK','Warning')|Out-Null;return}
            if($catName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0){[Windows.Forms.MessageBox]::Show("Nombre de categoría inválido: $catName",'BandaNV','OK','Warning')|Out-Null;return}
            if($newCats.Contains($catName)){[Windows.Forms.MessageBox]::Show("La categoría '$catName' está repetida.",'BandaNV','OK','Warning')|Out-Null;return}
            $exts=New-Object System.Collections.Generic.List[string]
            foreach($holder in @($meta.ExtPanel.Controls)){if($null -eq $holder.Tag){continue};$x=$holder.Tag.Text.Trim().ToLower();if([string]::IsNullOrWhiteSpace($x)){continue};if(-not $x.StartsWith('.')){$x='.'+$x};if($x -notmatch '^\.[a-z0-9][a-z0-9._+-]*$'){[Windows.Forms.MessageBox]::Show("Extensión inválida: $x",'BandaNV','OK','Warning')|Out-Null;return};if($exts -notcontains $x){[void]$exts.Add($x)}}
            if($exts.Count -eq 0){[Windows.Forms.MessageBox]::Show("La categoría '$catName' necesita al menos una extensión.",'BandaNV','OK','Warning')|Out-Null;return}
            foreach($x in $exts){if($used.ContainsKey($x)){[Windows.Forms.MessageBox]::Show("La extensión '$x' está asignada a '$($used[$x])' y '$catName'.`nCada extensión debe pertenecer a una sola categoría.",'BandaNV','OK','Warning')|Out-Null;return};$used[$x]=$catName}
            $newCats[$catName]=@($exts)
        }
        if($newCats.Count -eq 0){[Windows.Forms.MessageBox]::Show('Debe existir al menos una categoría.','BandaNV','OK','Warning')|Out-Null;return}
        try {
            Sync-CategoryFolders $cfg $cards
            $new=[ordered]@{autoDownloads=[bool]$auto.Checked;source=$source.Text.Trim();organizedFolder=$org.Text.Trim();categories=$newCats}
            Save-Config $new
            $script:ConfigDirty=$false; $save.Enabled=$false
            [Windows.Forms.MessageBox]::Show('Configuración guardada y orden de carpetas sincronizado.','BandaNV','OK','Information')|Out-Null
        } catch {
            [Windows.Forms.MessageBox]::Show("No se pudo aplicar la configuración:`n$($_.Exception.Message)`n`nNo se guardaron los cambios.",'BandaNV','OK','Error')|Out-Null
        }
    })

    $form.Add_FormClosing({
        param($sender,$e)
        if($script:ConfigDirty){
            $r=[Windows.Forms.MessageBox]::Show("Hay cambios en la configuración que todavía no fueron guardados.`n`n¿Querés salir sin guardar los cambios?",'Cambios sin guardar','YesNo','Warning')
            if($r -ne [Windows.Forms.DialogResult]::Yes){$e.Cancel=$true}
        }
    })
    $form.Controls.AddRange(@($title,$auto,$manual,$source,$browse,$orgLbl,$org,$catsLbl,$cards,$addCat,$save)); [void]$form.ShowDialog()
}

Ensure-AppData
$main=New-Object Windows.Forms.Form; $main.Text='BandaNV'; $main.Size=New-Object Drawing.Size(520,430); $main.StartPosition='CenterScreen'; $main.FormBorderStyle='FixedSingle'; $main.MaximizeBox=$false
$title=New-Object Windows.Forms.Label; $title.Text='BandaNV'; $title.Font=New-Object Drawing.Font('Segoe UI',24,[Drawing.FontStyle]::Bold); $title.Location=New-Object Drawing.Point(28,22); $title.AutoSize=$true
$ver=New-Object Windows.Forms.Label; $ver.Text='v1.0 RC1.3.1'; $ver.Location=New-Object Drawing.Point(420,35); $ver.AutoSize=$true
$subtitle=New-Object Windows.Forms.Label; $subtitle.Text='Organizador de archivos'; $subtitle.Location=New-Object Drawing.Point(32,70); $subtitle.AutoSize=$true
$folderLabel=New-Object Windows.Forms.Label; $folderLabel.Text='Carpeta'; $folderLabel.Font=New-Object Drawing.Font('Segoe UI',9,[Drawing.FontStyle]::Bold); $folderLabel.Location=New-Object Drawing.Point(32,112); $folderLabel.AutoSize=$true
$folder=New-Object Windows.Forms.Label; $folder.Location=New-Object Drawing.Point(32,135); $folder.Size=New-Object Drawing.Size(445,36); $folder.AutoEllipsis=$true
$organize=New-Object Windows.Forms.Button; $organize.Text='ORGANIZAR'; $organize.Font=New-Object Drawing.Font('Segoe UI',12,[Drawing.FontStyle]::Bold); $organize.Location=New-Object Drawing.Point(160,185); $organize.Size=New-Object Drawing.Size(190,52)
$status=New-Object Windows.Forms.Label; $status.Text='Estado: Listo para organizar'; $status.Location=New-Object Drawing.Point(32,255); $status.Size=New-Object Drawing.Size(445,25); $status.TextAlign='MiddleCenter'
$lastTitle=New-Object Windows.Forms.Label; $lastTitle.Text='Última ejecución'; $lastTitle.Font=New-Object Drawing.Font('Segoe UI',9,[Drawing.FontStyle]::Bold); $lastTitle.Location=New-Object Drawing.Point(32,295); $lastTitle.AutoSize=$true
$last=New-Object Windows.Forms.Label; $last.Location=New-Object Drawing.Point(32,318); $last.Size=New-Object Drawing.Size(445,22); $last.Text=Get-LastRun
$history=New-Object Windows.Forms.Button; $history.Text='Historial'; $history.Location=New-Object Drawing.Point(32,355); $history.Size=New-Object Drawing.Size(130,30)
$config=New-Object Windows.Forms.Button; $config.Text='Configuración'; $config.Location=New-Object Drawing.Point(347,355); $config.Size=New-Object Drawing.Size(130,30)
function Refresh-Main { $c=Load-Config; $folder.Text=Resolve-Source $c; $last.Text=Get-LastRun }
$organize.Add_Click({Invoke-Organizer $status $last $organize}); $history.Add_Click({Show-History; Refresh-Main}); $config.Add_Click({Show-Config; Refresh-Main})
$main.Add_Shown({Refresh-Main}); $main.Controls.AddRange(@($title,$ver,$subtitle,$folderLabel,$folder,$organize,$status,$lastTitle,$last,$history,$config)); [void]$main.ShowDialog()
