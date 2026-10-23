param(
    # Zeichnung, die geprueft wird (nur kopiert, nie veraendert).
    [Parameter(Mandatory)] [string] $Zeichnung,
    [Parameter(Mandatory)] [string] $Arbeit,
    # Inventur der Fassung (kanon.txt), gegen die verglichen wird; leer = nur Kopf (Stempel, DWGPROPS).
    [string] $Soll = ''
)
# Pruefen (AVT 0.2.0): AVTKANON an einer Kopie, dann Pruefbericht (tools\avt_pruefen.py). Nie waehrend eines MEP-Slots.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
$kanon = Join-Path $Arbeit 'kanon.txt'
Lauf (Join-Path $Arbeit 'lauf') $Zeichnung $dll @('_AUDIT', '_N', 'AVTKANON', (P $kanon), '-')
PruefeEnde $kanon 'AVTENDE ok'
$argumente = @('-I', '-X', 'utf8', (Join-Path $PSScriptRoot 'avt_pruefen.py'), $kanon)
if ($Soll) { $argumente += @('--soll', $Soll) }
& python @argumente
$rc = $LASTEXITCODE
'AUDIT: ' + ((Get-Content (Join-Path $Arbeit 'lauf\konsole.txt') | Where-Object { $_ -match 'Fehler gefunden' }) -join ' ')
exit $rc
