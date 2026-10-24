using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Avt.Kern.Erzeugen;

/// <summary>Eine Zeile aus Themen.csv (Nr 0 = Kopf: Komponente = Titel, Wert = Beschreibung).</summary>
public sealed record Themenzeile(string Thema, int Nr, string Komponente, string Feld, string Wert, string Farbe, string Linientyp, string Linienstaerke, double LtFaktor);

/// <summary>
/// AVT 0.4.0 Teil 2 (Y6): Anzeigethemen Medien, Stoffgruppen, RohrKanalKlassen aus Themen.csv. Die API legt die Darstellung
/// einer neuen Komponente nicht an (Sonde 23.10.) – darum aus dem Baustein-Thema (eine Komponente, Bausteine.csv) je Zeile eine
/// Komponente klonen, Regel auf das Feld aus Themen.csv umhängen, Wert und Darstellung setzen. Das Baustein-Thema wird danach entfernt.
/// </summary>
internal static class Themen
{
    public const string BausteinThema = Standard.Kuerzel + "_Thema_Baustein";

    public static IReadOnlyList<Themenzeile> Lesen(Tabelle t) => t.Zeilen.Select(z => z.Werte).Select(w => new Themenzeile(
        w["Thema"], int.Parse(w["Nr"], System.Globalization.CultureInfo.InvariantCulture), w["Komponente"], w.GetValueOrDefault("Feld") ?? "", w.GetValueOrDefault("Wert") ?? "",
        w.GetValueOrDefault("Farbe") ?? "", w.GetValueOrDefault("Linientyp") ?? "", w.GetValueOrDefault("Linienstaerke") ?? "",
        w.GetValueOrDefault("LT_Faktor") is { Length: > 0 } f ? Stammwerte.Zahl(f) : 1.0)).ToList();

    public static List<string> Bauen(Database db, Transaction tr, string stammdaten, Action<DBObject, string> id)
    {
        var aus = new List<string>();
        var zeilen = Lesen(Tabelle.Lies(Path.Combine(stammdaten, "Themen.csv"), "Thema", "Nr", "Komponente"));
        var dict = new AecDb.DictionaryDisplayThemeStyle(db);
        if (!dict.Has(BausteinThema, tr)) throw new InvalidDataException($"Baustein-Thema {BausteinThema} fehlt (Bausteine.csv)");
        var baustein = (AecDb.DisplayThemeStyle)tr.GetObject(dict.GetAt(BausteinThema), OpenMode.ForWrite);
        if (baustein.DisplayThemeComponents.Count != 1 || baustein.DisplayThemeComponents[0].DisplayProperties is null)
            throw new InvalidDataException("Baustein-Thema: genau eine Komponente mit Darstellung erwartet");
        foreach (var g in zeilen.GroupBy(z => z.Thema))
        {
            var kopf = g.SingleOrDefault(z => z.Nr == 0) ?? throw new InvalidDataException($"Themen.csv: {g.Key} ohne Kopfzeile (Nr 0)");
            if (dict.Has(g.Key, tr)) throw new InvalidOperationException($"Thema {g.Key} gibt es schon – Basis nicht leer?");
            var t = (AecDb.DisplayThemeStyle)baustein.Clone();
            dict.AddNewRecord(g.Key, t);
            tr.AddNewlyCreatedDBObject(t, true);
            t.Title = kopf.Komponente;
            t.Description = kopf.Wert;
            var vorbild = t.DisplayThemeComponents[0];
            foreach (var z in g.Where(z => z.Nr > 0).OrderBy(z => z.Nr))
            {
                var k = (AecDb.DisplayThemeComponentBase)vorbild.Clone();
                k.Name = z.Komponente;
                var (satz, feld) = Feld(db, tr, z.Feld);
                Umhaengen(k, satz, feld, z.Wert);
                var dp = k.DisplayProperties ?? throw new InvalidOperationException($"{g.Key}/{z.Komponente}: Darstellung leer");
                dp.Color = Farbwert(Farbe.Lies(z.Farbe));
                dp.LinetypeId = Linientyp(db, tr, z.Linientyp);
                dp.LineWeight = (LineWeight)Stammwerte.Linienstaerke(z.Linienstaerke);
                dp.LinetypeScale = z.LtFaktor;
                t.DisplayThemeComponents.Add(k);
            }
            t.DisplayThemeComponents.RemoveAt(0);
            id(t, "TH-" + g.Key);
            aus.Add($"AVTERZEUGEN Thema | {g.Key} | {t.DisplayThemeComponents.Count} Komponenten");
        }
        baustein.Erase();
        return aus;
    }

    static Color Farbwert(Farbe f) => f.Rgb ? Color.FromRgb(f.R, f.G, f.B) : Color.FromColorIndex(ColorMethod.ByAci, f.Index);

    static ObjectId Linientyp(Database db, Transaction tr, string name)
    {
        var tab = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        return tab.Has(name) ? tab[name] : throw new InvalidDataException($"Themen.csv: Linientyp „{name}“ fehlt – in Linientypen.csv aufnehmen");
    }

    /// <summary>„EHS_Leitungsstil.Kuerzel“ → Satzdefinition und Feld-ID.</summary>
    internal static (ObjectId Satz, int Feld) Feld(Database db, Transaction tr, string satzFeld)
    {
        var teile = satzFeld.Split('.', 2);
        if (teile.Length != 2) throw new InvalidDataException($"Themen.csv: Feld „{satzFeld}“ – erwartet Satz.Feld");
        var dict = new Aecp.DictionaryPropertySetDefinitions(db);
        if (!dict.Has(teile[0], tr)) throw new InvalidDataException($"Themen.csv: Satz {teile[0]} fehlt");
        var d = (Aecp.PropertySetDefinition)tr.GetObject(dict.GetAt(teile[0]), OpenMode.ForRead);
        var f = d.Definitions.Cast<Aecp.PropertyDefinition>().FirstOrDefault(x => x.Name == teile[1])
            ?? throw new InvalidDataException($"Themen.csv: Feld {satzFeld} fehlt im Satz");
        return (d.ObjectId, f.Id);
    }

    /// <summary>
    /// Kopfregel und die eine Unterregel auf Satz/Feld umhängen und den Wert setzen. Die eigentliche Bedingung steht in der
    /// Unterregel; der Kopf zeigt in RI 9.1 auf einen alten Satz. Unterregeln nur über den Enumerator (der Index liefert
    /// ImpObject – Befund 24.10.2026).
    /// </summary>
    public static void Umhaengen(AecDb.DisplayThemeComponentBase k, ObjectId satz, int feld, string wert)
    {
        var kopf = k.Rules as Aecp.DisplayThemeScheduleDataRule ?? throw new InvalidOperationException($"Regeltyp {k.Rules?.GetType().Name} nicht umhängbar");
        kopf.SetPropertySetDefinitionId(satz, false);
        kopf.SetPropertyDefinitionId(feld, false);
        var unter = ((System.Collections.IEnumerable)kopf.DisplayThemeRules).Cast<object>().ToList();
        if (unter.Count != 1) throw new InvalidOperationException($"Komponente {k.Name}: {unter.Count} Unterregeln – erwartet 1");
        dynamic u = unter[0];
        u.SetPropertySetDefinitionId(satz, false);
        u.SetPropertyDefinitionId(feld, false);
        u.ComparisonOperator = AecDb.DisplayThemeRuleComparisonOperator.RuleComparisonOperatorEqualTo;
        u.Value = wert;
        k.Rules = kopf;
    }

    /// <summary>Unterregel lesen: (Satz, Feld, Wert) – für die Prüfung.</summary>
    public static (string Satz, string Feld, string Wert)? Regel(AecDb.DisplayThemeComponentBase k)
    {
        if (k.Rules is not Aecp.DisplayThemeScheduleDataRule kopf) return null;
        var u = ((System.Collections.IEnumerable)kopf.DisplayThemeRules).Cast<object>().FirstOrDefault();
        if (u is null) return null;
        dynamic d = u;
        return ((string)d.PropertyOwnerName, (string)d.PropertyName, (string)d.ValueAsString);
    }
}
