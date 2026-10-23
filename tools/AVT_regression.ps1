param(
    # Freigegebene Fassung EHS 0.0 (nur gelesen).
    [string] $Quelle00 = (Join-Path $env:USERPROFILE 'Meine Ablage\Vorlagen\CAD\CADStandardEichlerGmbH\Schema\Versionen\EHS_0.0_Basis_aus_RI_9.1_20261023.dwg'),
    # Referenz-Inventuren von ASG (nachher.txt des Endstands).
    [string] $Referenz01 = 'C:\Temp\AMK_Vorlage\EHS_0.1\nachher.txt',
    [string] $Referenz02 = 'C:\Temp\AMK_Vorlage\EHS_0.2\zweiter\nachher.txt',
    # Fremd-Vorsilben fuer EHS 0.2 - nur beim Aufruf angeben, nie ins Repo.
    [Parameter(Mandatory)] [string[]] $FremdeWoerterbuecher02,
    [string[]] $Themen02 = @('ANA Rohr DN'),
    [int] $Laeufe = 3,
    [string] $Arbeit = 'C:\Temp\AVT\Regression'
)
# Regression AVT 0.1.0 gegen ASG tools/ehs (Auftrag AMK 23.10.2026): je Lauf EHS 0.0 -> 0.1 (ohne Schalter) und das eigene
# 0.1-Ergebnis -> 0.2 (Fremd-Vorsilben, Thema), Kennzahlen gegen die ASG-Referenz. Nur Kopien unter C:\Temp.
$ErrorActionPreference = 'Stop'
$skript = Join-Path $PSScriptRoot 'AVT_bereinigen.ps1'
$bericht = Join-Path $PSScriptRoot 'avt_bericht.py'
$stempel = Get-Date -Format 'yyyyMMdd_HHmm'
$zusammen = @()
for ($i = 1; $i -le $Laeufe; $i++) {
    $basis = Join-Path $Arbeit "$stempel`_lauf$i"
    $o1 = Join-Path $basis 'EHS_0.1'; $o2 = Join-Path $basis 'EHS_0.2'
    New-Item -ItemType Directory -Force $basis | Out-Null
    & $skript -Quelle $Quelle00 -Arbeit $o1 -Ergebnis 'EHS_0.1_leer.dwg' | Out-File -Encoding utf8 (Join-Path $basis 'ablauf_0.1.txt')
    python -I -X utf8 $bericht $o1 --referenz $Referenz01
    $r1 = $LASTEXITCODE
    $e01 = if (Test-Path (Join-Path $o1 'zweiter\EHS_0.1_leer.dwg')) { Join-Path $o1 'zweiter\EHS_0.1_leer.dwg' } else { Join-Path $o1 'EHS_0.1_leer.dwg' }
    & $skript -Quelle $e01 -Arbeit $o2 -Ergebnis 'EHS_0.2.dwg' -FremdeWoerterbuecher $FremdeWoerterbuecher02 -Themen $Themen02 | Out-File -Encoding utf8 (Join-Path $basis 'ablauf_0.2.txt')
    python -I -X utf8 $bericht $o2 --referenz $Referenz02
    $r2 = $LASTEXITCODE
    $zusammen += "Lauf $i | EHS 0.1 $(if ($r1) { 'ABWEICHUNG' } else { 'gleich' }) | EHS 0.2 $(if ($r2) { 'ABWEICHUNG' } else { 'gleich' }) | $basis"
}
$zusammen | Out-File -Encoding utf8 (Join-Path $Arbeit "$stempel`_zusammenfassung.txt")
$zusammen
