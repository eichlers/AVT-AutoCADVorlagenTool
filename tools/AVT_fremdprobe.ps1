param(
    # Grundlage der Probezeichnung (nur kopiert), z. B. die freigegebene EHS-Fassung.
    [Parameter(Mandatory)] [string] $Quelle,
    [Parameter(Mandatory)] [string] $Arbeit
)
# Nachweis zum Review 0.3.0: Probezeichnung mit Faellen anlegen, die nicht als "unbenutzt" gelten duerfen (AVTFREMDPROBE),
# dann mit tools\AVT_fremd.ps1 -Bereinigen pruefen. Erwartung: nur TAGProbe wird entfernt.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
$probe = Join-Path $Arbeit 'probe.dwg'; $bericht = Join-Path $Arbeit 'probe.txt'
Lauf (Join-Path $Arbeit 'anlegen') $Quelle $dll @('AVTFREMDPROBE', (P $bericht), (P $probe)) @($bericht, $probe)
PruefeEnde $bericht 'AVTENDE ok'
& (Join-Path $PSScriptRoot 'AVT_fremd.ps1') -Quelle $probe -Arbeit (Join-Path $Arbeit 'fremd') -Bereinigen
$v = Join-Path $Arbeit 'fremd\vorschau.txt'; $b = Join-Path $Arbeit 'fremd\bereinigen.txt'
'--- Vorschau der Probe-Eintraege'
Zeilen $v | Where-Object { $_ -match 'Probe' }
'--- Bereinigen'
Zeilen $b | Where-Object { $_ -match 'Probe|gew.hlt|Ergebnis|FEHLER' }
