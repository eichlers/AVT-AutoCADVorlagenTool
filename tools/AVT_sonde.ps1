param(
    # Zeichnung oder Vorlage, an deren Kopie die Sonde anlegt (nur gelesen).
    [Parameter(Mandatory)] [string] $Quelle,
    [Parameter(Mandatory)] [string] $Arbeit
)
# API-Sonde (AVT 0.2.0): lauf1 legt je Objekttyp an und speichert (sonde.txt, sonde.dwg),
# lauf2 oeffnet das Ergebnis, AUDIT nur melden, liest zurueck (lesen.txt) und macht die kanonische Inventur (kanon.txt).
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
$ziel = Join-Path $Arbeit 'sonde.dwg'
if (Test-Path $ziel) { Remove-Item $ziel -Force }
Lauf (Join-Path $Arbeit 'lauf1') $Quelle $dll @('AVTSONDE', (P (Join-Path $Arbeit 'sonde.txt')), (P $ziel))
PruefeEnde (Join-Path $Arbeit 'sonde.txt') 'AVTENDE ok'
Lauf (Join-Path $Arbeit 'lauf2') $ziel $dll @('_AUDIT', '_N', 'AVTSONDELESEN', (P (Join-Path $Arbeit 'lesen.txt')),
    'AVTKANON', (P (Join-Path $Arbeit 'kanon.txt')), 'AVT_Sonde')
PruefeEnde (Join-Path $Arbeit 'lesen.txt') 'AVTENDE ok'
Zeilen (Join-Path $Arbeit 'sonde.txt')
Zeilen (Join-Path $Arbeit 'lesen.txt')
(Get-Content (Join-Path $Arbeit 'lauf2\konsole.txt') | Where-Object { $_ -match 'Fehler gefunden' }) -join ' '
