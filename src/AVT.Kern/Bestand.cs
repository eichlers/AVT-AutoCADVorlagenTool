using Autodesk.AutoCAD.DatabaseServices;
using AecRec = Autodesk.Aec.DatabaseServices.DictionaryRecord;

namespace Avt.Kern;

/// <summary>Ein Objekt der Zeichnung mit Klasse, Besitzer, Name und den Verweisen, die es beim DwgOut schreibt.</summary>
internal sealed record Objekt(ObjectId Id, string Klasse, ObjectId Besitzer, string Name, IReadOnlyList<ObjectId> Zeiger,
    bool Element, IReadOnlyList<string> XDataApps, IReadOnlyList<ObjectId>? XDataZiele = null)
{
    public string Handle => Id.Handle.ToString();
}

/// <summary>
/// Vollständige Bestandsaufnahme über alle Handles der Zeichnung (nur lesen): jedes Objekt, sein Besitzer, sein Name
/// (Tabelleneintrag, AEC-Stil oder Wörterbuchschlüssel) und seine Zeiger auf andere Objekte (ohne Besitz).
/// </summary>
internal sealed class Bestand
{
    public Dictionary<ObjectId, Objekt> Alle { get; } = [];
    /// <summary>Wer zeigt auf wen (ohne Besitzverweise).</summary>
    public Dictionary<ObjectId, List<ObjectId>> Verwiesen { get; } = [];
    public int Unlesbar { get; private set; }
    /// <summary>Objekte, deren DwgOut scheiterte – ihre Zeiger fehlen im Graphen (Bereinigen bricht ab, wenn eines bleibt).</summary>
    public List<ObjectId> OhneZeiger { get; } = [];
    /// <summary>
    /// Nur mit <c>mitTexten</c>: Texte je Objekt, die auf Namen verweisen können („Satz:Feld“, „[Satz:Feld]“) – für die
    /// Fremdanalyse (Verweise über Namen sieht der Zeigergraph nicht; Review 0.3.0).
    /// </summary>
    public Dictionary<ObjectId, List<string>> Texte { get; } = [];

    public static Bestand Lies(Database db, Transaction tr, bool mitTexten = false)
    {
        var b = new Bestand();
        var schluessel = new Dictionary<ObjectId, string>();
        var ende = db.Handseed.Value;
        for (long h = 1; h < ende; h++)
        {
            if (!db.TryGetObjectId(new Handle(h), out var id) || id.IsNull || id.IsErased) continue;
            DBObject o;
            try { o = tr.GetObject(id, OpenMode.ForRead, false); }
            catch (System.Exception) { b.Unlesbar++; continue; }
            if (o is DBDictionary d)
            {
                foreach (var e in d) schluessel.TryAdd(e.Value, e.Key);
            }
            var fang = new Zeigerfang();
            try { o.DwgOut(fang); } catch (System.Exception) { b.OhneZeiger.Add(id); }
            var zeiger = fang.Verweise.Where(v => !v.Besitz && v.Ziel != o.OwnerId && v.Ziel != id)
                .Select(v => v.Ziel).Distinct().ToList();
            var (apps, xziele) = XData(db, o);
            b.Alle[id] = new Objekt(id, Klasse(o), o.OwnerId, Name(o), zeiger, o is Entity and not BlockBegin and not BlockEnd, apps, xziele);
            if (mitTexten)
            {
                var t = fang.Texte.Where(x => x.Contains(':')).ToList();
                if (t.Count > 0) b.Texte[id] = t;
            }
        }
        foreach (var (id, o) in b.Alle.ToList())
        {
            if (o.Name == "" && schluessel.TryGetValue(id, out var k)) b.Alle[id] = o with { Name = k };
            foreach (var z in o.Zeiger)
            {
                (b.Verwiesen.TryGetValue(z, out var l) ? l : b.Verwiesen[z] = []).Add(id);
            }
        }
        return b;
    }

    public static string Klasse(DBObject o) => o.GetRXClass().Name;

    /// <summary>
    /// Anwendungsnamen der XData am Objekt (Gruppencode 1001) und Objekte, auf die XData-Handles (1005) zeigen – z. B.
    /// Bemaßungs-Overrides (DIMBLK, DIMLTYPE) oder Rückverweise dynamischer Blöcke. Die Handles zählen NICHT im Zeigergraphen
    /// (Vorlagen-Bereinigen unverändert), nur als <see cref="Objekt.XDataZiele"/> für die Fremdanalyse.
    /// </summary>
    static (IReadOnlyList<string> Apps, IReadOnlyList<ObjectId> Ziele) XData(Database db, DBObject o)
    {
        try
        {
            using var rb = o.XData;
            if (rb is null) return ([], []);
            var werte = rb.AsArray();
            var apps = werte.Where(t => t.TypeCode == 1001).Select(t => (string)t.Value).Distinct().ToList();
            var ziele = new List<ObjectId>();
            foreach (var t in werte.Where(t => t.TypeCode == 1005))
            {
                try
                {
                    var h = new Handle(Convert.ToInt64(Convert.ToString(t.Value), 16));
                    if (db.TryGetObjectId(h, out var z) && !z.IsNull && !z.IsErased) ziele.Add(z);
                }
                catch (System.Exception) { /* kein gültiger Handle – zählt nicht */ }
            }
            return (apps, ziele);
        }
        catch (System.Exception) { return ([], []); }
    }

    static string Name(DBObject o)
    {
        try
        {
            return o switch
            {
                SymbolTableRecord s => s.Name,
                Layout l => l.LayoutName,
                AecRec r => r.Name ?? "",
                _ => "",
            };
        }
        catch (System.Exception) { return ""; }
    }

    /// <summary>Besitzerkette eines Objekts (ohne das Objekt selbst), endet am Wurzelobjekt.</summary>
    public IEnumerable<ObjectId> Besitzer(ObjectId id)
    {
        var gesehen = new HashSet<ObjectId>();
        while (Alle.TryGetValue(id, out var o) && Alle.ContainsKey(o.Besitzer) && gesehen.Add(o.Besitzer))
        {
            id = o.Besitzer;
            yield return id;
        }
    }

    /// <summary>Lesbare Bezeichnung „Klasse Name [Handle]“, bei Objekten ohne Namen mit dem Besitzer.</summary>
    public string Text(ObjectId id)
    {
        if (!Alle.TryGetValue(id, out var o)) return $"? [{id.Handle}]";
        if (o.Name != "") return $"{o.Klasse} „{o.Name}“ [{o.Handle}]";
        var tr = Besitzer(id).Select(b => Alle[b]).FirstOrDefault(b => b.Name != "");
        return tr is null ? $"{o.Klasse} [{o.Handle}]" : $"{o.Klasse} [{o.Handle}] in {tr.Klasse} „{tr.Name}“";
    }
}
