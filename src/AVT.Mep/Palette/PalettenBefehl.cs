// AVTPALETTE / AVT – andockbare AVT-Palette. Gemeinsame Regeln (AMK_Stand.md), Vorbilder ADM 0.9.24 (Knopf öffnet ganz)
// und ANA 0.9.24 (stiller Startbefehl, Ausblenden beim Beenden):
// - PaletteSet(titel, "AVTPALETTESTART", guid): AutoCAD ruft für eine beim Beenden offene Palette beim Start diesen Befehl
//   auf – er tut nichts außer einem Protokolleintrag. Name nie ändern.
// - Beim Start aus; Knopf oder AVTPALETTE zeigt sie sichtbar UND ausgeklappt (RolledUp = false im Leerlauf, Zweitprüfung nach
//   600 ms; AutoRollUp nie ändern; bei minimiertem MEP oder ausgeblendeten Paletten nichts erzwingen).
// - Nie an die untere Kante (Befehlszeile). Ausblenden bei QuitWillStart/BeginQuit, Wiederherstellen bei QuitAborted (ohne Ausklappen).
// - Palettensymbol = Knopfsymbol, nach Visible = true erneut gesetzt (Erfahrung ANA 0.9.19).

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(Avt.Mep.Palette.PalettenBefehl))]

namespace Avt.Mep.Palette;

public sealed class PalettenBefehl
{
    /// <summary>Stiller Startbefehl – NIE umbenennen (ein altes Profil riefe sonst einen unbekannten Befehl auf).</summary>
    internal const string Startbefehl = "AVTPALETTESTART";

    static PaletteSet? palette;
    static AvtAnsicht? ansicht;
    static bool beendet, warOffenBeimBeenden, beendenEingerichtet;
    static System.Windows.Threading.DispatcherTimer? zweitpruefung;

    internal static AvtAnsicht? Ansicht => ansicht;
    internal static bool Sichtbar => palette?.Visible == true;

    [CommandMethod("AVTPALETTE")]
    public void Zeigen() => Oeffnen("AVTPALETTE");

    /// <summary>Kurzer Befehl wie bei ANA/ASG.</summary>
    [CommandMethod("AVT")]
    public void ZeigenKurz() => Oeffnen("AVT");

    [CommandMethod(Startbefehl, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void PaletteStart() => Protokoll.Merke("Sitzung", $"{Startbefehl}: Palette war beim letzten Beenden offen – bleibt zu (öffnen mit Knopf AVT oder AVTPALETTE)");

    static void Oeffnen(string befehl)
    {
        try
        {
            if (palette == null) Anlegen();
            beendet = false; warOffenBeimBeenden = false;
            palette!.Visible = true;
            SymbolSetzen();   // MEP setzt es beim ersten Anzeigen teils zurück (ANA 0.9.19)
            NichtUnten();
            Ausklappen();
            ansicht?.ZeichnungAnzeigen(AcApp.DocumentManager.MdiActiveDocument);
            Protokoll.Merke("Palette", $"{befehl}: geöffnet (Dock {palette.Dock}, eingeklappt {palette.RolledUp})");
        }
        catch (System.Exception ex)
        {
            Protokoll.Merke("Fehler", $"{befehl}: {ex.GetBaseException().Message}");
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\nAVT: Palette nicht geöffnet – {ex.GetBaseException().Message}\n");
        }
    }

    static void Anlegen()
    {
        palette = new PaletteSet("AVT", Startbefehl, new Guid("6A3D9E52-0B7C-4F18-9C2E-4D5A8B1F7E63"))
        {
            MinimumSize = new System.Drawing.Size(300, 40),
            Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton
                    | PaletteSetStyles.ShowTabForSingle,
            // Die untere Kante gehört der Befehlszeile (Nutzer 20.10.2026).
            DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top,
        };
        SymbolSetzen();
        ansicht = new AvtAnsicht();
        palette.AddVisual("AVT", ansicht);
        BeendenEinrichten();
        var dm = AcApp.DocumentManager;
        dm.DocumentActivated += (_, e) =>
        {
            try { if (palette is { Visible: true }) ansicht?.ZeichnungAnzeigen(e.Document); }
            catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Zeichnungswechsel: {ex.Message}"); }
        };
        dm.DocumentToBeDestroyed += (_, e) =>
        {
            try { ansicht?.ZeichnungGeschlossen(e.Document); }
            catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Zeichnung geschlossen: {ex.Message}"); }
        };
    }

    static void SymbolSetzen()
    {
        try { if (palette != null) palette.Icon = Werkzeugkasten.WerkzeugkastenBefehl.PalettenSymbol(); }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Palettensymbol: {ex.Message}"); }
    }

    /// <summary>Lag die Palette noch unten (Profil von früher): rechts andocken.</summary>
    static void NichtUnten()
    {
        try
        {
            if (palette?.Dock != DockSides.Bottom) return;
            palette.Dock = DockSides.Right;
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nAVT: Die Palette lag unten (dort ist die Befehlszeile) – jetzt rechts angedockt.\n");
        }
        catch (System.Exception) { /* Lage bleibt */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr fenster);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr fenster);

    /// <summary>Nichts erzwingen, wenn MEP minimiert ist oder alle Paletten ausgeblendet sind (Strg+Umschalt+H).</summary>
    static bool Pruefbar()
    {
        IntPtr haupt = AcApp.MainWindow.Handle;
        if (IsIconic(haupt) || !IsWindowVisible(haupt)) return false;
        try { if (palette != null && !IsWindowVisible(palette.Handle)) return false; } catch (System.Exception) { /* weiter prüfen */ }
        return true;
    }

    /// <summary>
    /// Knopf öffnet die Palette immer ganz (Nutzer 22.10.): im Leerlauf ausrollen, nach 600 ms nachprüfen. Die Einstellung
    /// „Automatisch ausblenden“ des Nutzers bleibt (AutoRollUp wird nie geändert).
    /// </summary>
    static void Ausklappen()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                if (palette is not { Visible: true } || !Pruefbar()) return;
                if (palette.RolledUp) palette.RolledUp = false;
                if (zweitpruefung == null)
                {
                    zweitpruefung = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    zweitpruefung.Tick += (_, _) =>
                    {
                        zweitpruefung.Stop();
                        try
                        {
                            if (palette is { Visible: true, RolledUp: true } && Pruefbar())
                            {
                                palette.RolledUp = false;
                                Protokoll.Merke("Palette", "Zweitprüfung: war wieder eingeklappt – ausgerollt");
                            }
                            SymbolSetzen();
                        }
                        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Zweitprüfung: {ex.Message}"); }
                    };
                }
                zweitpruefung.Stop();
                zweitpruefung.Start();
            }
            catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Ausklappen: {ex.Message}"); }
        }));
    }

    static void BeendenEinrichten()
    {
        if (beendenEingerichtet) return;
        beendenEingerichtet = true;
        AcApp.BeginQuit += (_, _) => BeimBeenden("BeginQuit");
        AcApp.QuitWillStart += (_, _) => BeimBeenden("QuitWillStart");
        AcApp.QuitAborted += (_, _) => Sicher(() =>
        {
            Protokoll.Merke("Beenden", $"QuitAborted – Palette war offen: {warOffenBeimBeenden}");
            beendet = false;
            // Nur den vorigen Zustand herstellen – nicht ausklappen (gemeinsame Regel).
            if (warOffenBeimBeenden && palette != null) palette.Visible = true;
            warOffenBeimBeenden = false;
        });
    }

    // BeginQuit und QuitWillStart kommen beide: „war offen“ nur beim ersten merken.
    static void BeimBeenden(string ereignis) => Sicher(() =>
    {
        bool sichtbar = palette?.Visible == true;
        Protokoll.Merke("Beenden", $"{ereignis} – Palette sichtbar: {sichtbar}");
        if (!beendet) warOffenBeimBeenden = sichtbar;
        beendet = true;
        if (palette != null && sichtbar) palette.Visible = false;
    });

    static void Sicher(Action tun)
    {
        try { tun(); }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Palette beim Beenden: {ex.GetBaseException().Message}"); }
    }
}
