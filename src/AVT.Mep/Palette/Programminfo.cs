using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Avt.Mep.Palette;

/// <summary>Version, Stand (Builddatum), Copyright, Logo und Versionshistorie für Zahnrad und Tooltip (gemeinsame Regel AMK).</summary>
internal static class Programminfo
{
    static readonly Assembly Dll = typeof(Programminfo).Assembly;

    public static string Version => Dll.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    public static string Datum =>
        Attribut(nameof(AssemblyMetadataAttribute), "Builddatum") ?? File.GetLastWriteTime(Dll.Location).ToString("dd.MM.yyyy");

    public static string Copyright => Attribut(nameof(AssemblyCopyrightAttribute), null) ?? "© S. Eichler · Eichler-GmbH";

    /// <summary>
    /// Assembly-Attribut einzeln lesen: GetCustomAttributes würde auch die AutoCAD-Attribute (CommandClass) auflösen und
    /// außerhalb von AutoCAD (Vorschau) scheitern. <paramref name="schluessel"/>: erster Wert (Metadaten-Schlüssel) oder null.
    /// </summary>
    static string? Attribut(string typ, string? schluessel)
    {
        IList<CustomAttributeData> alle;
        try { alle = Dll.GetCustomAttributesData(); }
        catch (Exception) { return null; }   // außerhalb von AutoCAD (Vorschau) – Ersatzwerte
        foreach (var d in alle)
        {
            try
            {
                if (d.AttributeType.Name != typ) continue;
                var w = d.ConstructorArguments.Select(a => a.Value as string).ToList();
                if (schluessel == null) return w.FirstOrDefault();
                if (w.Count == 2 && w[0] == schluessel) return w[1];
            }
            catch (Exception) { /* Attribut aus einer nicht geladenen Bibliothek (AutoCAD) – überspringen */ }
        }
        return null;
    }

    /// <summary>„AVT 0.3.0 · Stand 23.10.2026 · © S. Eichler · Eichler-GmbH“</summary>
    public static string Zeile => $"AVT {Version} · Stand {Datum} · {Copyright}";

    public const string Beschreibung =
        "AVT – Vorlagen-Tool\n" +
        "Pflegt den Eichler-Hausstandard (EHS): prüft die offene Zeichnung gegen ihre Fassung und bereinigt fremde Inhalte.\n\n" +
        "• Diese Zeichnung: Fassung, Zeichnungseigenschaften, Hinweis auf Fremdinhalte\n" +
        "• Prüfen: nur lesen – Fassung, Pflichtfelder, Fremdinhalte, Inhaltsprüfsumme\n" +
        "• Bereinigen …: Vorschau (a fremd/unbenutzt · b fremd/benutzt · c Projektinhalt), dann Sicherungskopie, Bereinigen, Nachprüfung.\n" +
        "  AVT speichert nie – speichern Sie selbst.\n\n" +
        "Befehle: AVTPALETTE (AVT), AVTWERKZEUGKASTEN · Handbuch: docs\\AVT_Handbuch.pdf\n\n";

    /// <summary>Versionshistorie (neueste zuerst) – gleicher Stand wie im Handbuch, Kapitel „Versionsstand“.</summary>
    public static readonly (string Version, string Datum, string Inhalt)[] Historie =
    [
        ("0.3.1", "23.10.2026", "Palette im gemeinsamen Farbschema aller Module (wie ATT): folgt COLORTHEME hell/dunkel, auch beim Umschalten; Zahnrad ebenso"),
        ("0.3.0", "23.10.2026", "Plugin mit Palette und Werkzeugkasten-Knopf; Prüfen und Bereinigen der offenen Zeichnung mit Vorschau a/b/c, Sicherungskopie und Nachprüfung; leiser Hinweis beim Öffnen"),
        ("0.2.0", "23.10.2026", "Inhaltsauszug mit Inhaltsprüfsumme, Prüfen gegen eine Fassung, API-Sonde, DWGPROPS lesen"),
        ("0.1.0", "23.10.2026", "Bereinigen der Vorlage (aus ASG übernommen), Nachprüfung mit zweitem Durchgang"),
    ];

    public static FrameworkElement Kennung()
    {
        var tipp = new TextBlock { Text = Beschreibung + Zeile, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        var t = new TextBlock
        {
            Text = "AVT ⓘ", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = tipp, Cursor = System.Windows.Input.Cursors.Help,
        };
        ToolTipService.SetShowDuration(t, 60000);
        return t;
    }

    /// <summary>Logo auf weißem Schild; null, wenn das Bild fehlt.</summary>
    public static FrameworkElement? Logo(double hoehe = 30)
    {
        try
        {
            using Stream? s = Dll.GetManifestResourceStream("AVT.Eichler-Logo.png");
            if (s == null) return null;
            var bild = new BitmapImage();
            bild.BeginInit();
            bild.CacheOption = BitmapCacheOption.OnLoad;
            bild.StreamSource = s;
            bild.EndInit();
            bild.Freeze();
            var img = new Image { Source = bild, Height = hoehe, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return new Border
            {
                Child = img, Background = Brushes.White, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Zeile,
            };
        }
        catch (Exception) { return null; }   // Logo ist Beiwerk
    }
}

/// <summary>Einstellungen des Anwenders: %APPDATA%\AVT\AVT_einstellungen.json.</summary>
internal sealed record Einstellungen(bool HinweisBeimOeffnen = true)
{
    static readonly string Ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVT");
    public static readonly string Pfad = Path.Combine(Ordner, "AVT_einstellungen.json");

    public static Einstellungen Lies()
    {
        try { return File.Exists(Pfad) ? JsonSerializer.Deserialize<Einstellungen>(File.ReadAllText(Pfad)) ?? new() : new(); }
        catch (Exception ex) { Protokoll.Merke("Fehler", $"Einstellungen nicht lesbar ({ex.Message}) – Vorgaben"); return new(); }
    }

    public void Schreibe()
    {
        try
        {
            Directory.CreateDirectory(Ordner);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Protokoll.Merke("Fehler", $"Einstellungen nicht gespeichert: {ex.Message}"); }
    }
}

/// <summary>Sitzungsprotokoll %APPDATA%\AVT\AVT_sitzung.log (Start, Palette, Beenden, Bereinigen, Fehler).</summary>
internal static class Protokoll
{
    static readonly string Pfad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVT", "AVT_sitzung.log");

    public static void Merke(string art, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
            // Protokoll klein halten: über 1 MB wird es neu begonnen.
            if (File.Exists(Pfad) && new FileInfo(Pfad).Length > 1_000_000) File.Delete(Pfad);
            File.AppendAllText(Pfad, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{art}] {text}{Environment.NewLine}");
        }
        catch (Exception) { /* Protokoll ist Beiwerk */ }
    }
}
