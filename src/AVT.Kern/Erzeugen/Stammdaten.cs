using System.Globalization;
using System.Text.RegularExpressions;

namespace Avt.Kern.Erzeugen;

/// <summary>Eine Farbe aus den Stammdaten: „ACI 40“ oder „RGB 181,203,11“.</summary>
public sealed record Farbe(bool Rgb, short Index, byte R, byte G, byte B)
{
    public static Farbe Lies(string text)
    {
        var t = text.Trim();
        if (Regex.Match(t, @"^ACI\s+(\d{1,3})$", RegexOptions.IgnoreCase) is { Success: true } a && short.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture) is >= 0 and <= 256 and var i)
            return new Farbe(false, i, 0, 0, 0);
        if (Regex.Match(t, @"^RGB\s+(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})$", RegexOptions.IgnoreCase) is { Success: true } r)
            return new Farbe(true, 0, byte.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), byte.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture), byte.Parse(r.Groups[3].Value, CultureInfo.InvariantCulture));
        throw new FormatException($"Farbe „{text}“ nicht lesbar (erwartet „ACI n“ oder „RGB r,g,b“)");
    }

    public override string ToString() => Rgb ? $"RGB {R},{G},{B}" : $"ACI {Index}";
}

/// <summary>Werte aus Tabellen der Stammdaten.</summary>
public static class Stammwerte
{
    static readonly int[] Linienstaerken = [0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211];

    /// <summary>„0.30 mm“ → 30 (AutoCAD-Linienstärke in 1/100 mm); nur zulässige Stufen.</summary>
    public static int Linienstaerke(string text)
    {
        var m = Regex.Match(text.Trim(), @"^(\d+(?:[.,]\d+)?)\s*mm$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new FormatException($"Linienstärke „{text}“ nicht lesbar (erwartet z. B. „0.30 mm“)");
        var wert = (int)Math.Round(double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * 100);
        if (!Linienstaerken.Contains(wert)) throw new FormatException($"Linienstärke „{text}“ ist keine AutoCAD-Stufe");
        return wert;
    }

    public static double Zahl(string text) => double.Parse(text.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);

    /// <summary>Muster aus Basis_entfernen.csv: „NOD/AEC_PROPERTY_SET_DEFS/*“ – * steht für einen Namen (ohne „/“).</summary>
    public static bool Passt(string muster, string pfad) =>
        Regex.IsMatch(pfad, "^" + Regex.Escape(muster).Replace(@"\*", "[^/]+") + "$", RegexOptions.CultureInvariant);

    /// <summary>Ist die Schrift Arial (TrueType)? Den Dateinamen „arial.ttf“ trägt AutoCAD beim Speichern selbst ein.</summary>
    public static bool IstArial(string schriftart, string? datei) =>
        schriftart.Equals("Arial", StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrEmpty(datei) || Path.GetFileName(datei).Equals("arial.ttf", StringComparison.OrdinalIgnoreCase));

    /// <summary>„EHS_S_{Kuerzel}“ mit Werten füllen.</summary>
    public static string Fuelle(string vorlage, IReadOnlyDictionary<string, string> werte) =>
        Regex.Replace(vorlage, @"\{(\w+)\}", m => werte.TryGetValue(m.Groups[1].Value, out var w) ? w : throw new FormatException($"Platzhalter {m.Value} unbekannt"));
}

/// <summary>
/// Eine Tabelle der Stammdaten (CSV, Semikolon, UTF-8, Zeilen mit # sind Kommentar, erste andere Zeile = Spaltenköpfe).
/// Fehlende Pflichtspalten und leere Pflichtwerte sind Fehler mit Datei und Zeile.
/// </summary>
public sealed class Tabelle
{
    public string Datei { get; }
    public IReadOnlyList<string> Spalten { get; }
    public IReadOnlyList<(int Zeile, Dictionary<string, string> Werte)> Zeilen { get; }

    Tabelle(string datei, IReadOnlyList<string> spalten, IReadOnlyList<(int, Dictionary<string, string>)> zeilen) =>
        (Datei, Spalten, Zeilen) = (datei, spalten, zeilen);

    public static Tabelle Lies(string datei, params string[] pflicht)
    {
        if (!File.Exists(datei)) throw new FileNotFoundException($"Stammdaten fehlen: {datei}");
        return Lies(Path.GetFileName(datei), File.ReadAllLines(datei, System.Text.Encoding.UTF8), pflicht);
    }

    public static Tabelle Lies(string name, IEnumerable<string> text, params string[] pflicht)
    {
        List<string>? spalten = null;
        var zeilen = new List<(int, Dictionary<string, string>)>();
        var nr = 0;
        foreach (var roh in text)
        {
            nr++;
            var z = roh.TrimStart('﻿');
            if (z.Trim().Length == 0 || z.StartsWith('#')) continue;
            var teile = z.Split(';');
            if (spalten == null)
            {
                spalten = teile.Select(t => t.Trim()).ToList();
                var fehlt = pflicht.Where(p => !spalten.Contains(p)).ToList();
                if (fehlt.Count > 0) throw new FormatException($"{name}: Spalten fehlen: {string.Join(", ", fehlt)}");
                continue;
            }
            var werte = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < spalten.Count; i++) werte[spalten[i]] = i < teile.Length ? teile[i].Trim() : "";
            var leer = pflicht.Where(p => werte[p].Length == 0).ToList();
            if (leer.Count > 0) throw new FormatException($"{name}, Zeile {nr}: leer: {string.Join(", ", leer)}");
            zeilen.Add((nr, werte));
        }
        if (spalten == null) throw new FormatException($"{name}: keine Spaltenköpfe");
        return new Tabelle(name, spalten, zeilen);
    }
}
