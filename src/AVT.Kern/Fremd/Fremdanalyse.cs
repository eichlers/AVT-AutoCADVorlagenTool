using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;

namespace Avt.Kern.Fremd;

/// <summary>Ergebnis einer Analyse: Einteilung, Zuordnung Pfad → Objekt, Referenz und Dauer.</summary>
public sealed record Analyse(List<Einordnung> Funde, IReadOnlyDictionary<string, ObjectId> Objekte, Referenz Referenz,
    string? Fassung, int Objektzahl, TimeSpan Dauer);

/// <summary>
/// Sucht in einer Zeichnung alles, was nicht zur Fassung gehört (Auftrag AMK/Nutzer 23.10.2026: „nicht nur liNear“),
/// und teilt es in a/b/c ein (<see cref="Einteilung"/>). Nur lesen. Gleiche Pfade wie der Inhaltsauszug (Kanon), gleicher
/// Verweisgraph wie das Bereinigen der Vorlage (Bestand).
/// </summary>
internal static class Fremdanalyse
{
    /// <summary>Wörterbuch der Zeichnung mit gemerkten Ausnahmen (b „behalten“): Xrecord mit Paaren Pfad/Grund.</summary>
    public const string AusnahmenSchluessel = "AVT_Ausnahmen";

    public static Analyse Lies(Database db, Transaction tr, string? referenzOrdner)
    {
        var uhr = Stopwatch.StartNew();
        var b = Bestand.Lies(db, tr);
        var pfade = Kanon.Pfade(db, b);
        var fassung = Fassung(db, tr);
        var referenz = Referenz.Fuer(fassung, referenzOrdner);
        var kinder = b.Alle.Values.GroupBy(o => o.Besitzer).ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToList());
        var nod = db.NamedObjectsDictionaryId;

        // XData-Benutzer je RegApp (Name → Objekte).
        var xdata = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in b.Alle.Values)
            foreach (var app in o.XDataApps) (xdata.TryGetValue(app, out var l) ? l : xdata[app] = []).Add(o.Id);

        var kandidaten = new List<Kandidat>();
        var objekte = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var o in b.Alle.Values)
        {
            var art = Art(o, b, nod);
            if (art is null) continue;
            var pfad = pfade[o.Id];
            // Was zur Fassung gehört oder geschützt ist, gar nicht erst auswerten (Zeit beim leisen Hinweis).
            if (referenz.Pfade.Contains(pfad) || Einteilung.IstGeschuetzt(art, o.Name)) continue;
            var teilbaum = Teilbaum(o.Id, kinder);
            var benutzer = art == "RegApp"
                ? (xdata.TryGetValue(o.Name, out var x) ? x : []).Concat(b.Verwiesen.GetValueOrDefault(o.Id) ?? [])
                : teilbaum.SelectMany(t => b.Verwiesen.GetValueOrDefault(t) ?? []);
            // Benutzer außerhalb des eigenen Teilbaums; Besitzer zählen nicht (ein Wörterbuch zeigt auf seine Einträge).
            var besitzer = b.Besitzer(o.Id).ToHashSet();
            var fremdeBenutzer = benutzer.Where(u => !teilbaum.Contains(u) && !besitzer.Contains(u)).Distinct()
                .Select(u => pfade.TryGetValue(u, out var p) ? p : "?").ToList();
            kandidaten.Add(new Kandidat(pfad, art, o.Name, fremdeBenutzer, Spuren(o, art, teilbaum, b, kinder)));
            objekte[pfad] = o.Id;
        }
        var ausnahmen = Ausnahmen(db, tr).Keys.ToHashSet(StringComparer.Ordinal);
        return new Analyse(Einteilung.Einordnen(kandidaten, referenz.Pfade, ausnahmen), objekte, referenz, fassung, b.Alle.Count, uhr.Elapsed);
    }

    /// <summary>Art eines benannten Objekts, das als Fund in Frage kommt; null = kein Kandidat.</summary>
    static string? Art(Objekt o, Bestand b, ObjectId nod)
    {
        if (o.Name == "" && !Einteilung.IstFremdklasse(o.Klasse)) return null;
        switch (o.Klasse)
        {
            case "AcDbBlockTableRecord": return o.Name.StartsWith('*') ? null : "Block";
            case "AcDbLayerTableRecord": return "Layer";
            case "AcDbLinetypeTableRecord": return "Linientyp";
            case "AcDbTextStyleTableRecord": return o.Name == "" ? null : "Textstil";
            case "AcDbDimStyleTableRecord": return "Bemaßungsstil";
            case "AcDbRegAppTableRecord": return "RegApp";
        }
        if (o.Besitzer == nod) return "Wörterbuch";
        // Einträge der AEC-Wörterbücher (Satzdefinitionen, Stile, Systeme, Themen …) – zweite Ebene unter dem NOD.
        if (b.Alle.TryGetValue(o.Besitzer, out var halter) && halter.Besitzer == nod
            && (halter.Name.StartsWith("AEC", StringComparison.OrdinalIgnoreCase) || halter.Name.StartsWith("ACAD_", StringComparison.OrdinalIgnoreCase)))
        {
            return o.Klasse switch
            {
                "AecDbPropertySetDef" => "Satzdefinition",
                "AecDbMvBlockDef" => "Mehransichtsblock",
                _ => "Stil/Definition",
            };
        }
        // Objekte fremder Klassen anderswo (z. B. in Erweiterungswörterbüchern), nur das oberste ihres Zweigs.
        if (Einteilung.IstFremdklasse(o.Klasse) && b.Alle.TryGetValue(o.Besitzer, out var h) && !Einteilung.IstFremdklasse(h.Klasse)
            && h.Klasse.Contains("Dictionary") && o.Besitzer != nod)
            return "Fremdobjekt";
        return null;
    }

    static HashSet<ObjectId> Teilbaum(ObjectId id, Dictionary<ObjectId, List<ObjectId>> kinder)
    {
        var alle = new HashSet<ObjectId> { id };
        var stapel = new Stack<ObjectId>([id]);
        while (stapel.Count > 0)
        {
            foreach (var k in kinder.GetValueOrDefault(stapel.Pop()) ?? [])
                if (alle.Add(k)) stapel.Push(k);
        }
        return alle;
    }

    static List<string> Spuren(Objekt o, string art, HashSet<ObjectId> teilbaum, Bestand b, Dictionary<ObjectId, List<ObjectId>> kinder)
    {
        var s = new List<string>();
        var fremd = teilbaum.Select(t => b.Alle.TryGetValue(t, out var x) ? x.Klasse : "").Where(Einteilung.IstFremdklasse).Distinct().ToList();
        if (fremd.Count > 0) s.Add($"Objekte fremder Klassen ({string.Join(", ", fremd.Take(3))}{(fremd.Count > 3 ? " …" : "")})");
        if (Einteilung.DublettenBasis(o.Name) is { } basis
            && (kinder.GetValueOrDefault(o.Besitzer) ?? []).Any(g => b.Alle.TryGetValue(g, out var x) && x.Name == basis))
            s.Add($"Dublette von „{basis}“");
        if (Einteilung.IstAltname(art, o.Name)) s.Add("alter Stempel-/Legendenblock");
        return s;
    }

    /// <summary>Fassung der Zeichnung: DWGPROPS-Feld &lt;Kürzel&gt;_Fassung, sonst der Fassungsstempel; null = keine.</summary>
    public static string? Fassung(Database db, Transaction tr)
    {
        var e = db.SummaryInfo.CustomProperties;
        while (e.MoveNext())
        {
            if (Convert.ToString(e.Key) == Standard.Feld + "Fassung" && Convert.ToString(e.Value) is { Length: > 0 } f)
                return $"{Standard.Kuerzel}_{f}";
        }
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(Standard.Stempel) && tr.GetObject(nod.GetAt(Standard.Stempel), OpenMode.ForRead) is Xrecord { Data: { } d })
        {
            var w = d.AsArray().Select(t => Convert.ToString(t.Value) ?? "").ToList();
            var i = w.IndexOf("Fassung");
            if (i >= 0 && i + 1 < w.Count && w[i + 1].Length > 0) return $"{Standard.Kuerzel}_{w[i + 1]}";
        }
        return null;
    }

    /// <summary>Gemerkte Ausnahmen der Zeichnung (Pfad → Grund).</summary>
    public static Dictionary<string, string> Ausnahmen(Database db, Transaction tr)
    {
        var aus = new Dictionary<string, string>(StringComparer.Ordinal);
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(AusnahmenSchluessel) || tr.GetObject(nod.GetAt(AusnahmenSchluessel), OpenMode.ForRead) is not Xrecord { Data: { } d }) return aus;
        var w = d.AsArray().Select(t => Convert.ToString(t.Value) ?? "").ToList();
        for (var i = 0; i + 1 < w.Count; i += 2) aus[w[i]] = w[i + 1];
        return aus;
    }
}
