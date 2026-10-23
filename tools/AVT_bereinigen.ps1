param(
    # Quelle wird nur gelesen (kopiert), nie veraendert.
    [Parameter(Mandatory)] [string] $Quelle,
    # Arbeitsordner: unter C:\Temp, ohne Leerzeichen.
    [Parameter(Mandatory)] [string] $Arbeit,
    [string] $Ergebnis = 'ergebnis.dwg',
    # Vorsilben fremder Woerterbucheintraege (Gross/klein zaehlt), nur beim Aufruf - nie Produktnamen ins Repo.
    [string[]] $FremdeWoerterbuecher = @(),
    # Genaue Namen von Anzeigethemen, die entfernt werden.
    [string[]] $Themen = @(),
    # Nur Inventur und Entfernplan, nichts speichern.
    [switch] $Trocken,
    # Keinen zweiten Durchgang, auch wenn die Nachpruefung neu angelegte Spuren findet (nur melden).
    [switch] $OhneZweitenDurchgang
)
# AVT 0.1.0 Bereinigen (Herkunft ASG tools/ehs 095faa6 + e9f502d, Auftrag AMK 23.10.2026).
# Durchgang: Kopie oeffnen -> AVTINVENTUR (vorher.txt) -> AVTBEREINIGEN (plan.txt, Ergebnis per SaveAs)
#            -> Ergebnis oeffnen, AUDIT nur melden, AVTINVENTUR (nachher.txt).
# Nachpruefung: Hat ein Object-Enabler beim Speichern Woerterbuecher/RegApps neu angelegt (Befund ASG, Fremd-Add-on)?
#            -> nachpruefung.txt; bei Befund automatisch ein zweiter Durchgang am Ergebnis (Ordner zweiter\), erneut pruefen.
# Nichts im Schema-Ordner, keine Aenderung an Profil oder Bundles. Nie waehrend eines MEP-Slots starten.
$ErrorActionPreference = 'Stop'
$Arbeit = [IO.Path]::GetFullPath($Arbeit).TrimEnd('\')
if ($Arbeit -match '\s' -or -not $Arbeit.StartsWith('C:\Temp\', [StringComparison]::OrdinalIgnoreCase)) { throw "Arbeitsordner muss unter C:\Temp liegen und ohne Leerzeichen sein: $Arbeit" }
if ($Ergebnis -match '[\\/]' -or [IO.Path]::GetExtension($Ergebnis) -ne '.dwg' -or $Ergebnis -match '\s') { throw "Ergebnis muss ein Dateiname .dwg ohne Pfad und Leerzeichen sein: $Ergebnis" }
if (-not (Test-Path $Quelle)) { throw "Quelle fehlt: $Quelle" }
# PowerShell -File uebergibt Arrays als einen Text mit Kommas.
$FremdeWoerterbuecher = @($FremdeWoerterbuecher | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$run = Join-Path $PSScriptRoot 'headless\run.ps1'
$projekt = Join-Path $PSScriptRoot '..\src\AVT.Werkzeug\AVT.Werkzeug.csproj'
New-Item -ItemType Directory -Force $Arbeit | Out-Null

$werkzeug = Join-Path $Arbeit 'werkzeug'
$build = Join-Path $Arbeit 'build.txt'
dotnet build $projekt -c Release "-p:Ausgabe=$werkzeug\" -v q -nologo | Out-File -Encoding utf8 $build
if ($LASTEXITCODE -ne 0) { throw "Build von AVT.Werkzeug fehlgeschlagen, siehe $build" }
$dll = (Join-Path $werkzeug 'AVT.Werkzeug.dll') -replace '\\', '/'
function P([string] $pfad) { $pfad -replace '\\', '/' }
$fremd = if ($FremdeWoerterbuecher.Count -gt 0) { $FremdeWoerterbuecher -join ',' } else { '-' }
$themenWeg = if ($Themen.Count -gt 0) { $Themen -join ';' } else { '-' }

function Lauf([string] $ordner, [string] $dwg, [string[]] $befehle) {
    New-Item -ItemType Directory -Force $ordner | Out-Null
    $kopie = Join-Path $ordner 'arbeit.dwg'
    Copy-Item $dwg $kopie -Force
    (Get-Item $kopie).IsReadOnly = $false
    $konsole = Join-Path $ordner 'konsole.txt'
    & $run -Zeichnung $kopie -Befehle (@("_NETLOAD $dll") + $befehle) | Out-File -Encoding utf8 $konsole
    if ((Get-Content $konsole -Raw) -match 'TIMEOUT|ACHTUNG|AVT abgelehnt') { throw "Lauf in $ordner nicht sauber (siehe $konsole)" }
}

function Zeilen([string] $datei) { [IO.File]::ReadAllLines($datei, [Text.Encoding]::UTF8) }

function PruefeEnde([string] $datei) {
    if (-not (Test-Path $datei) -or -not ((Zeilen $datei) -contains 'AVTENDE ok')) { throw "$datei nicht mit AVTENDE ok beendet" }
}

# Ein Durchgang: Quelle -> Ergebnis im Ordner $ziel, mit vorher/plan/nachher.
function Durchgang([string] $ordner, [string] $quelle) {
    New-Item -ItemType Directory -Force $ordner | Out-Null
    # Alte Ausgaben loeschen, damit nie ein frueheres Ergebnis als neues gilt (Review 23.10.2026).
    foreach ($a in 'vorher.txt', 'plan.txt', 'nachher.txt', 'nachpruefung.txt') { $f = Join-Path $ordner $a; if (Test-Path $f) { Remove-Item $f -Force } }
    $ziel = Join-Path $ordner $Ergebnis
    $schritte = @('AVTINVENTUR', (P (Join-Path $ordner 'vorher.txt')), $fremd, $themenWeg)
    if (-not $Trocken) {
        if (Test-Path $ziel) { Remove-Item $ziel -Force }
        $schritte += @('AVTBEREINIGEN', (P (Join-Path $ordner 'plan.txt')), (P $ziel), $fremd, $themenWeg)
    }
    Lauf (Join-Path $ordner 'lauf1') $quelle $schritte
    PruefeEnde (Join-Path $ordner 'vorher.txt')
    if ($Trocken) { return $null }
    PruefeEnde (Join-Path $ordner 'plan.txt')
    if (-not (Test-Path $ziel)) { throw "Ergebnis fehlt: $ziel (siehe lauf1\konsole.txt und plan.txt)" }
    Lauf (Join-Path $ordner 'lauf2') $ziel @('_AUDIT', '_N', 'AVTINVENTUR', (P (Join-Path $ordner 'nachher.txt')), $fremd, $themenWeg)
    PruefeEnde (Join-Path $ordner 'nachher.txt')
    return $ziel
}

function Namen([string] $datei, [string] $art) {
    $z = Zeilen $datei | Where-Object { $_ -like "AVTNAMEN $art |*" } | Select-Object -First 1
    if (-not $z) { return @() }
    @(($z -split '\|')[1] -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
function NodSchluessel([string] $datei) {
    @(Zeilen $datei | Where-Object { $_ -like 'AVTNOD *' } | ForEach-Object { ($_.Substring(7) -split '\|')[0].Trim() })
}
function PlanZahl([string] $datei, [string] $grund) {
    $z = Zeilen $datei | Where-Object { $_ -like "AVTPLANZAHL $grund |*" } | Select-Object -First 1
    if ($z) { [int](($z -split '\|')[1].Trim()) } else { 0 }
}

# Nachpruefung: neu angelegte NOD-Eintraege und RegApps (nachher, aber nicht vorher) und Restspuren, die der Plan am
# Ergebnis wieder entfernen wuerde. Nur melden; Namen bleiben in C:\Temp (nie ins Repo).
function Nachpruefung([string] $ordner) {
    $vor = Join-Path $ordner 'vorher.txt'; $nach = Join-Path $ordner 'nachher.txt'
    $neuNod = @(NodSchluessel $nach | Where-Object { (NodSchluessel $vor) -notcontains $_ })
    $neuApps = @(Namen $nach 'RegApps' | Where-Object { (Namen $vor 'RegApps') -notcontains $_ })
    $rest = [ordered]@{}
    foreach ($g in 'Fremd-Spur W?rterbuch', 'Modul-Spur W?rterbuch', 'Modul-Spur RegApp', 'Anzeigethema auf Auftrag') { $rest[$g] = PlanZahl $nach $g }
    $aus = @("AVTNACH neue NOD-Eintraege | $($neuNod.Count) | $($neuNod -join '; ')",
             "AVTNACH neue RegApps | $($neuApps.Count) | $($neuApps -join '; ')")
    foreach ($k in $rest.Keys) { $aus += "AVTNACH Rest $k | $($rest[$k])" }
    $befund = $neuNod.Count -gt 0 -or ($rest.Values | Measure-Object -Sum).Sum -gt 0
    $aus += "AVTNACH Befund | $(if ($befund) { 'ja' } else { 'nein' })"
    [IO.File]::WriteAllLines((Join-Path $ordner 'nachpruefung.txt'), [string[]] $aus, (New-Object Text.UTF8Encoding $false))
    $aus
    return $befund
}

$ziel = Durchgang $Arbeit $Quelle
if ($Trocken) { 'Trockenlauf fertig: vorher.txt'; return }
$ergebnisse = Nachpruefung $Arbeit
$befund = $ergebnisse[-1]
$ergebnisse[0..($ergebnisse.Count - 2)]
if ($befund) {
    if ($OhneZweitenDurchgang) { "NACHPRUEFUNG: Spuren neu angelegt oder uebrig - kein zweiter Durchgang (Schalter). Ergebnis: $ziel"; return }
    'NACHPRUEFUNG: Spuren neu angelegt oder uebrig - zweiter Durchgang am Ergebnis'
    $zweiter = Join-Path $Arbeit 'zweiter'
    $ziel = Durchgang $zweiter $ziel
    $e2 = Nachpruefung $zweiter
    $e2[0..($e2.Count - 2)]
    if ($e2[-1]) { "ACHTUNG: auch nach dem zweiten Durchgang Spuren - siehe $zweiter\nachpruefung.txt" }
}
"Fertig: $ziel"
