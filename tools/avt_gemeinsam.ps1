# Gemeinsame Bausteine der AVT-Ablaufskripte (per Dot-Sourcing). Nur Kopien unter C:\Temp, Start ueber headless\run.ps1.

function Arbeitsordner([string] $pfad) {
    $voll = [IO.Path]::GetFullPath($pfad).TrimEnd('\')
    if ($voll -match '\s' -or -not $voll.StartsWith('C:\Temp\', [StringComparison]::OrdinalIgnoreCase)) { throw "Arbeitsordner muss unter C:\Temp liegen und ohne Leerzeichen sein: $voll" }
    New-Item -ItemType Directory -Force $voll | Out-Null
    $voll
}

# Baut AVT.Werkzeug in <Arbeit>\werkzeug und liefert den DLL-Pfad (mit /).
function Baue([string] $arbeit) {
    $werkzeug = Join-Path $arbeit 'werkzeug'
    $build = Join-Path $arbeit 'build.txt'
    $projekt = Join-Path $PSScriptRoot '..\src\AVT.Werkzeug\AVT.Werkzeug.csproj'
    dotnet build $projekt -c Release "-p:Ausgabe=$werkzeug\" -v q -nologo | Out-File -Encoding utf8 $build
    if ($LASTEXITCODE -ne 0) { throw "Build von AVT.Werkzeug fehlgeschlagen, siehe $build" }
    (Join-Path $werkzeug 'AVT.Werkzeug.dll') -replace '\\', '/'
}

function P([string] $pfad) { $pfad -replace '\\', '/' }

# Kopie der Zeichnung in <ordner>\arbeit.dwg, Befehle headless ausfuehren, Konsole nach konsole.txt.
# $ausgaben: Dateien, die der Lauf neu schreibt - vorher geloescht, damit nie ein altes Ergebnis als neues gilt (Review).
# Bricht ab bei Zeitueberschreitung, fehlgeschlagenem /isolate oder abgelehntem Befehl.
function Lauf([string] $ordner, [string] $dwg, [string] $dll, [string[]] $befehle, [string[]] $ausgaben = @(), [int] $TimeoutSek = 600) {
    if (-not (Test-Path $dwg)) { throw "Zeichnung fehlt: $dwg" }
    New-Item -ItemType Directory -Force $ordner | Out-Null
    foreach ($a in $ausgaben) { if (Test-Path $a) { Remove-Item $a -Force } }
    $kopie = Join-Path $ordner 'arbeit.dwg'
    Copy-Item $dwg $kopie -Force
    (Get-Item $kopie).IsReadOnly = $false
    $konsole = Join-Path $ordner 'konsole.txt'
    & (Join-Path $PSScriptRoot 'headless\run.ps1') -Zeichnung $kopie -TimeoutSek $TimeoutSek -Befehle (@("_NETLOAD $dll") + $befehle) | Out-File -Encoding utf8 $konsole
    $text = Get-Content $konsole -Raw
    if ($text -match 'TIMEOUT|ACHTUNG|AVT abgelehnt') { throw "Lauf in $ordner nicht sauber (siehe $konsole)" }
}

function Zeilen([string] $datei) { [IO.File]::ReadAllLines($datei, [Text.Encoding]::UTF8) }

function PruefeEnde([string] $datei, [string] $ende) {
    if (-not (Test-Path $datei) -or -not ((Zeilen $datei) -contains $ende)) { throw "$datei nicht mit '$ende' beendet" }
}
