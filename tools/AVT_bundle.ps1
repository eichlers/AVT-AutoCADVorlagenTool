<#
  AVT_bundle.ps1 - baut AVT (Release) und erzeugt dist\AVT.bundle (AVT.Mep.dll + AVT.Kern.dll), dazu das Handbuch als PDF
  (gemeinsamer Erzeuger ..\AMK-AutoCADModulKoordination\tools\amk_handbuch_pdf.py).
  Mit -Installieren wird das Bundle nach %APPDATA%\Autodesk\ApplicationPlugins\AVT.bundle kopiert - nur bei geschlossenem
  MEP 2026 und nur nach der "frei"-Runde ueber AMK (gemeinsame Regel "Eine MEP-Instanz fuer alle").

  Aufruf (im Projektordner):  powershell -File tools\AVT_bundle.ps1 [-Installieren]
#>
param([switch]$Installieren)
$ErrorActionPreference = 'Stop'
$wurzel = Split-Path $PSScriptRoot -Parent

dotnet build (Join-Path $wurzel 'src\AVT.Mep\AVT.Mep.csproj') -c Release --nologo -v q -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

$bundle = Join-Path $wurzel 'dist\AVT.bundle'
$inhalt = Join-Path $bundle 'Contents'
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force $inhalt | Out-Null
Copy-Item (Join-Path $wurzel 'bundle\PackageContents.xml') $bundle
foreach ($dll in 'AVT.Mep.dll', 'AVT.Kern.dll') { Copy-Item (Join-Path $wurzel "src\AVT.Mep\bin\Release\$dll") $inhalt }
# Versionsnummer nur in AVT.Mep.csproj pflegen - hier aus der gebauten DLL ins Bundle uebernehmen.
$v = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $inhalt 'AVT.Mep.dll')).Version
$version = "$($v.Major).$($v.Minor).$($v.Build)"
$paket = Join-Path $bundle 'PackageContents.xml'
$xml = [IO.File]::ReadAllText($paket) -replace 'AppVersion="[^"]*"', "AppVersion=`"$version`"" -replace '(<ComponentEntry AppName="AVT" Version=)"[^"]*"', "`$1`"$version`""
[IO.File]::WriteAllText($paket, $xml, (New-Object Text.UTF8Encoding $false))
"Version $version"
"Bundle erzeugt: $bundle"

$docs = Join-Path $wurzel 'docs'
$erzeuger = Join-Path (Split-Path $wurzel -Parent) 'AMK-AutoCADModulKoordination\tools\amk_handbuch_pdf.py'
if (-not (Test-Path $erzeuger)) { throw "Handbuch-Erzeuger fehlt: $erzeuger" }
python $erzeuger AVT (Join-Path $docs 'AVT_Handbuch.md') (Join-Path $docs 'AVT_Handbuch.pdf')
if ($LASTEXITCODE -ne 0) { throw 'PDF-Handbuch nicht erzeugt.' }
foreach ($datei in 'AVT_Handbuch.pdf', 'AVT_Handbuch.md') { Copy-Item (Join-Path $docs $datei) (Join-Path $wurzel 'dist') -Force }

if ($Installieren) {
    $laeuft = Get-CimInstance Win32_Process -Filter "Name='acad.exe'" | Where-Object { $_.ExecutablePath -like '*AutoCAD 2026*' }
    if ($laeuft) { throw 'AutoCAD MEP 2026 laeuft noch - bitte erst schliessen (und vorher die frei-Runde ueber AMK).' }
    $ziel = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AVT.bundle'
    if (Test-Path $ziel) { Remove-Item $ziel -Recurse -Force }
    Copy-Item $bundle $ziel -Recurse
    "Installiert: $ziel"
}
