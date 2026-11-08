# Foto del estado del sistema en lo que toca TuringLite. Sirve para comprobar que la desinstalación no deja rastro:
#   powershell -ExecutionPolicy Bypass -File release\verificar-rastro.ps1 -Out antes.txt
#   ... instalar, usar, desinstalar ...
#   powershell -ExecutionPolicy Bypass -File release\verificar-rastro.ps1 -Out despues.txt
#   fc antes.txt despues.txt        (sin diferencias = sin rastro)
param([Parameter(Mandatory)][string]$Out)

$lines = [System.Collections.Generic.List[string]]::new()
function Add($name, $value) { $lines.Add(("{0}={1}" -f $name, $value)) }

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TuringLite"
Add "entrada_aplicaciones_instaladas" (Test-Path $uninstallKey)
if (Test-Path $uninstallKey) { Add "entrada_ubicacion" (Get-ItemProperty $uninstallKey).InstallLocation }

$shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "TuringLite.lnk"
Add "acceso_directo_inicio" (Test-Path $shortcut)

$runEntry = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name TuringLite -ErrorAction SilentlyContinue).TuringLite
Add "inicio_con_windows_registro" ($null -ne $runEntry)
if ($runEntry) { Add "inicio_con_windows_comando" $runEntry }

$task = Get-ScheduledTask -TaskName TuringLite -ErrorAction SilentlyContinue
Add "tarea_inicio" ($null -ne $task)
if ($task) { Add "tarea_inicio_ejecutable" $task.Actions[0].Execute; Add "tarea_inicio_nivel" $task.Principal.RunLevel }

$service = Get-CimInstance Win32_Service -Filter "Name='TuringLiteSensors'" -ErrorAction SilentlyContinue
Add "servicio_sensores" ($(if ($service) { "$($service.State) $($service.PathName)" } else { "no instalado" }))

Add "carpeta_programa_usuario" (Test-Path (Join-Path $env:LOCALAPPDATA "Programs\TuringLite"))
Add "carpeta_programa_sistema" (Test-Path (Join-Path $env:ProgramFiles "TuringLite"))
Add "carpeta_datos_documentos" (Test-Path (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "TuringLite"))
Add "carpeta_datos_appdata" (Test-Path (Join-Path $env:APPDATA "TuringLite"))

$temp = @(Get-ChildItem $env:TEMP -Filter "turinglite-*" -ErrorAction SilentlyContinue)
Add "temporales_turinglite" $temp.Count
foreach ($t in $temp) { Add "  temporal" $t.Name }

Add "controladores_pawnio" (@(Get-ChildItem "$env:WINDIR\System32\drivers" -Filter "PawnIO*" -ErrorAction SilentlyContinue).Count)
Add "servicios_pawnio" (@(Get-Service -Name "PawnIO*" -ErrorAction SilentlyContinue).Count)

# Huellas de Windows del usuario. Cada línea: entradas que mencionan TuringLite, y cuántas apuntan a la
# instalación de usuario (Programs\TuringLite). Tras desinstalar, "de_la_instalacion" debe ser 0.
$cu = [Microsoft.Win32.Registry]::CurrentUser
$release = 'Programs\\TuringLite|programsturinglite'
function ValueNames([string]$path, [bool]$byData) {
    $k = $cu.OpenSubKey($path); if ($null -eq $k) { return ,@() }
    try {
        $r = @()
        foreach ($n in $k.GetValueNames()) {
            $text = if ($byData) { "$n=" + [string]$k.GetValue($n) } else { $n }
            if ($text -match 'TuringLite') { $r += $text }
        }
        return ,$r
    } finally { $k.Close() }
}
function TileNames([string]$path) {
    $k = $cu.OpenSubKey($path); if ($null -eq $k) { return ,@() }
    $found = New-Object System.Collections.Generic.List[string]
    function Walk($key, $prefix, $depth) {
        if ($depth -gt 12) { return }
        foreach ($c in $key.GetSubKeyNames()) {
            $full = if ($prefix) { "$prefix\$c" } else { $c }
            if ($full -match 'TuringLite') { $found.Add($full); continue }
            $sub = $key.OpenSubKey($c)
            if ($sub) { try { Walk $sub $full ($depth + 1) } finally { $sub.Close() } }
        }
    }
    try { Walk $k "" 0 } finally { $k.Close() }
    return ,$found.ToArray()
}
function CloudNames {
    $store = 'Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount'
    $r = @()
    foreach ($sec in @('Cloud', 'Current')) {
        $k = $cu.OpenSubKey("$store\$sec"); if ($null -eq $k) { continue }
        try {
            foreach ($list in $k.GetSubKeyNames()) {
                if ($list -notmatch 'appmetadatalist|appleveltilelist') { continue }
                $l = $k.OpenSubKey($list); if ($null -eq $l) { continue }
                try { $r += @($l.GetSubKeyNames() | Where-Object { $_ -match 'turinglite' }) } finally { $l.Close() }
            }
        } finally { $k.Close() }
    }
    return ,$r
}
function TraceLine([string]$label, $names) {
    $all = @($names); $mine = @($all | Where-Object { $_ -match $release })
    Add "trazas_$label" ("total={0} de_la_instalacion={1}" -f $all.Count, $mine.Count)
}
TraceLine 'muicache' (ValueNames 'Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache' $false)
TraceLine 'historial_ejecucion' (ValueNames 'Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched' $false)
TraceLine 'compatibilidad' (ValueNames 'Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store' $false)
TraceLine 'acceso_directo_uso' (ValueNames 'Software\Microsoft\Windows\CurrentVersion\UFH\SHC' $true)
TraceLine 'mosaicos_inicio' (TileNames 'Software\Microsoft\Windows\CurrentVersion\Start\TileProperties')
TraceLine 'nube' (CloudNames)

$lines | Set-Content -Path $Out -Encoding utf8
Write-Host "Foto guardada en $Out ($($lines.Count) líneas)"
