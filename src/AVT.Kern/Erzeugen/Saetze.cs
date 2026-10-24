using System.Collections.Specialized;
using Autodesk.AutoCAD.DatabaseServices;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Avt.Kern.Erzeugen;

/// <summary>Ein Feld aus Feldkatalog.csv.</summary>
public sealed record Feldzeile(string Satz, string Feld, string Anzeigename, string Typ, string Einheit, string Liste, string Vorgabe, string Quelle, bool Pflicht);

/// <summary>Ein Satz aus Saetze.csv.</summary>
public sealed record Satzzeile(string Id, string Satz, bool Stilbasiert, IReadOnlyList<string> GiltFuer, string Beschreibung);

/// <summary>Feldkatalog lesen und prüfen – ohne AutoCAD (Unit-Tests).</summary>
public static class Feldkatalog
{
    public static readonly string[] Typen = ["Text", "Integer", "Real", "JaNein", "Liste", "Auto", "Formel"];

    public static IReadOnlyList<Satzzeile> SaetzeLesen(Tabelle t) => t.Zeilen.Select(z => z.Werte).Select(w => new Satzzeile(
        w["ID"], w["Satz"],
        w["Art"] switch { "stil" => true, "objekt" => false, var a => throw new FormatException($"Saetze.csv: Art „{a}“ – erwartet objekt oder stil") },
        w["GiltFuer"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), w.GetValueOrDefault("Beschreibung") ?? "")).ToList();

    public static IReadOnlyList<Feldzeile> FelderLesen(Tabelle t) => t.Zeilen.Select(z => z.Werte).Select(w => new Feldzeile(
        w["Satz"], w["Feld"], w.GetValueOrDefault("Anzeigename") ?? "", w["Typ"], w.GetValueOrDefault("Einheit") ?? "", w.GetValueOrDefault("Liste") ?? "",
        w.GetValueOrDefault("Vorgabe") ?? "", w.GetValueOrDefault("Quelle") ?? "", Stammwerte.JaNein(w.GetValueOrDefault("Pflicht")))).ToList();

    /// <summary>Wertelisten: Name → Werte in Reihenfolge (Spalte Nr).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ListenLesen(Tabelle t) =>
        t.Zeilen.Select(z => z.Werte).GroupBy(w => w["Liste"], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderBy(w => int.Parse(w["Nr"], System.Globalization.CultureInfo.InvariantCulture)).Select(w => w["Wert"]).ToList(), StringComparer.Ordinal);

    /// <summary>Regeln des Katalogs (Feldkatalog 1): Namen, Typen, Listen, Satzbezug, Formelverweise. Leere Liste = in Ordnung.</summary>
    public static List<string> Fehler(IReadOnlyList<Satzzeile> saetze, IReadOnlyList<Feldzeile> felder, IReadOnlyDictionary<string, IReadOnlyList<string>> listen)
    {
        var aus = new List<string>();
        var satznamen = saetze.Select(s => s.Satz).ToHashSet(StringComparer.Ordinal);
        foreach (var s in saetze)
            if (!s.Satz.StartsWith(Standard.Kuerzel + "_", StringComparison.Ordinal)) aus.Add($"Satz {s.Satz}: Name ohne Vorsilbe {Standard.Kuerzel}_");
        foreach (var g in felder.GroupBy(f => (f.Satz, f.Feld))) if (g.Count() > 1) aus.Add($"{g.Key.Satz}.{g.Key.Feld}: doppelt");
        foreach (var f in felder)
        {
            var wo = $"{f.Satz}.{f.Feld}";
            if (!satznamen.Contains(f.Satz)) aus.Add($"{wo}: Satz fehlt in Saetze.csv");
            if (!System.Text.RegularExpressions.Regex.IsMatch(f.Feld, "^[A-Za-z][A-Za-z0-9]*$")) aus.Add($"{wo}: Feldname nur Buchstaben und Ziffern (Feldkatalog 1.3)");
            if (!Typen.Contains(f.Typ)) aus.Add($"{wo}: Typ „{f.Typ}“ unbekannt");
            if (f.Typ == "Liste" && !listen.ContainsKey(f.Liste)) aus.Add($"{wo}: Liste „{f.Liste}“ fehlt in Wertelisten.csv");
            if (f.Typ == "Liste" && f.Vorgabe.Length > 0 && listen.TryGetValue(f.Liste, out var werte) && !werte.Contains(f.Vorgabe)) aus.Add($"{wo}: Vorgabe „{f.Vorgabe}“ nicht in Liste {f.Liste}");
            if (f.Typ != "Liste" && f.Liste.Length > 0) aus.Add($"{wo}: Liste angegeben, Typ ist {f.Typ}");
            if (f.Typ is "Auto" or "Formel" && f.Quelle.Length == 0) aus.Add($"{wo}: Quelle fehlt");
            if (f.Typ is "Formel")
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(f.Quelle, @"\[([^\]:]+)(?::([^\]]+))?\]"))
                {
                    var (satz, feld) = m.Groups[2].Success ? (m.Groups[1].Value, m.Groups[2].Value) : (f.Satz, m.Groups[1].Value);
                    if (!felder.Any(x => x.Satz == satz && x.Feld == feld)) aus.Add($"{wo}: Formel verweist auf unbekanntes Feld [{satz}:{feld}]");
                }
        }
        return aus;
    }

    /// <summary>Objektklasse, deren automatische Quellen ein stilbasierter Satz nutzt (Befund RI 9.1: StilSchemaRohrKanal → AecbDbSchematic).</summary>
    public static string Objektklasse(string klasse) => klasse.EndsWith("Style", StringComparison.Ordinal) ? klasse[..^"Style".Length] : klasse;
}

/// <summary>
/// AVT 0.4.0 Teil 2: Wertelisten und Eigenschaftssätze aus Saetze.csv, Feldkatalog.csv, Wertelisten.csv anlegen (F1–F10).
/// Feste IDs als XData (Satz: ID aus Saetze.csv, Liste: LI-&lt;Name&gt;). Felder in Katalogreihenfolge; Formeln zuletzt, weil sie
/// auf andere Felder (auch anderer Sätze) verweisen.
/// </summary>
internal static class Saetze
{
    public const string ListenVorsilbe = Standard.Kuerzel + "_";

    // Klasse, für die Wertelisten von Eigenschaftsfeldern gelten (Befund RI 9.1, wird beim Prüfen gegen die Basis bestätigt).
    internal static string ListenKlasse = "AecListUserManualPropertyDef";

    public static List<string> Bauen(Database db, Transaction tr, string stammdaten, Action<DBObject, string> id)
    {
        var aus = new List<string>();
        var saetze = Feldkatalog.SaetzeLesen(Tabelle.Lies(Path.Combine(stammdaten, "Saetze.csv"), "ID", "Satz", "Art", "GiltFuer"));
        var felder = Feldkatalog.FelderLesen(Tabelle.Lies(Path.Combine(stammdaten, "Feldkatalog.csv"), "Satz", "Feld", "Typ"));
        var listen = Feldkatalog.ListenLesen(Tabelle.Lies(Path.Combine(stammdaten, "Wertelisten.csv"), "Liste", "Nr", "Wert"));
        var fehler = Feldkatalog.Fehler(saetze, felder, listen);
        if (fehler.Count > 0) throw new InvalidDataException("Feldkatalog: " + string.Join(" | ", fehler));

        var listIds = Listen(db, tr, listen, id, aus);
        var defs = new Dictionary<string, Aecp.PropertySetDefinition>(StringComparer.Ordinal);
        foreach (var s in saetze) defs[s.Satz] = Satz(db, tr, s, id);
        // 1. Durchgang: alle Felder außer Formeln. 2. Durchgang: Formeln.
        foreach (var formeln in new[] { false, true })
            foreach (var s in saetze)
                foreach (var f in felder.Where(x => x.Satz == s.Satz && (x.Typ == "Formel") == formeln))
                    Feld(db, defs[s.Satz], s, f, listIds, aus);
        foreach (var s in saetze) aus.Add($"AVTERZEUGEN Satz | {s.Satz} | {(s.Stilbasiert ? "stilbasiert" : "am Objekt")} | {defs[s.Satz].Definitions.Count} Felder");
        return aus;
    }

    static Dictionary<string, ObjectId> Listen(Database db, Transaction tr, IReadOnlyDictionary<string, IReadOnlyList<string>> listen, Action<DBObject, string> id, List<string> aus)
    {
        var dict = new AecDb.DictionaryListDefinition(db);
        var ids = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var (name, werte) in listen)
        {
            var voll = ListenVorsilbe + name;
            if (dict.Has(voll, tr)) throw new InvalidOperationException($"Liste {voll} gibt es schon – Basis nicht leer?");
            var l = new AecDb.ListDefinition();
            l.SetToStandard(db);
            l.SubSetDatabaseDefaults(db);
            l.AppliesToFilter = new StringCollection { ListenKlasse };
            dict.AddNewRecord(voll, l);
            tr.AddNewlyCreatedDBObject(l, true);
            foreach (var w in werte) l.AddListItem(w);
            id(l, "LI-" + name);
            ids[name] = l.ObjectId;
            aus.Add($"AVTERZEUGEN Liste | {voll} | {werte.Count} Werte");
        }
        // MEP legt beim Zugriff auf das Listenwörterbuch eine Standardliste mit leerem Verweis an – derselbe AUDIT-Fehler wie in
        // der Leervorlage (B1, Befund 24.10.2026). Sie gehört nicht zum Standard und wird entfernt.
        if (dict.Has("Standard", tr) && !ids.Values.Contains(dict.GetAt("Standard")))
        {
            tr.GetObject(dict.GetAt("Standard"), OpenMode.ForWrite).Erase();
            aus.Add("AVTERZEUGEN Liste | Standard (von MEP angelegt, leer) entfernt");
        }
        return ids;
    }

    static Aecp.PropertySetDefinition Satz(Database db, Transaction tr, Satzzeile s, Action<DBObject, string> id)
    {
        var dict = new Aecp.DictionaryPropertySetDefinitions(db);
        if (dict.Has(s.Satz, tr)) throw new InvalidOperationException($"Satz {s.Satz} gibt es schon – Basis nicht leer?");
        var d = new Aecp.PropertySetDefinition();
        d.SetToStandard(db);
        d.SubSetDatabaseDefaults(db);
        d.AlternateName = s.Satz;
        d.Description = s.Beschreibung;
        var filter = new StringCollection();
        filter.AddRange(s.GiltFuer.ToArray());
        d.SetAppliesToFilter(filter, s.Stilbasiert);
        dict.AddNewRecord(s.Satz, d);
        tr.AddNewlyCreatedDBObject(d, true);
        id(d, s.Id);
        return d;
    }

    static void Feld(Database db, Aecp.PropertySetDefinition d, Satzzeile s, Feldzeile f, IReadOnlyDictionary<string, ObjectId> listen, List<string> aus)
    {
        var beschreibung = f.Einheit.Length > 0 ? $"{f.Anzeigename} [{f.Einheit}]" : f.Anzeigename;
        if (f.Typ == "Formel")
        {
            var fo = new Aecp.PropertyDefinitionFormula();
            fo.SetToStandard(db);
            fo.SubSetDatabaseDefaults(db);
            fo.Name = f.Feld;
            fo.Description = beschreibung;
            d.Definitions.Add(fo);
            if (!fo.SetFormulaString(f.Quelle)) throw new InvalidDataException($"{f.Satz}.{f.Feld}: Formel abgelehnt: {f.Quelle}");
            return;
        }
        var p = new Aecp.PropertyDefinition();
        p.SetToStandard(db);
        p.SubSetDatabaseDefaults(db);
        p.Name = f.Feld;
        p.Description = beschreibung;
        switch (f.Typ)
        {
            case "Auto":
                p.Automatic = true;
                var gesetzt = 0;
                foreach (var klasse in s.GiltFuer.Select(Feldkatalog.Objektklasse).Distinct())
                {
                    var quellen = Aecp.PropertyDataServices.FindAutomaticSourceNames(klasse, db) ?? [];
                    if (!quellen.Contains(f.Quelle)) continue;
                    p.SetAutomaticData(klasse, f.Quelle);
                    gesetzt++;
                }
                if (gesetzt == 0) throw new InvalidDataException($"{f.Satz}.{f.Feld}: automatische Quelle „{f.Quelle}“ gibt es für {string.Join(",", s.GiltFuer)} nicht");
                break;
            case "Liste":
                p.DataType = Autodesk.Aec.PropertyData.DataType.List;
                p.ListDefinitionId = listen[f.Liste];
                if (f.Vorgabe.Length > 0) p.DefaultData = f.Vorgabe;
                break;
            case "JaNein":
                p.DataType = Autodesk.Aec.PropertyData.DataType.TrueFalse;
                p.DefaultData = Stammwerte.JaNein(f.Vorgabe);
                break;
            case "Integer":
                p.DataType = Autodesk.Aec.PropertyData.DataType.Integer;
                p.DefaultData = f.Vorgabe.Length > 0 ? int.Parse(f.Vorgabe, System.Globalization.CultureInfo.InvariantCulture) : 0;
                break;
            case "Real":
                p.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                p.DefaultData = f.Vorgabe.Length > 0 ? Stammwerte.Zahl(f.Vorgabe) : 0.0;
                break;
            default:
                p.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                p.DefaultData = f.Vorgabe;
                break;
        }
        d.Definitions.Add(p);
    }
}
