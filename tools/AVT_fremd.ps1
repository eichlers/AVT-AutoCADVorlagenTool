param(
    # Zeichnung (nur kopiert, nie veraendert).
    [Parameter(Mandatory)] [string] $Quelle,
    [Parameter(Mandatory)] [string] $Arbeit,
    # Alle Funde der Gruppe a entfernen, als ergebnis.dwg speichern und am Ergebnis nachpruefen.
    [switch] $Bereinigen,
    # Mit -Bereinigen: alle b-Funde als Ausnahme merken (wie "behalten" in der Palette).
    [switch] $AusnahmenMerken
)
# Fremdinhalte (AVT 0.3.0) ohne Oberflaeche - derselbe Kern wie Bereinigen in der Palette.
# lauf1: AVTFREMD (Vorschau a/b/c, vorschau.txt) [+ AVTFREMDBEREINIGEN -> bereinigen.txt, ergebnis.dwg]
# lauf2 (mit -Bereinigen): Ergebnis neu oeffnen, Vorschau (nachpruefung.txt), dann AUDIT nur melden.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
$vorschau = Join-Path $Arbeit 'vorschau.txt'; $bericht = Join-Path $Arbeit 'bereinigen.txt'; $ziel = Join-Path $Arbeit 'ergebnis.dwg'
$nach = Join-Path $Arbeit 'nachpruefung.txt'
$befehle = @('AVTFREMD', (P $vorschau))
if ($Bereinigen) { $befehle += @('AVTFREMDBEREINIGEN', (P $bericht), (P $ziel), $(if ($AusnahmenMerken) { 'j' } else { 'n' })) }
Lauf (Join-Path $Arbeit 'lauf1') $Quelle $dll $befehle @($vorschau, $bericht, $ziel)
PruefeEnde $vorschau 'AVTENDE ok'
Zeilen $vorschau | Where-Object { $_ -match '^AVTFREMD (Kopf|Kurz|Zahl)' }
if (-not $Bereinigen) { return }
PruefeEnde $bericht 'AVTENDE ok'
Zeilen $bericht | Where-Object { $_ -match '^AVTFREMD (Ergebnis|RegApps|FEHLER|nachher (Kurz|Zahl))' }
'Ausnahmen gemerkt: ' + @(Zeilen $bericht | Where-Object { $_ -match '^AVTFREMD Ausnahme gemerkt' }).Count
Lauf (Join-Path $Arbeit 'lauf2') $ziel $dll @('AVTFREMD', (P $nach), '_AUDIT', '_N') @($nach)
PruefeEnde $nach 'AVTENDE ok'
'Nachpruefung (neu geoeffnet): ' + ((Zeilen $nach | Where-Object { $_ -match '^AVTFREMD Kurz' }) -join ' ')
Zeilen $nach | Where-Object { $_ -match '^AVTFREMD Zahl' }
'AUDIT: ' + ((Get-Content (Join-Path $Arbeit 'lauf2\konsole.txt') | Where-Object { $_ -match 'Fehler gefunden' }) -join ' ')
