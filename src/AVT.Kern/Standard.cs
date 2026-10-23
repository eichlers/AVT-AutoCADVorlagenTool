namespace Avt.Kern;

/// <summary>
/// Name des Hausstandards an EINER Stelle (Bauplan Abschnitt 12, Nutzer 23.10.2026: „EHS“ bleibt, muss aber umschwenkbar
/// sein). Ab AVT 0.3.0 kommen die Werte aus Stammdaten\Standard.csv; bis dahin hier. Nirgends sonst im Code „EHS“ schreiben.
/// </summary>
internal static class Standard
{
    /// <summary>Kürzel = Vorsilbe für Sätze, Stile, Wörterbücher, RegApp und benutzerdefinierte DWGPROPS.</summary>
    public const string Kuerzel = "EHS";
    public const string Langname = "Eichler-Hausstandard";

    /// <summary>NOD-Eintrag mit dem Fassungsstempel der Zeichnung.</summary>
    public const string Stempel = Kuerzel + "_Zeichnung";
    /// <summary>RegApp für die festen IDs (XData).</summary>
    public const string RegApp = Kuerzel;
    /// <summary>Vorsilbe der benutzerdefinierten DWGPROPS-Felder (z. B. EHS_Fassung).</summary>
    public const string Feld = Kuerzel + "_";

    /// <summary>Benutzerdefinierte DWGPROPS, die jede Fassung tragen muss (Bauplan Abschnitt 12).</summary>
    public static readonly string[] PflichtFelder = [.. new[] { "Fassung", "Vorgaenger", "Freigabe", "Profile", "Stammdaten",
        "Symbolfassung", "Basis", "Inhaltspruefsumme", "AVT" }.Select(f => Feld + f)];
}
