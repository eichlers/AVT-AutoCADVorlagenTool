param(
    # Zeichnungen (werden nur kopiert, nie veraendert). Je Zeichnung ein Unterordner kanon_<n>.
    [Parameter(Mandatory)] [string[]] $Zeichnungen,
    # Arbeitsordner: unter C:\Temp, ohne Leerzeichen.
    [Parameter(Mandatory)] [string] $Arbeit,
    # Pfad-Praefix fuer Objekte, deren ganzer Inhalt mit ausgegeben wird (Fehlersuche); leer = keiner.
    [string] $Detail = '',
    # Vorher AUDIT (nur melden), Ergebnis in pruefsummen.txt.
    [switch] $Audit
)
# Kanonische Inventur (AVT 0.2.0): je Zeichnung AVTKANON auf einer Kopie -> kanon.txt, dazu Liste der Gesamtpruefsummen.
# Nie waehrend eines MEP-Slots starten.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avt_gemeinsam.ps1')
# PowerShell -File uebergibt Arrays als einen Text mit Kommas.
$Zeichnungen = @($Zeichnungen | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Arbeit = Arbeitsordner $Arbeit
$dll = Baue $Arbeit
$detailText = if ($Detail) { $Detail } else { '-' }
$ergebnis = @()
$n = 0
foreach ($z in $Zeichnungen) {
    $n++
    $ordner = Join-Path $Arbeit "kanon_$n"
    $aus = Join-Path $ordner 'kanon.txt'
    $vorweg = if ($Audit) { @('_AUDIT', '_N') } else { @() }
    # Inventur vor AUDIT: AUDIT berechnet Schraffuren neu, auch mit _N (Befund 23.10.2026).
    Lauf $ordner $z $dll (@('AVTKANON', (P $aus), $detailText) + $vorweg)
    PruefeEnde $aus 'AVTENDE ok'
    $gesamt = (Zeilen $aus | Where-Object { $_ -like 'AVTKANON Gesamt |*' }) -replace '^AVTKANON Gesamt \| ', ''
    $objekte = (Zeilen $aus | Where-Object { $_ -like 'AVTKANON Objekte |*' }) -replace '^AVTKANON Objekte \| ', ''
    $auditText = if ($Audit) { ' | AUDIT ' + ((Get-Content (Join-Path $ordner 'konsole.txt') | Where-Object { $_ -match 'Fehler gefunden' }) -join ' ') } else { '' }
    $ergebnis += "$gesamt | $objekte Objekte$auditText | $z"
}
$ergebnis | Out-File -Encoding utf8 (Join-Path $Arbeit 'pruefsummen.txt')
$ergebnis
