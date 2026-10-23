param(
    # Autodesk-Leervorlage (nur gelesen). Vorschlag DACH mm - Freigabe B1 beim Nutzer.
    [string] $Basis = 'C:\ProgramData\Autodesk\MEP 2026\deu\Template\Aecb Model (D A CH Ctb) mm.dwt',
    [string] $Arbeit = 'C:\Temp\AVT\Erzeugen',
    [string] $Fassung = '1.0.0-e1',
    [string] $Vorgaenger = 'EHS 0.2 (Entwurf, nur Beleg)',
    # Stammdaten (Entwurf im AVT-Projekt) und Systeme-Tabelle.
    [string] $Stammdaten = '',
    [string] $Systeme = '',
    # Ein zweiter, unabhaengiger Bau zum Vergleich der Inhaltspruefsumme.
    [switch] $Zweimal
)
# AVT 0.4.0 Teil 1 "Erzeugen": Entwurf <Kuerzel> <Fassung> aus Stammdaten auf einer Kopie der Leervorlage, dann am Ergebnis
# Inhaltsauszug, Pruefen gegen die Stammdaten und AUDIT (nur melden). Nur C:\Temp, nichts ins Schema-Repo. Nie waehrend eines MEP-Slots.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
$wurzel = Split-Path $PSScriptRoot -Parent
if (-not $Stammdaten) { $Stammdaten = Join-Path $wurzel 'stammdaten_entwurf' }
if (-not $Systeme) { $Systeme = Join-Path $Stammdaten 'Systeme.csv' }
if (-not (Test-Path $Basis)) { throw "Basis fehlt: $Basis" }
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
# Herkunft ohne Datum: Stand der Stammdaten = Git-Commit des AVT-Projekts (Warnung, wenn ungesichert), Basis mit Pruefsumme.
$commit = (& git -C $wurzel rev-parse --short HEAD).Trim()
$offen = (& git -C $wurzel status --porcelain -- stammdaten_entwurf) -ne $null
$stand = "AVT $commit" + $(if ($offen) { ' (ungesichert!)' } else { '' })
$basisText = "$(Split-Path $Basis -Leaf) SHA256 $((Get-FileHash $Basis -Algorithm SHA256).Hash.Substring(0, 16))"
$version = [Reflection.AssemblyName]::GetAssemblyName(($dll -replace '/', '\')).Version
$avt = "$($version.Major).$($version.Minor).$($version.Build)"

function Bau([string] $ordner) {
    New-Item -ItemType Directory -Force $ordner | Out-Null
    $ziel = Join-Path $ordner "EHS_$Fassung.dwg"; $bericht = Join-Path $ordner 'erzeugen.txt'
    Lauf (Join-Path $ordner 'lauf1') $Basis $dll @('AVTERZEUGEN', (P $bericht), (P $ziel), $Stammdaten, $Systeme, $Fassung, $Vorgaenger, $stand, $basisText, $avt) @($bericht, $ziel)
    PruefeEnde $bericht 'AVTENDE ok'
    $kanon = Join-Path $ordner 'kanon.txt'; $stamm = Join-Path $ordner 'stammpruefung.txt'
    Lauf (Join-Path $ordner 'lauf2') $ziel $dll @('AVTKANON', (P $kanon), '-', 'AVTSTAMMPRUEFEN', (P $stamm), $Stammdaten, $Systeme, '_AUDIT', '_N') @($kanon, $stamm)
    PruefeEnde $kanon 'AVTENDE ok'; PruefeEnde $stamm 'AVTENDE ok'
    $gesamt = ((Zeilen $kanon | Where-Object { $_ -like 'AVTKANON Gesamt |*' }) -replace '^AVTKANON Gesamt \| ', '')
    [pscustomobject]@{ Ordner = $ordner; Ziel = $ziel; Gesamt = $gesamt
        Audit = ((Get-Content (Join-Path $ordner 'lauf2\konsole.txt') | Where-Object { $_ -match 'Fehler gefunden' }) -join ' ') }
}

"Basis: $basisText · Stammdaten: $stand · AVT $avt · Fassung $Fassung"
$b1 = Bau (Join-Path $Arbeit 'bau1')
Zeilen (Join-Path $b1.Ordner 'erzeugen.txt') | Where-Object { $_ -match '^AVTERZEUGEN (Basis \||Basis entfernt|Systeme|Inhalt|gespeichert)' }
Zeilen (Join-Path $b1.Ordner 'erzeugen.txt') | Where-Object { $_ -match '^AVTERZEUGEN Textstil der Basis' } | Select-Object -First 8
Zeilen (Join-Path $b1.Ordner 'stammpruefung.txt') | Where-Object { $_ -match '^AVTSTAMM (FEHLER|Ergebnis)' } | Select-Object -First 25
"Inhaltspruefsumme Bau 1: $($b1.Gesamt) · AUDIT: $($b1.Audit)"
if ($Zweimal) {
    $b2 = Bau (Join-Path $Arbeit 'bau2')
    "Inhaltspruefsumme Bau 2: $($b2.Gesamt) · AUDIT: $($b2.Audit)"
    "Gleiche Stammdaten -> gleiche Vorlage: $(if ($b1.Gesamt -eq $b2.Gesamt) { 'JA' } else { 'NEIN - Unterschied siehe kanon.txt' })"
}
