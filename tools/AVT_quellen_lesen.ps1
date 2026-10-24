param(
    [string] $Arbeit = 'C:\Temp\AVT\Quellen',
    [string] $Vorschlag = 'C:\Temp\AVT\Stammdaten_Vorschlag'
)
# AVT 0.4.0 Teil 2: Quellen fuer den EHS-Bau lesen - nur an Kopien in C:\Temp, headless (/isolate). Nie waehrend eines MEP-Slots.
# 1) RI-9.1-Basis: Layouts, Rahmenformate, Layer, Saetze, Listen, Themen (AVTVORLAGELESEN) und Themenprobe (AVTTHEMENPROBE).
# 2) Rahmen-Baustein EHS_Rahmen aus RI 9.1 (AVTBAUSTEIN) nach <Vorschlag>\Bausteine\EHS_Rahmen.dwg.
# 3) DACH-Leervorlage (B1): dieselbe Auslesung.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
$schema = Join-Path $env:USERPROFILE 'Meine Ablage\Vorlagen\CAD\CADStandardEichlerGmbH\Schema\Versionen'

function Kopie([string] $quelle, [string] $name) {
    $ziel = Join-Path $Arbeit $name
    Copy-Item $quelle $ziel -Force
    (Get-Item $ziel).IsReadOnly = $false
    $ziel
}

$ri = Kopie (Join-Path $schema 'EHS_0.0_Basis_aus_RI_9.1_20261023.dwg') 'ri91.dwg'
$riText = Join-Path $Arbeit 'ri91_vorlage.txt'; $probe = Join-Path $Arbeit 'ri91_themenprobe.txt'
Lauf (Join-Path $Arbeit 'lauf_ri91') $ri $dll @('AVTVORLAGELESEN', (P $riText), 'AVTTHEMENPROBE', (P $probe)) @($riText, $probe)
PruefeEnde $riText 'AVTENDE ok'; PruefeEnde $probe 'AVTENDE ok'

$bausteine = Join-Path $Vorschlag 'Bausteine'
New-Item -ItemType Directory -Force $bausteine | Out-Null
$rahmen = Join-Path $bausteine 'EHS_Rahmen.dwg'
if (Test-Path $rahmen) { Remove-Item $rahmen -Force }
$ri2 = Kopie (Join-Path $schema 'EHS_0.0_Basis_aus_RI_9.1_20261023.dwg') 'ri91_baustein.dwg'
$bText = Join-Path $Arbeit 'baustein.txt'
Lauf (Join-Path $Arbeit 'lauf_baustein') $ri2 $dll @('AVTBAUSTEIN', (P $bText), (P $rahmen), 'Rocherahmen', 'EHS_Rahmen') @($bText)
PruefeEnde $bText 'AVTENDE ok'

$themen = Join-Path $bausteine 'EHS_Themen.dwg'
if (Test-Path $themen) { Remove-Item $themen -Force }
$ri3 = Kopie (Join-Path $schema 'EHS_0.0_Basis_aus_RI_9.1_20261023.dwg') 'ri91_themen.dwg'
$tText = Join-Path $Arbeit 'themenbaustein.txt'
Lauf (Join-Path $Arbeit 'lauf_themen') $ri3 $dll @('AVTTHEMENBAUSTEIN', (P $tText), (P $themen), 'Medien') @($tText)
PruefeEnde $tText 'AVTENDE ok'
Zeilen $tText | Where-Object { $_ -like 'AVTTHEMENBAUSTEIN*' }

$dach = Kopie 'C:\ProgramData\Autodesk\MEP 2026\deu\Template\Aecb Model (D A CH Ctb) mm.dwt' 'dach.dwg'
$dText = Join-Path $Arbeit 'dach_vorlage.txt'
Lauf (Join-Path $Arbeit 'lauf_dach') $dach $dll @('AVTVORLAGELESEN', (P $dText)) @($dText)
PruefeEnde $dText 'AVTENDE ok'

Zeilen $bText | Where-Object { $_ -like 'AVTBAUSTEIN*' }
Zeilen $probe | Where-Object { $_ -like 'AVTTHEMEN*' }
Zeilen $dText | Where-Object { $_ -match '^(LAYOUT|PAPIER)' }
