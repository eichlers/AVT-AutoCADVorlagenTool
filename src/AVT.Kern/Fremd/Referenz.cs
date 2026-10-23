using System.Reflection;

namespace Avt.Kern.Fremd;

/// <summary>
/// Was zur Fassung gehört: benannte Einträge aus dem Inhaltsauszug der Fassung (tools/avt_referenz.py).
/// Quelle: eine Datei (z. B. später neben der Fassung im EHS-Ordner) oder die eingebettete Referenz der aktuellen
/// Live-Fassung (heute EHS 0.2).
/// </summary>
public sealed class Referenz(string fassung, string herkunft, IReadOnlySet<string> pfade)
{
    /// <summary>Eingebettete Referenz, wenn die Zeichnung keine Fassung trägt (Auftrag AMK: dann gegen die Live-Fassung).</summary>
    public const string LiveFassung = "EHS_0.2";

    public string Fassung { get; } = fassung;
    public string Herkunft { get; } = herkunft;
    public IReadOnlySet<string> Pfade { get; } = pfade;

    public static Referenz Lies(string fassung, TextReader text, string herkunft)
    {
        var pfade = new HashSet<string>(StringComparer.Ordinal);
        while (text.ReadLine() is { } z)
        {
            if (z.Length > 0 && z[0] != '#') pfade.Add(z.TrimEnd());
        }
        if (pfade.Count == 0) throw new InvalidDataException($"Referenz {fassung} ({herkunft}) ist leer");
        return new Referenz(fassung, herkunft, pfade);
    }

    /// <summary>Eingebettete Referenz der Fassung (Ressource „AVT.Referenz.&lt;Fassung&gt;.referenz.txt“), sonst null.</summary>
    public static Referenz? Eingebettet(string fassung)
    {
        var name = $"AVT.Referenz.{fassung}.referenz.txt";
        using var s = typeof(Referenz).Assembly.GetManifestResourceStream(name);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return Lies(fassung, r, "eingebettet in AVT.Kern");
    }

    /// <summary>
    /// Referenz für eine Zeichnung: zuerst eine Datei &lt;Fassung&gt;.referenz.txt im Ordner <paramref name="ordner"/>
    /// (falls angegeben), dann die eingebettete der Fassung, sonst die Live-Fassung.
    /// </summary>
    public static Referenz Fuer(string? fassung, string? ordner)
    {
        if (fassung is { Length: > 0 } && ordner is { Length: > 0 })
        {
            var datei = Path.Combine(ordner, $"{fassung}.referenz.txt");
            if (File.Exists(datei))
            {
                using var r = new StreamReader(datei);
                return Lies(fassung, r, datei);
            }
        }
        if (fassung is { Length: > 0 } && Eingebettet(fassung) is { } eigene) return eigene;
        return Eingebettet(LiveFassung) ?? throw new InvalidOperationException($"Eingebettete Referenz {LiveFassung} fehlt in {Assembly.GetExecutingAssembly().GetName().Name}");
    }
}
