using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Avt.Kern.Fremd;
using Avt.Mep.Palette;

// Zeichnet die AVT-Palette (ohne AutoCAD) im hellen und dunklen Farbschema mit Beispielinhalt als PNG.
// Aufruf: dotnet run --project tools\vorschau -- <Zielordner>
namespace Avt.Vorschau;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var ziel = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "AVT_Vorschau");
        Directory.CreateDirectory(ziel);
        foreach (var (name, farben) in new[] { ("hell", Farben.Hell), ("dunkel", Farben.Dunkel) })
        {
            var p = new Probe();
            p.Fuellen();
            p.Farbschema(farben);
            var breite = 380.0;
            p.Measure(new Size(breite, double.PositiveInfinity));
            var hoehe = Math.Max(400, p.DesiredSize.Height);
            p.Measure(new Size(breite, hoehe));
            p.Arrange(new Rect(0, 0, breite, hoehe));
            p.UpdateLayout();
            var bild = new RenderTargetBitmap((int)breite, (int)hoehe, 96, 96, PixelFormats.Pbgra32);
            bild.Render(p);
            var datei = Path.Combine(ziel, $"AVT_Palette_{name}.png");
            using (var s = File.Create(datei))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bild));
                enc.Save(s);
            }
            Console.WriteLine(datei);
        }
        return 0;
    }
}

/// <summary>Palette mit Beispielinhalt wie nach „Bereinigen …“ an einer Kopie von RI 9.1.</summary>
sealed class Probe : AvtOberflaeche
{
    static Kandidat K(string art, string name, int benutzer = 0) => new($"X/{name}", art, name, Enumerable.Range(0, benutzer).Select(i => $"M#{i}").ToList(), []);

    public void Fuellen()
    {
        zeichnung.Text = "avt_mep_ri91.dwg";
        fassung.Text = "Keine EHS-Vorlage (kein Fassungsstempel) – Vergleich mit der Live-Fassung EHS 0.2.";
        hinweis.Text = "216 Einträge nicht in der Fassung – Bereinigen …";
        VorschauFuellen(
        [
            new Einordnung(K("Block", "TAGBauteil3Zeilig"), Gruppe.FremdUnbenutzt, "alter Stempel-/Legendenblock; unbenutzt", true),
            new Einordnung(K("Wörterbuch", "LIN_MEDIEN_DICT"), Gruppe.FremdUnbenutzt, "Objekte fremder Klassen (linMediumRecord); unbenutzt", true),
            new Einordnung(K("Layer", "Alt_Leer"), Gruppe.FremdUnbenutzt, "Layer gehört nicht zur Fassung; unbenutzt, ohne Fremdspur – nicht vorab angehakt"),
            new Einordnung(K("Satzdefinition", "TAGBauteil", 655), Gruppe.FremdBenutzt, "Satzdefinition gehört nicht zur Fassung; benutzt von 655"),
            new Einordnung(K("Block", "LegendeRaumstempel", 1), Gruppe.Projektinhalt, "nicht in der Fassung, benutzt von 1"),
        ], 0);
        bericht.Text = "Vorschau avt_mep_ri91.dwg: 206 Fremdinhalte, 47 benutzt · Projektinhalt 10. Noch nichts geändert.";
    }
}
