using System.Text.RegularExpressions;

namespace Avt.Kern.Fremd;

/// <summary>Gruppe eines Fundes (Auftrag AMK/Nutzer 23.10.2026).</summary>
public enum Gruppe
{
    /// <summary>a) fremd und unbenutzt – Bereinigen bietet Entfernen an (Vorgabe: angehakt).</summary>
    FremdUnbenutzt,
    /// <summary>b) fremd und benutzt – nie still löschen; behalten (Ausnahme merken), später ersetzen / an ASG geben.</summary>
    FremdBenutzt,
    /// <summary>c) Projektinhalt – nur Information.</summary>
    Projektinhalt,
}

/// <summary>
/// Ein benanntes Objekt, das nicht zur Fassung gehört (Kandidat). <see cref="Benutzer"/>: Pfade der Objekte außerhalb
/// des Kandidaten, die auf ihn (oder auf etwas in ihm) zeigen. <see cref="Spuren"/>: Gründe, warum er fremd ist.
/// </summary>
public sealed record Kandidat(string Pfad, string Art, string Name, IReadOnlyCollection<string> Benutzer, IReadOnlyList<string> Spuren);

public sealed record Einordnung(Kandidat Kandidat, Gruppe Gruppe, string Grund);

/// <summary>
/// Einteilung der Funde in a/b/c – ohne AutoCAD, damit sie sich prüfen lässt. Was zur Fassung gehört (Referenz),
/// AutoCAD-/MEP-eigen ist, einem Modul gehört oder als Ausnahme gemerkt ist, wird nicht gemeldet.
/// </summary>
public static class Einteilung
{
    /// <summary>Arten, die von sich aus fremd sind, wenn sie nicht zur Fassung gehören.</summary>
    static readonly HashSet<string> ImmerFremd = ["Satzdefinition", "Wörterbuch", "Fremdobjekt"];

    /// <summary>Vorsilben der Module (Sätze, Wörterbücher, RegApps, Layer) – gehören den Modulen, nie „fremd“.</summary>
    static readonly string[] Module = ["ATT_", "ADM_", "ANA_", "ASG", "NA_", "AVT_", Standard.Kuerzel + "_"];

    /// <summary>Vorsilben von AutoCAD/MEP-Einträgen im Wörterbuch der Zeichnung (entstehen bei Bedarf).</summary>
    static readonly string[] SystemWoerterbuch = ["ACAD_", "AEC", "AcAec", "AcDb", "AcadDim", "AbsT", "ASE_"];

    /// <summary>Klassen von AutoCAD und MEP; alles andere stammt aus Zusatzprogrammen (Object-Enabler).</summary>
    static readonly string[] EigeneKlassen = ["AcDb", "Aec", "AcAec", "AbsTools", "vlo_"];

    /// <summary>Namen, die AutoCAD selbst vergibt.</summary>
    static readonly HashSet<string> SystemNamen = new(StringComparer.OrdinalIgnoreCase) { "0", "Defpoints", "Standard", "ByLayer", "ByBlock", "Continuous" };

    /// <summary>Alte Stempel- und Legendenblöcke (Altlasten laut Bauplan).</summary>
    static readonly string[] AltNamen = ["TAG", "eaklLeg"];

    static readonly Regex Dublette = new(@"^(?<basis>.+) \((?<n>\d+)\)$", RegexOptions.CultureInvariant);

    public static bool IstFremdklasse(string klasse) =>
        klasse.Length > 0 && !EigeneKlassen.Any(v => klasse.StartsWith(v, StringComparison.OrdinalIgnoreCase));

    /// <summary>„ANA_Hydraulik (2)“ → „ANA_Hydraulik“; sonst null.</summary>
    public static string? DublettenBasis(string name) => Dublette.Match(name) is { Success: true } m ? m.Groups["basis"].Value : null;

    public static bool IstAltname(string art, string name) =>
        art is "Block" or "Mehransichtsblock" && AltNamen.Any(v => name.StartsWith(v, StringComparison.OrdinalIgnoreCase));

    /// <summary>AutoCAD-/MEP-eigen oder Modul: nie melden.</summary>
    public static bool IstGeschuetzt(string art, string name)
    {
        if (SystemNamen.Contains(name) || name.StartsWith('*')) return true;
        if (Module.Any(v => name.StartsWith(v, StringComparison.OrdinalIgnoreCase))) return true;
        return art switch
        {
            "RegApp" => name.StartsWith("AC", StringComparison.OrdinalIgnoreCase) || name.StartsWith("AEC", StringComparison.OrdinalIgnoreCase),
            "Wörterbuch" => SystemWoerterbuch.Any(v => name.StartsWith(v, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    /// <summary>
    /// Funde einteilen. Unbenutzt ist auch, was nur von anderen unbenutzten Funden benutzt wird (z. B. ein Block nur in
    /// einem fremden Block). Reihenfolge des Ergebnisses: a, b, c, je nach Pfad.
    /// </summary>
    public static List<Einordnung> Einordnen(IEnumerable<Kandidat> kandidaten, IReadOnlySet<string> referenz, IReadOnlySet<string> ausnahmen)
    {
        var offen = kandidaten.Where(k => !referenz.Contains(k.Pfad) && !ausnahmen.Contains(k.Pfad) && !IstGeschuetzt(k.Art, k.Name))
            .GroupBy(k => k.Pfad, StringComparer.Ordinal).Select(g => g.First()).ToList();
        // Funde in einem anderen Fund (z. B. Einträge eines fremden Wörterbuchs) gehen mit ihm – nicht doppelt melden.
        var pfadeOffen = offen.Select(k => k.Pfad).ToHashSet(StringComparer.Ordinal);
        offen = offen.Where(k => !Vorfahren(k.Pfad).Any(pfadeOffen.Contains)).ToList();
        var unbenutzt = new HashSet<string>(StringComparer.Ordinal);
        bool weiter;
        do
        {
            weiter = false;
            foreach (var k in offen.Where(k => !unbenutzt.Contains(k.Pfad)))
            {
                if (k.Benutzer.All(b => unbenutzt.Contains(b) || UnterPfad(b, k.Pfad))) { unbenutzt.Add(k.Pfad); weiter = true; }
            }
        } while (weiter);

        var aus = new List<Einordnung>();
        foreach (var k in offen)
        {
            var fremd = k.Spuren.Count > 0 || ImmerFremd.Contains(k.Art);
            var spuren = k.Spuren.Count > 0 ? string.Join("; ", k.Spuren) : $"{k.Art} gehört nicht zur Fassung";
            if (unbenutzt.Contains(k.Pfad))
                aus.Add(new Einordnung(k, Gruppe.FremdUnbenutzt, $"{spuren}; unbenutzt"));
            else if (fremd)
                aus.Add(new Einordnung(k, Gruppe.FremdBenutzt, $"{spuren}; benutzt von {k.Benutzer.Count}"));
            else
                aus.Add(new Einordnung(k, Gruppe.Projektinhalt, $"nicht in der Fassung, benutzt von {k.Benutzer.Count}"));
        }
        return aus.OrderBy(e => e.Gruppe).ThenBy(e => e.Kandidat.Pfad, StringComparer.Ordinal).ToList();
    }

    static IEnumerable<string> Vorfahren(string pfad)
    {
        for (var i = pfad.LastIndexOf('/'); i > 0; i = pfad.LastIndexOf('/', i - 1)) yield return pfad[..i];
    }

    static bool UnterPfad(string pfad, string oben) => pfad.StartsWith(oben + "/", StringComparison.Ordinal);

    /// <summary>Kurzfassung für den leisen Hinweis, z. B. „3 Fremdinhalte, 1 benutzt“; null, wenn nichts Fremdes da ist.</summary>
    public static string? Kurzfassung(IReadOnlyCollection<Einordnung> e)
    {
        var a = e.Count(x => x.Gruppe == Gruppe.FremdUnbenutzt);
        var b = e.Count(x => x.Gruppe == Gruppe.FremdBenutzt);
        return a + b == 0 ? null : $"{a + b} Fremdinhalt{(a + b == 1 ? "" : "e")}, {b} benutzt";
    }
}
