# AVT 0.3.x - unbeaufsichtigter Test an der vom NUTZER gestarteten MEP-2026-Instanz (per COM angehaengt).
# Nur nach "Slot: AVT" von AMK starten. Vorbild ANA_Anhaengen.ps1 / ADM_testhelfer.ps1.
# Prueft (Ergebnis je Punkt mit Beleg, Bilder vom MEP-Fenster):
#   1  Werkzeugkasten "AVT": genau ein Knopf, Makro AVTPALETTE, Tooltip; Symbol (Bild)
#   2  Knopf-Befehl AVTPALETTE: Palette sichtbar und ausgeklappt (UI-Automation: Groesse), Symbol (Bild), Protokoll
#   4  Hinweis beim Oeffnen einer Kopie von RI 9.1: Zeile im AVT-Protokoll, Dauer, kein Dialog
#   5  Bereinigen ... an dieser Kopie: Vorschau a/b/c, Ausgewaehlte bereinigen, AVT-Rueckfrage (eigener Dialog "AVT - Bereinigen")
#      mit Ja, Sicherungskopie daneben (Pruefsumme = Original), Bericht, Zeichnung NICHT gespeichert (Datei unveraendert)
#   6  Zahnrad: Fenster "AVT - Einstellungen und Version" (Version, Stand, Copyright, Historie), Bild, Fenster schliessen
#   S  Sonde-Thema: Kopie der Sonde-Zeichnung oeffnen, Bild - das Anwenden eines Anzeigethemas geht per API nicht (ANA-Befund),
#      darum Handpunkt mit Bild als Vorbereitung
#   3  Neustart (Palette bleibt zu): Handpunkt - MEP wird nie vom Skript beendet/gestartet
# Regeln: nur Testkopien in C:\Temp\AVT_MEP; nie eine Zeichnung des Nutzers aktivieren oder speichern (Befehle nur, wenn die
# eigene Kopie aktiv ist); AutoCAD nie starten/beenden; MEP 2025 nie anfassen; vor jedem Schritt GetLastInputInfo - bei Eingabe
# sofort aufhoeren (eigene Kopien ohne Speichern schliessen, Exitcode 2); fremde Dialoge erkennen, nie beantworten (Abbruch);
# Profil (Registry R25.1) und AVT-Einstellungen vorher sichern, danach vergleichen - nie zurueckspielen.
# Klicks nur ueber UI-Automation (keine echte Maus/Tastatur).
# Aufruf:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\mep\AVT_mep_test.ps1             (nur pruefen, ob anhaengbar)
#          powershell -NoProfile -ExecutionPolicy Bypass -File tools\mep\AVT_mep_test.ps1 -Ausfuehren (Test fahren)
param([switch] $Ausfuehren, [string] $Ordner = 'C:\Temp\AVT_MEP')

$ErrorActionPreference = 'Stop'
$schema = Join-Path $env:USERPROFILE 'Meine Ablage\Vorlagen\CAD\CADStandardEichlerGmbH\Schema\Versionen'
$quelleRi91 = Join-Path $schema 'EHS_0.0_Basis_aus_RI_9.1_20261023.dwg'
$quelleSonde = 'C:\Temp\AVT\Sonde_EHS02_final\sonde.dwg'
$protokoll = Join-Path $env:APPDATA 'AVT\AVT_sitzung.log'
$einstellungen = Join-Path $env:APPDATA 'AVT\AVT_einstellungen.json'
$ergebnis = [System.Collections.Generic.List[string]]::new()
function Notiere([string] $zeile) { $z = "$(Get-Date -Format 'HH:mm:ss') $zeile"; $ergebnis.Add($z); $z }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace AvtTest -Name Eingabe -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
[DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
public static uint Letzte() { var i = new LASTINPUTINFO(); i.cbSize = (uint)Marshal.SizeOf(i); GetLastInputInfo(ref i); return i.dwTime; }
[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
[DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public static IntPtr Klick(IntPtr knopf) { return SendMessage(knopf, 0x00F5, IntPtr.Zero, IntPtr.Zero); }   // BM_CLICK, keine echte Eingabe
public struct RECT { public int L, T, R, B; }
public static void Foto(IntPtr h, string pfad) {
  RECT r; GetWindowRect(h, out r);
  using (var b = new System.Drawing.Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T))) {
    using (var g = System.Drawing.Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
    b.Save(pfad);
  }
}
'@ -ReferencedAssemblies System.Drawing
$A = [Windows.Automation.AutomationElement]; $Baum = [Windows.Automation.TreeScope]

$script:eingabeStart = $null
$script:eigene = [System.Collections.Generic.List[object]]::new()   # eigene offene Testkopien (COM-Dokumente)
$script:ergebnisDatei = $null

# Knopf in einem Win32-Dialog (MessageBox): UI Automation meldet Ja/Nein als "Pane" ohne Invoke (Befund 24.10. 17:26) -
# darum nach Namen suchen und per BM_CLICK an den Fenstergriff druecken.
function DialogKnopf($dialog, [string[]] $namen) {
    foreach ($n in $namen) {
        $k = $dialog.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $n)))
        if ($k -and $k.Current.NativeWindowHandle -ne 0) { [void][AvtTest.Eingabe]::Klick([IntPtr]$k.Current.NativeWindowHandle); return $true }
    }
    $false
}
# Bei jedem Ende: eine noch offene eigene Rueckfrage mit "Nein" schliessen (nichts wird geaendert), erst dann Kopien schliessen.
function OffeneRueckfrageSchliessen {
    try {
        foreach ($w in Fenster) {
            foreach ($n in 'AVT – Bereinigen', 'AVT - Bereinigen') {
                $d = $w.FindFirst([Windows.Automation.TreeScope]::Element -bor [Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $n)))
                if ($d -and (DialogKnopf $d @('Nein', 'No'))) { Notiere 'Offene Rueckfrage "AVT – Bereinigen" mit Nein geschlossen (nichts geaendert).' | Out-Null; Start-Sleep 1 }
            }
        }
    } catch { }
}
function Schluss([int] $code) {
    OffeneRueckfrageSchliessen
    foreach ($d in $script:eigene) { try { $d.Close($false) } catch { } }
    if ($script:eigene.Count -gt 0) { Notiere "Eigene Testkopien ohne Speichern geschlossen ($($script:eigene.Count))." | Out-Null }
    if ($script:ergebnisDatei) { $ergebnis | Set-Content $script:ergebnisDatei -Encoding UTF8 }
    exit $code
}
function PruefeEingabe {
    if ($null -eq $script:eingabeStart -or [AvtTest.Eingabe]::Letzte() -eq $script:eingabeStart) { return }
    Notiere 'ABGEBROCHEN: Eingabe am PC erkannt - Test sofort beendet.' | Out-Null
    Schluss 2
}
function Warte([double] $sek) { for ($i = 0; $i -lt [Math]::Ceiling($sek * 4); $i++) { PruefeEingabe; Start-Sleep -Milliseconds 250 }; PruefeEingabe }
function Com([scriptblock] $aufruf, [int] $sek = 15) {
    for ($i = 0; $i -lt $sek * 2; $i++) {
        PruefeEingabe
        try { return & $aufruf } catch [System.Runtime.InteropServices.COMException] { Warte 0.5 }
    }
    throw "AutoCAD antwortet nicht (COM $sek s lang abgelehnt)"
}

# --- UI-Automation im MEP-Prozess -------------------------------------------------------------------------------------
function Fenster { $A::RootElement.FindAll($Baum::Children, (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:mepPid))) }
function Finde([string] $name, $wurzel = $null, [Windows.Automation.ControlType] $typ = $null) {
    $c = New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)
    if ($typ) { $c = New-Object Windows.Automation.AndCondition($c, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, $typ))) }
    $orte = if ($wurzel) { @($wurzel) } else { Fenster }
    # Element = Fenster selbst (z. B. Rueckfrage als eigenes Hauptfenster, Befund 24.10. 17:29) oder darin.
    foreach ($w in $orte) { $e = $w.FindFirst($Baum::Element -bor $Baum::Descendants, $c); if ($e) { return $e } }
    $null
}
function Druecke($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
# Erlaubte Fenster: MEP-Hauptfenster, AVT-Palette, eigene AVT-Fenster. Alles andere Modale ist ein fremder Dialog -> Abbruch.
$script:erlaubt = @('AVT', 'AVT - Einstellungen und Version', 'AVT – Einstellungen und Version', 'AVT – Bereinigen', 'AVT - Bereinigen')
function FremderDialog {
    foreach ($w in Fenster) {
        $n = $w.Current.Name
        if ($w.Current.NativeWindowHandle -eq $script:haupt) { continue }
        if ($script:erlaubt -contains $n) { continue }
        if ($w.Current.ClassName -eq '#32770' -or ($w.GetCurrentPropertyValue($A::IsWindowPatternAvailableProperty) -and
            $w.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Current.IsModal)) { return "$n [$($w.Current.ClassName)]" }
    }
    $null
}
function KeinDialog([string] $wo) {
    $d = FremderDialog
    if ($d) { Notiere "ABGEBROCHEN bei ${wo}: fremder Dialog offen ($d) - wird nicht beantwortet." | Out-Null; Foto "dialog_$wo" | Out-Null; Schluss 3 }
}
function Foto([string] $name) { $p = Join-Path $script:lauf "$name.png"; try { [AvtTest.Eingabe]::Foto([IntPtr]$script:haupt, $p) } catch { }; $p }
# Ganzer Bildschirm (schwebende Palette ist ein eigenes Fenster und fehlt auf dem Foto des Hauptfensters).
function Schirmfoto([string] $name) {
    $p = Join-Path $script:lauf "$name.png"
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $s = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $b = New-Object System.Drawing.Bitmap $s.Width, $s.Height
        $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($s.Left, $s.Top, 0, 0, $b.Size); $g.Dispose()
        $b.Save($p); $b.Dispose()
    } catch { }
    $p
}
# Pruefsumme auch fuer eine in AutoCAD geoeffnete Datei (Get-FileHash scheitert an der Sperre, Befund 24.10. 17:28).
function Pruefsumme([string] $datei) {
    $s = [IO.File]::Open($datei, 'Open', 'Read', 'ReadWrite')
    try { -join ([Security.Cryptography.SHA256]::Create().ComputeHash($s) | ForEach-Object { $_.ToString('X2') }) } finally { $s.Dispose() }
}
function ProtokollZeilen { if (Test-Path $protokoll) { @(Get-Content $protokoll -Encoding UTF8) } else { @() } }
function WarteAufProtokoll([int] $ab, [string] $muster, [int] $sek = 20) {
    for ($i = 0; $i -lt $sek * 2; $i++) {
        $neu = @(ProtokollZeilen | Select-Object -Skip $ab | Where-Object { $_ -match $muster })
        if ($neu.Count -gt 0) { return $neu[0] }
        Warte 0.5
    }
    $null
}
# Befehl nur an die eigene, aktive Testkopie - nie an eine Zeichnung des Nutzers.
function Befehl($doc, [string] $text) {
    $aktiv = Com { $script:acad.ActiveDocument.FullName }
    if ($aktiv -ne $doc.FullName) { Notiere "ABGEBROCHEN: aktiv ist nicht die eigene Testkopie ($aktiv)." | Out-Null; Schluss 3 }
    Com { $doc.SendCommand($text) } | Out-Null
}
function Kopie([string] $quelle, [string] $name) {
    if (-not (Test-Path $quelle)) { throw "Quelle fehlt: $quelle" }
    $ziel = Join-Path $script:lauf $name
    Copy-Item $quelle $ziel -Force
    (Get-Item $ziel).IsReadOnly = $false
    $ziel
}
function EigeneOffene([string] $datei) {
    foreach ($x in @($script:acad.Documents)) { if ($x.FullName -eq $datei) { return $x } }
    $null
}
function Oeffne([string] $datei) {
    # Oeffnen einer grossen Kopie blockiert COM laenger als 15 s (Befund 23.10. 19:37) - darum 90 s; kommt die Antwort
    # nicht, die Kopie trotzdem suchen, damit sie am Ende ohne Speichern geschlossen wird.
    try { $d = Com { $script:acad.Documents.Open($datei, $false) } 90 }
    catch { $d = Com { EigeneOffene $datei } 60; if ($d) { $script:eigene.Add($d) }; throw }
    $script:eigene.Add($d)
    Warte 3
    $d
}
function Schliesse($d) { try { Com { $d.Close($false) } | Out-Null } catch { }; [void]$script:eigene.Remove($d); Warte 2 }

# --- 0. Anhaengen ------------------------------------------------------------------------------------------------------
$mep = @(Get-Process acad -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*AutoCAD 2026*' })
if ($mep.Count -ne 1) { throw "Erwartet genau ein laufendes MEP 2026 (vom Nutzer gestartet), gefunden: $($mep.Count)" }
if (Get-Process accoreconsole -ErrorAction SilentlyContinue) { throw 'accoreconsole laeuft - waehrend eines MEP-Slots nicht erlaubt (AMK-Regel)' }
$script:mepPid = $mep[0].Id
$script:haupt = $mep[0].MainWindowHandle
$script:acad = Com { [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application.25.1') }
$avtGeladen = Test-Path (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AVT.bundle\Contents\AVT.Mep.dll')
"Angehaengt an MEP 2026 (PID $script:mepPid), offene Zeichnungen: $(Com { $script:acad.Documents.Count }), AVT-Bundle installiert: $avtGeladen"
if (-not $Ausfuehren) { 'Nur geprueft - Test mit -Ausfuehren starten (nur nach "Slot: AVT").'; return }

# --- Ab hier Sofort-Abbruch bei Eingabe. Sichern -----------------------------------------------------------------------
$script:eingabeStart = [AvtTest.Eingabe]::Letzte()
$stempel = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:lauf = Join-Path $Ordner $stempel
New-Item -ItemType Directory -Force $script:lauf | Out-Null
$script:ergebnisDatei = Join-Path $script:lauf 'ergebnis.txt'
& reg.exe export 'HKCU\Software\Autodesk\AutoCAD\R25.1' (Join-Path $script:lauf 'profil_vorher.reg') /y | Out-Null
if (Test-Path $einstellungen) { Copy-Item $einstellungen (Join-Path $script:lauf 'AVT_einstellungen_vorher.json') }
$protVorher = (ProtokollZeilen).Count
Notiere "Start AVT-MEP-Test, MEP PID $script:mepPid, Ordner $script:lauf" | Out-Null
KeinDialog 'Start'

try {
    # --- 1. Werkzeugkasten -------------------------------------------------------------------------------------------
    try {
        # COM-Sammlungen aufzaehlen statt .Item() - PowerShell findet Item an spaet gebundenen AutoCAD-Sammlungen nicht (Befund 23.10. 19:13).
        $gruppe = Com { @($script:acad.MenuGroups) | Where-Object { $_.Name -eq 'AVT' } | Select-Object -First 1 }
        if ($null -eq $gruppe) { throw 'Menuegruppe AVT nicht geladen' }
        $kasten = Com { @($gruppe.Toolbars) | Where-Object { $_.Name -eq 'AVT' } | Select-Object -First 1 }
        if ($null -eq $kasten) { throw 'Werkzeugkasten AVT nicht in der Menuegruppe' }
        $anzahl = Com { $kasten.Count }
        $knopf = Com { @($kasten) | Select-Object -First 1 }
        $makro = Com { $knopf.Macro }; $hilfe = Com { $knopf.HelpString }; $name = Com { $knopf.Name }; $sichtbar = Com { $kasten.Visible }
        $ok = $anzahl -eq 1 -and $makro -match 'AVTPALETTE' -and $name -eq 'AVT – Vorlagen-Tool (Palette öffnen)'
        Notiere ("1 Werkzeugkasten AVT: " + $(if ($ok) { 'OK' } else { 'FEHLER' }) + " - Knoepfe $anzahl, sichtbar $sichtbar, Name '$name', Makro '$makro', Hilfe '$hilfe'") | Out-Null
    } catch { Notiere "1 Werkzeugkasten AVT: FEHLER - nicht gefunden ($($_.Exception.Message))" | Out-Null }
    Notiere "1 Bild: $(Foto '1_werkzeugkasten')" | Out-Null

    # --- 4. Hinweis beim Oeffnen (Kopie RI 9.1) ----------------------------------------------------------------------
    $ri = Kopie $quelleRi91 'avt_mep_ri91.dwg'
    $hashVorher = (Get-FileHash $ri -Algorithm SHA256).Hash
    $ab = (ProtokollZeilen).Count
    $uhr = [Diagnostics.Stopwatch]::StartNew()
    $docRi = Oeffne $ri
    KeinDialog 'Oeffnen'
    $hinweis = WarteAufProtokoll $ab '\[Hinweis\] avt_mep_ri91\.dwg' 30
    Notiere ("4 Hinweis beim Oeffnen: " + $(if ($hinweis -match 'Einträge nicht in der Fassung') { "OK - $hinweis" } elseif ($hinweis) { "unerwartet - $hinweis" } else { 'FEHLER - kein Eintrag nach 30 s (Hinweis ausgeschaltet?)' }) + " (nach $([int]$uhr.Elapsed.TotalSeconds) s)") | Out-Null
    KeinDialog 'Hinweis'

    # --- 2. Palette per Befehl (wie Knopf) ---------------------------------------------------------------------------
    $ab = (ProtokollZeilen).Count
    Befehl $docRi "AVTPALETTE`n"
    $offen = WarteAufProtokoll $ab 'AVTPALETTE: geöffnet' 15
    Warte 2
    $palette = Finde 'Bereinigen …' $null ([Windows.Automation.ControlType]::Button)
    $r = if ($palette) { $palette.Current.BoundingRectangle } else { $null }
    Notiere ("2 Palette AVTPALETTE: " + $(if ($offen -and $palette -and -not $palette.Current.IsOffscreen) { "OK - sichtbar und ausgeklappt (Knopf 'Bereinigen …' auf dem Schirm bei $([int]$r.X),$([int]$r.Y)) - $offen" } elseif ($offen) { "TEILWEISE - geoeffnet laut Protokoll, Inhalt nicht sichtbar (eingeklappt?) - $offen" } else { 'FEHLER - kein Protokolleintrag' })) | Out-Null
    Notiere "2 Bild (Palette + Symbol): $(Foto '2_palette')" | Out-Null
    KeinDialog 'Palette'

    # --- 6. Zahnrad ---------------------------------------------------------------------------------------------------
    $zahnrad = Finde '⚙' $null ([Windows.Automation.ControlType]::Button)
    if ($zahnrad) {
        Druecke $zahnrad
        Warte 2
        $fenster = $null
        foreach ($n in 'AVT – Einstellungen und Version', 'AVT - Einstellungen und Version') { if (-not $fenster) { $fenster = Finde $n $null ([Windows.Automation.ControlType]::Window) } }
        if ($fenster) {
            $texte = @($fenster.FindAll($Baum::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name })
            $hatVersion = @($texte | Where-Object { $_ -match '^Version 0\.3\.\d' }).Count -gt 0
            $hatCopy = @($texte | Where-Object { $_ -match 'S\. Eichler · Eichler-GmbH' }).Count -gt 0
            $hatHist = @($texte | Where-Object { $_ -match '^0\.1\.0' }).Count -gt 0
            Notiere ("6 Zahnrad: " + $(if ($hatVersion -and $hatCopy -and $hatHist) { 'OK' } else { 'FEHLER' }) + " - Version $hatVersion, Copyright $hatCopy, Historie $hatHist; Texte: $(($texte | Select-Object -First 4) -join ' | ')") | Out-Null
            Notiere "6 Bild: $(Foto '6_zahnrad')" | Out-Null
            try { $fenster.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() } catch { Notiere "6 Fenster nicht geschlossen: $($_.Exception.Message)" | Out-Null }
            Warte 1
        } else { Notiere '6 Zahnrad: FEHLER - Fenster nicht gefunden' | Out-Null }
    } else { Notiere '6 Zahnrad: FEHLER - Knopf ⚙ nicht gefunden' | Out-Null }
    KeinDialog 'Zahnrad'

    # --- 5. Bereinigen an der RI-9.1-Kopie ----------------------------------------------------------------------------
    # Schwebende Palette mit "Automatisch ausblenden" klappt nach dem Zahnrad-Fenster wieder ein (Befund 24.10. 17:21) -
    # darum wie ein Nutzer erneut AVTPALETTE (Regel: Befehl klappt aus).
    $ab = (ProtokollZeilen).Count
    Befehl $docRi "AVTPALETTE`n"
    WarteAufProtokoll $ab 'AVTPALETTE: geöffnet' 15 | Out-Null
    Warte 2
    $knopf = Finde 'Bereinigen …' $null ([Windows.Automation.ControlType]::Button)
    if (-not $knopf) { throw 'Knopf Bereinigen … fehlt' }
    Druecke $knopf
    # Vorschau bis 30 s abwarten (die volle Analyse dauert bei grossen Zeichnungen) - Merkmal: Knopf "Ausgewählte bereinigen …".
    $los = $null
    # Ohne Maus ueber der Palette klappt sie sofort wieder ein (Befund 24.10. 17:25, Bild 5a) - vor jedem Nachsehen ausklappen.
    for ($i = 0; $i -lt 10 -and -not $los; $i++) {
        Warte 2
        Befehl $docRi "AVTPALETTE`n"
        Warte 1
        $los = Finde 'Ausgewählte bereinigen …' $null ([Windows.Automation.ControlType]::Button)
    }
    $boxen = @(Fenster | ForEach-Object { $_.FindAll($Baum::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::CheckBox))) })
    $an = @($boxen | Where-Object { $_.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' }).Count
    $alle = @(Fenster | ForEach-Object { $_.FindAll($Baum::Descendants, [Windows.Automation.Condition]::TrueCondition) } | ForEach-Object { $_.Current.Name })
    $gruppen = @('a) Fremd', 'b) Fremd', 'c) Projektinhalt' | ForEach-Object { $g = $_; @($alle | Where-Object { $_ -like "$g*" }).Count -gt 0 })
    $berichtText = @($alle | Where-Object { $_ -like 'Vorschau *' -or $_ -like 'Keine Fremdinhalte*' -or $_ -like 'Achtung:*' }) | Select-Object -First 2
    $k2 = Finde 'Bereinigen …' $null ([Windows.Automation.ControlType]::Button)
    Notiere ("5a Vorschau: Gruppen a/b/c sichtbar $($gruppen -join '/'), Kontrollkaestchen $($boxen.Count), davon angehakt $an; Palette sichtbar $([bool]($k2 -and -not $k2.Current.IsOffscreen)); Text: $($berichtText -join ' | ')") | Out-Null
    Notiere "5a Bild (ganzer Schirm): $(Schirmfoto '5a_vorschau')" | Out-Null
    if (-not $los) { throw 'Knopf Ausgewählte bereinigen … fehlt' }
    $ab = (ProtokollZeilen).Count
    Druecke $los
    # Eigene Rueckfrage "AVT – Bereinigen" mit Ja beantworten - nur dieser Dialog, jeder andere fuehrt zum Abbruch. Bis 10 s warten.
    $frage = $null
    for ($i = 0; $i -lt 10 -and -not $frage; $i++) {
        Warte 1
        foreach ($n in 'AVT – Bereinigen', 'AVT - Bereinigen') { if (-not $frage) { $frage = Finde $n $null ([Windows.Automation.ControlType]::Window) } }
    }
    if ($frage) {
        Notiere "5b Rueckfrage AVT erschienen: $((@($frage.FindAll($Baum::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) -join ' ') -replace '\s+', ' ')" | Out-Null
        Notiere "5b Bild: $(Foto '5b_rueckfrage')" | Out-Null
        if (-not (DialogKnopf $frage @('Ja', 'Yes'))) { throw 'Knopf Ja in der Rueckfrage nicht gefunden' }
        $fertig = WarteAufProtokoll $ab '\[Bereinigen\]' 60
        Warte 2
        KeinDialog 'Bereinigen'
        $sicherung = @(Get-ChildItem $script:lauf -Filter 'avt_mep_ri91_vor-Bereinigen_*.dwg')
        $hashSicherung = if ($sicherung.Count -gt 0) { (Get-FileHash $sicherung[0].FullName -Algorithm SHA256).Hash } else { '' }
        $hashNachher = Pruefsumme $ri   # Datei ist noch in AutoCAD offen - mit geteiltem Lesen
        $dbmod = Com { $docRi.GetVariable('DBMOD') }
        Notiere ("5c Sicherungskopie: " + $(if ($sicherung.Count -eq 1 -and $hashSicherung -eq $hashVorher) { "OK - $($sicherung[0].Name), Pruefsumme = Original" } else { "FEHLER - $($sicherung.Count) Datei(en)" })) | Out-Null
        Notiere ("5d Bericht: " + $(if ($fertig -match 'Ergebnis \| entfernt') { "OK - $($fertig.Substring(0, [Math]::Min(400, $fertig.Length)))" } elseif ($fertig) { "pruefen - $($fertig.Substring(0, [Math]::Min(400, $fertig.Length)))" } else { 'FEHLER - kein Eintrag' })) | Out-Null
        Notiere ("5e Nicht gespeichert: " + $(if ($hashNachher -eq $hashVorher -and [int]$dbmod -ne 0) { "OK - Datei unveraendert, DBMOD $dbmod" } else { "FEHLER - Datei geaendert oder DBMOD $dbmod" })) | Out-Null
        Notiere "5 Bild nach dem Bereinigen: $(Foto '5_nachher')" | Out-Null
    } else {
        Notiere '5b FEHLER: Rueckfrage "AVT – Bereinigen" nicht erschienen (Zeichnung ungespeichert? Bericht in der Palette)' | Out-Null
        Notiere "5b Bild: $(Foto '5b_ohne_rueckfrage')" | Out-Null
    }
    Schliesse $docRi

    # --- S. Sonde-Thema (Vorbereitung Handpunkt) ----------------------------------------------------------------------
    if (Test-Path $quelleSonde) {
        $docS = Oeffne (Kopie $quelleSonde 'avt_mep_sonde.dwg')
        KeinDialog 'Sonde'
        Notiere "S Sonde-Zeichnung geoeffnet (Thema AVT_Sonde_Thema_Kopie, Farbe 200,30,30). Bild: $(Foto 'S_sonde')" | Out-Null
        Notiere 'S Handpunkt: Anzeigethema anwenden geht per API nicht (IsActive nur lesbar, ANA-Befund) - Nutzer: Legende einfuegen, "Anzeigethema anwenden", rote Faerbung?' | Out-Null
        Schliesse $docS
    } else { Notiere "S uebersprungen: $quelleSonde fehlt" | Out-Null }
    Notiere '3 Handpunkt: MEP mit offener AVT-Palette beenden und neu starten - Palette bleibt zu, keine Meldung "Unbekannter Befehl" (Skript startet/beendet MEP nie).' | Out-Null
}
catch { Notiere "FEHLER im Ablauf: $($_.Exception.Message)" | Out-Null }

# --- Nachher: Protokoll, Profil, Einstellungen vergleichen (nie zurueckspielen) -----------------------------------------
$fehler = @(ProtokollZeilen | Select-Object -Skip $protVorher | Where-Object { $_ -match '\[Fehler\]' })
Notiere ("AVT-Protokoll Fehler waehrend des Tests: " + $(if ($fehler.Count -eq 0) { 'keine' } else { $fehler -join ' / ' })) | Out-Null
& reg.exe export 'HKCU\Software\Autodesk\AutoCAD\R25.1' (Join-Path $script:lauf 'profil_nachher.reg') /y | Out-Null
$v = Get-Content (Join-Path $script:lauf 'profil_vorher.reg'); $n = Get-Content (Join-Path $script:lauf 'profil_nachher.reg')
$diff = @(Compare-Object $v $n)
Notiere "Profil R25.1: $($diff.Count) geaenderte Zeilen (nur verglichen, nichts zurueckgespielt) - Details profil_vorher/nachher.reg" | Out-Null
if (Test-Path (Join-Path $script:lauf 'AVT_einstellungen_vorher.json')) {
    $gleich = (Get-FileHash $einstellungen).Hash -eq (Get-FileHash (Join-Path $script:lauf 'AVT_einstellungen_vorher.json')).Hash
    Notiere "AVT-Einstellungen unveraendert: $gleich" | Out-Null
}
Schluss 0
