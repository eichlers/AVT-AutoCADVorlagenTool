// AVTWERKZEUGKASTEN – Werkzeugkasten „AVT“ mit genau einem Knopf, der die AVT-Palette oeffnet (gemeinsame Regel AMK).
// Teil-Menue %APPDATA%\AVT\AVT.cuix (AcCui) mit eigenem Symbol, oben angedockt; beim ersten Start angelegt, bei neuem Stand
// neu gebaut, vom Anwender entladen = bleibt weg (AVTWERKZEUGKASTEN holt ihn zurueck). Vorbild ADM 0.9.24.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Customization;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(Avt.Mep.Werkzeugkasten.WerkzeugkastenBefehl))]

namespace Avt.Mep.Werkzeugkasten;

public class WerkzeugkastenBefehl
{
    const string Gruppe = "AVT";
    const string Name = "AVT";

    /// <summary>Ein Knopf: Kuerzel im Symbol, Farbe, Name, Hilfe, Makro.</summary>
    sealed record Knopf(string Kuerzel, string Farbe, string Name, string Hilfe, string Makro);

    // Ein Knopf (gemeinsame Regel: je Modul genau EIN Knopf): der Werkzeugkasten oeffnet das Werkzeug, alles
    // Weitere steckt in der Palette (Prüfen, Bereinigen, Zahnrad).
    static readonly Knopf[] Knoepfe =
    {
        // Titel nach gemeinsamer Regel (AMK, Nutzer 18.10.): „<Modul> – <Funktion> (Palette öffnen)“, Hilfe ein Satz.
        // Farbe Anthrazit #4A5662 (Nutzer 23.10.2026).
        new("AVT", "#4A5662", "AVT – Vorlagen-Tool (Palette öffnen)",
            "AVT-Palette öffnen: Zeichnung gegen den Eichler-Hausstandard prüfen und fremde Inhalte mit Vorschau bereinigen.",
            "^C^C_AVTPALETTE "),
    };

    static readonly string Ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVT");
        const string Dateiname = "AVT";
    static readonly string Datei = Path.Combine(Ordner, Dateiname + ".cuix");
    static readonly string StandDatei = Path.Combine(Ordner, Dateiname + "_werkzeugkasten.stand");

    // Was den Werkzeugkasten ausmacht – aendert es sich (neue Knoepfe, andere Makros), ist der geladene veraltet.
    static string Stand() => string.Join("\n", Knoepfe.Select(k => $"{k.Kuerzel};{k.Farbe};{k.Name};{k.Makro};{k.Hilfe}"));

    /// <summary>
    /// Beim Start (erstes Idle): noch nie angelegt → einmal anlegen und oben andocken. Geladen, aber von einer aelteren Version
    /// gebaut → neu bauen (Lage bleibt). Vom Anwender entladen → nichts tun.
    /// </summary>
    internal static void BeimStart()
    {
        try
        {
            if (!File.Exists(StandDatei))
            {
                if (!NeuLaden()) throw new InvalidOperationException($"{Datei} ließ sich nicht laden");
                Andocken();
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nAVT: Werkzeugkasten „AVT“ angelegt (oben angedockt) – Knopf AVT öffnet die AVT-Palette.\n");
                return;
            }
            if (!File.Exists(Datei) || !Geladen()) return;
            if (File.ReadAllText(StandDatei) == Stand() || !NeuLaden()) return;
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nAVT: Werkzeugkasten „AVT“ aktualisiert ({0} Knopf).\n", Knoepfe.Length);
        }
        catch (System.Exception ex)
        {
            // Sichtbar melden (nie still scheitern) – neu bauen geht jederzeit mit AVTWERKZEUGKASTEN.
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nAVT: Werkzeugkasten nicht angelegt – {0}. Befehl AVTWERKZEUGKASTEN legt ihn an.\n", ex.Message);
        }
    }

    // Entladen (sonst laesst sich die Datei nicht ersetzen), Symbole und Teil-Menue bauen, laden, Stand merken.
    static bool NeuLaden()
    {
        Directory.CreateDirectory(Ordner);
        if (Geladen()) Application.UnloadPartialMenu(Datei);
        try
        {
            Symbole(Ordner);
            Bauen(Datei, Ordner);
            if (!Application.LoadPartialMenu(Datei)) { File.Delete(StandDatei); return false; }
        }
        catch
        {
            // Entladen, aber nicht neu gebaut: ohne Stand legt AVT ihn beim naechsten Start neu an (Erfahrung ADM).
            try { File.Delete(StandDatei); } catch (IOException) { /* der eigentliche Fehler zaehlt */ }
            throw;
        }
        File.WriteAllText(StandDatei, Stand());
        return true;
    }

    [CommandMethod("AVTWERKZEUGKASTEN")]
    public void Anlegen()
    {
        Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            if (!NeuLaden()) { ed.WriteMessage("\nAVT: Werkzeugkasten konnte nicht geladen werden ({0}).", Datei); return; }
            Andocken();
            ed.WriteMessage("\nAVT: Werkzeugkasten „AVT“ angelegt und oben angedockt ({0}). Erneut AVTWERKZEUGKASTEN = neu bauen.", Datei);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nAVT: Werkzeugkasten nicht angelegt – {0}", ex.Message);
        }
    }

    static bool Geladen()
    {
        dynamic gruppen = Application.MenuGroups;
        foreach (dynamic g in gruppen) if (string.Equals((string)g.Name, Gruppe, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static void Bauen(string datei, string ordner)
    {
        var cs = new CustomizationSection { MenuGroupName = Gruppe };
        var makros = new MacroGroup(Gruppe, cs.MenuGroup);
        var werkzeugkasten = new Toolbar(Name, cs.MenuGroup) { ToolbarVisible = ToolbarVisible.show, ToolbarOrient = ToolbarOrient.top };
        int nr = 0;
        foreach (Knopf k in Knoepfe)
        {
            string id = "AVT_" + nr++;
            string klein = Path.Combine(ordner, $"AVT_Symbol_{id}_16.png"), gross = Path.Combine(ordner, $"AVT_Symbol_{id}_32.png");
            MenuMacro m = makros.CreateMenuMacro(k.Name, k.Makro, id, k.Hilfe, MacroType.Any, klein, gross, k.Name);
            _ = new ToolbarButton(m.ElementID, k.Name, werkzeugkasten, -1);
        }
        if (File.Exists(datei)) File.Delete(datei);
        cs.SaveAs(datei);
    }

    // Dockt oben an – neben die vorhandenen Werkzeugkaesten.
    static void Andocken()
    {
        dynamic gruppe = ((dynamic)Application.MenuGroups).Item(Gruppe);
        dynamic kasten = gruppe.Toolbars.Item(Name);
        kasten.Visible = true;
        // Steht er schon oben (so im Teil-Menue hinterlegt), meldet Dock „bereits fixiert“ – dann nichts tun.
        try { if ((int)kasten.DockStatus != 0) kasten.Dock(0); }   // 0 = acToolbarDockTop
        catch (System.Exception) { /* bleibt, wo er ist */ }
    }

    // Symbole 16 und 32 Pixel: farbiges Feld mit Kuerzel (WPF, ohne Bilddateien im Repo) – wie ATT.
    static void Symbole(string ordner)
    {
        int nr = 0;
        foreach (Knopf k in Knoepfe)
        {
            string id = "AVT_" + nr++;
            foreach (int groesse in new[] { 16, 32 })
                Speichern(Zeichnen(k, groesse), Path.Combine(ordner, $"AVT_Symbol_{id}_{groesse}.png"));
        }
    }

    static BitmapSource Zeichnen(Knopf k, int g)
    {
        var v = new DrawingVisual();
        using (DrawingContext dc = v.RenderOpen())
        {
            var farbe = (Color)ColorConverter.ConvertFromString(k.Farbe);
            dc.DrawRoundedRectangle(new SolidColorBrush(farbe), null, new Rect(0.5, 0.5, g - 1, g - 1), g / 6.0, g / 6.0);
            double schrift = k.Kuerzel.Length >= 3 ? g * 0.36 : g * 0.52;
            var text = new FormattedText(k.Kuerzel, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                         new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                                         schrift, Brushes.White, 1.0);
            dc.DrawText(text, new Point((g - text.Width) / 2, (g - text.Height) / 2));
        }
        var bild = new RenderTargetBitmap(g, g, 96, 96, PixelFormats.Pbgra32);
        bild.Render(v);
        return bild;
    }

    static void Speichern(BitmapSource bild, string pfad)
    {
        using var f = File.Create(pfad);
        Png(bild, f);
    }

    static void Png(BitmapSource bild, Stream ziel)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bild));
        enc.Save(ziel);
    }

    /// <summary>
    /// Symbol des AVT-Knopfs als Icon (16 und 32 px) für die Palette (AMK 20.10.2026) – dieselbe Zeichnung wie der
    /// Werkzeugkasten. Gebaut als .ico im Speicher (PNG-Bilder): kein GDI-Handle, das gehalten oder freigegeben werden muss.
    /// </summary>
    internal static System.Drawing.Icon PalettenSymbol() => new(new MemoryStream(Ico(Knoepfe[0], 16, 32)));

    // .ico-Aufbau: Kopf (0, Typ 1, Anzahl), je Bild ein Verzeichniseintrag (16 Byte), danach die PNG-Daten.
    static byte[] Ico(Knopf k, params int[] groessen)
    {
        byte[][] bilder = groessen.Select(g => { using var m = new MemoryStream(); Png(Zeichnen(k, g), m); return m.ToArray(); }).ToArray();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)bilder.Length);
        int versatz = 6 + 16 * bilder.Length;
        for (int n = 0; n < bilder.Length; n++)
        {
            w.Write((byte)groessen[n]); w.Write((byte)groessen[n]); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32); w.Write(bilder[n].Length); w.Write(versatz);
            versatz += bilder[n].Length;
        }
        foreach (byte[] b in bilder) w.Write(b);
        w.Flush();
        return ms.ToArray();
    }
}
