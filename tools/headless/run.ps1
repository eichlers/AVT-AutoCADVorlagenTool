param(
    [Parameter(Mandatory)] [string] $Zeichnung,
    [Parameter(Mandatory)] [AllowEmptyString()] [string[]] $Befehle,
    [int] $TimeoutSek = 900
)
# Fuehrt Befehle headless in AutoCAD MEP 2026 (accoreconsole) auf einer Zeichnungskopie aus (Herkunft: ASG tools/headless).
# Nur so starten (Start-Process, /isolate): ohne /isolate verstellt accoreconsole FILEDIA/SECURELOAD im Profil des Nutzers.
# Die Zeichnung muss eine KOPIE in einem Ordner unter C:\Temp ohne Leerzeichen sein. Sie wird nicht gespeichert (_QUIT _Y).
# Nie waehrend eines MEP-Slots starten (MEP startet sonst isoliert, Befund ADM 20.10.2026).
$ErrorActionPreference = 'Stop'
$exe = 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe'
$Zeichnung = [IO.Path]::GetFullPath($Zeichnung)
if ($Zeichnung -match '\s' -or -not $Zeichnung.StartsWith('C:\Temp\', [StringComparison]::OrdinalIgnoreCase)) { throw "Zeichnung muss unter C:\Temp liegen, ohne Leerzeichen: $Zeichnung" }
$dir = Split-Path $Zeichnung
$scr = Join-Path $dir 'lauf.scr'
$log = Join-Path $dir 'lauf.log'
# Jeder Lauf braucht einen neuen Profilordner (AutoCAD scheitert an einem vorhandenen).
Get-ChildItem $dir -Directory -Filter 'isoliert*' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch {} }
$isoliert = Join-Path $dir ('isoliert_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
$zuruecksetzen = @('(setvar "FILEDIA" 1)', '(setvar "SECURELOAD" 1)')

function Starte([string[]] $zeilen, [string] $skript, [string] $protokoll, [int] $sek) {
    [IO.File]::WriteAllText($skript, ($zeilen -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))
    $p = Start-Process -FilePath $exe `
        -ArgumentList @('/i', "`"$Zeichnung`"", '/s', "`"$skript`"", '/product', 'MEP', '/l', 'de-DE', '/isolate', 'AVT_Konsole', "`"$isoliert`"") `
        -WorkingDirectory $dir -RedirectStandardOutput $protokoll -NoNewWindow -PassThru
    if ($p.WaitForExit($sek * 1000)) { return $true }
    $p.Kill()
    return $false
}

$zeilen = @('(setvar "FILEDIA" 0)', '(setvar "SECURELOAD" 0)') + $Befehle + $zuruecksetzen + @('_QUIT _Y')
if (-not (Starte $zeilen $scr $log $TimeoutSek)) {
    'TIMEOUT - setze FILEDIA/SECURELOAD in eigenem Lauf auf 1 zurueck'
    $ok = Starte ($zuruecksetzen + @('_QUIT _Y')) (Join-Path $dir 'reset.scr') (Join-Path $dir 'reset.log') 120
    if ($ok) { 'FILEDIA/SECURELOAD = 1 wiederhergestellt' } else { 'ACHTUNG: Zuruecksetzen fehlgeschlagen - FILEDIA/SECURELOAD pruefen!' }
}
# Protokoll ist UTF-16 mit Nullbytes.
$text = (Get-Content $log -Raw) -replace "`0", ''
if ($text -match 'Failed to create registry keys') { 'ACHTUNG: /isolate fehlgeschlagen - Lauf nicht ausgefuehrt' }
$start = $text.IndexOf('_NETLOAD')
if ($start -ge 0) { $text.Substring($start) } else { $text }
