using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;

namespace Avt.Kern.Fremd;

/// <summary>
/// Ergebnis einer Analyse: Einteilung, Zuordnung Pfad → Objekt, Referenz, Dauer. <see cref="Luecke"/> &gt; 0: der
/// Verweisgraph ist unvollständig (nicht lesbare Objekte oder DwgOut-Fehler) – dann bietet AVT nichts zum Entfernen an.
/// </summary>
public sealed record Analyse(List<Einordnung> Funde, IReadOnlyDictionary<string, ObjectId> Objekte, Referenz Referenz,
    string? Fassung, int Objektzahl, TimeSpan Dauer, int Luecke = 0);

/// <summary>
/// Sucht in einer Zeichnung alles, was nicht zur Fassung gehört (Auftrag AMK/Nutzer 23.10.2026: „nicht nur liNear“),
/// und teilt es in a/b/c ein (<see cref="Einteilung"/>). Nur lesen. Gleiche Pfade wie der Inhaltsauszug (Kanon), gleicher
/// Verweisgraph wie das Bereinigen der Vorlage (Bestand) – ergänzt um Benutzungen, die der Zeigergraph nicht sieht
/// (Review 0.3.0): XData-Handles (1005), Kopfvariablen (aktueller Layer, Textstil …), Satz-Namen in Texten
/// („Satz:Feld“ in Beschriftungen, Formeln, Feldern) und Fremddaten an Bauteilen.
/// </summary>
internal static class Fremdanalyse
{
    /// <summary>Wörterbuch der Zeichnung mit gemerkten Ausnahmen (b „behalten“): Xrecord mit Paaren Pfad/Grund.</summary>
    public const string AusnahmenSchluessel = "AVT_Ausnahmen";

    const string Kopf = "Kopfvariable";

    public static Analyse Lies(Database db, Transaction tr, string? referenzOrdner)
    {
        var uhr = Stopwatch.StartNew();
        var b = Bestand.Lies(db, tr, mitTexten: true);
        var pfade = Kanon.Pfade(db, b);
        var fassung = Fassung(db, tr);
        var referenz = Referenz.Fuer(fassung, referenzOrdner);
        var kinder = b.Alle.Values.GroupBy(o => o.Besitzer).ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToList());
        var nod = db.NamedObjectsDictionaryId;

        // Weitere Benutzer: XData-Handles und Kopfvariablen (Ziel → Benutzerpfade).
        var extra = new Dictionary<ObjectId, List<string>>();
        void Extra(ObjectId ziel, string wer) { if (!ziel.IsNull) (extra.TryGetValue(ziel, out var l) ? l : extra[ziel] = []).Add(wer); }
        foreach (var o in b.Alle.Values)
            foreach (var z in o.XDataZiele ?? []) Extra(z, pfade[o.Id]);
        foreach (var (name, id) in KopfVerweise(db)) Extra(id, $"{Kopf} {name}");

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
            // Was zur Fassung gehört oder geschützt ist, gar nicht erst auswerten.
            if (referenz.Pfade.Contains(pfad) || Einteilung.IstGeschuetzt(art, o.Name)) continue;
            var teilbaum = Teilbaum(o.Id, kinder);
            var besitzer = b.Besitzer(o.Id).ToHashSet();
            var zeigerBenutzer = art == "RegApp"
                ? (xdata.TryGetValue(o.Name, out var x) ? x : []).Concat(b.Verwiesen.GetValueOrDefault(o.Id) ?? [])
                : teilbaum.SelectMany(t => b.Verwiesen.GetValueOrDefault(t) ?? []);
            // Benutzer außerhalb des eigenen Teilbaums; Besitzer zählen nicht (ein Wörterbuch zeigt auf seine Einträge).
            var benutzer = zeigerBenutzer.Where(u => !teilbaum.Contains(u) && !besitzer.Contains(u)).Distinct()
                .Select(u => pfade.TryGetValue(u, out var p) ? p : "?").ToList();
            benutzer.AddRange(teilbaum.SelectMany(t => extra.GetValueOrDefault(t) ?? []).Where(p => !p.StartsWith(pfad + "/", StringComparison.Ordinal)));
            if (art == "Satzdefinition") benutzer.AddRange(NamensBenutzer(o.Name, b, pfade, teilbaum));
            // Fremddaten an einem Bauteil: das Bauteil benutzt sie (keine Übernahme nach EHS_Bauteil in 0.3.0 – nie still löschen).
            var traeger = art == "Fremdobjekt" ? b.Besitzer(o.Id).FirstOrDefault(x => b.Alle.TryGetValue(x, out var t) && t.Element) : ObjectId.Null;
            if (!traeger.IsNull) benutzer.Add(pfade[traeger]);
            var spuren = Spuren(o, art, teilbaum, b, kinder);
            if (!traeger.IsNull) spuren.Add($"Daten am Bauteil {pfade[traeger]}");
            kandidaten.Add(new Kandidat(pfad, art, o.Name, benutzer.Distinct().ToList(), spuren));
            objekte[pfad] = o.Id;
        }
        var ausnahmen = Ausnahmen(db, tr).Keys.ToHashSet(StringComparer.Ordinal);
        var luecke = b.Unlesbar + b.OhneZeiger.Count;
        return new Analyse(Einteilung.Einordnen(kandidaten, referenz.Pfade, ausnahmen), objekte, referenz, fassung, b.Alle.Count, uhr.Elapsed, luecke);
    }

    /// <summary>Was die Kopfvariablen der Zeichnung benutzen – der Zeigergraph sieht sie nicht.</summary>
    static IEnumerable<(string Name, ObjectId Id)> KopfVerweise(Database db)
    {
        (string, Func<ObjectId>)[] werte =
        [
            ("CLAYER", () => db.Clayer), ("TEXTSTYLE", () => db.Textstyle), ("CELTYPE", () => db.Celtype),
            ("DIMSTYLE", () => db.Dimstyle), ("CMLEADERSTYLE", () => db.MLeaderstyle), ("CTABLESTYLE", () => db.Tablestyle),
            ("CMLSTYLE", () => db.CmlstyleID),
        ];
        foreach (var (name, lies) in werte)
        {
            ObjectId id;
            try { id = lies(); } catch (System.Exception) { continue; }
            if (!id.IsNull) yield return (name, id);
        }
    }

    /// <summary>Objekte, deren Texte den Satznamen als „Name:“ nennen (Beschriftungen, Formeln, Felder, Bauteiltabellen).</summary>
    static IEnumerable<string> NamensBenutzer(string satz, Bestand b, Dictionary<ObjectId, string> pfade, HashSet<ObjectId> teilbaum) =>
        b.Texte.Where(t => !teilbaum.Contains(t.Key) && t.Value.Any(x => x.Contains(satz + ":", StringComparison.OrdinalIgnoreCase)))
            .Select(t => pfade.TryGetValue(t.Key, out var p) ? p : "?");

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

    /// <summary>
    /// Schnelle Vorprüfung für den leisen Hinweis (Review 0.3.0: die volle Analyse kann bei großen Zeichnungen spürbar
    /// dauern): nur Namen der Tabellen und der ersten zwei Wörterbuch-Ebenen gegen die Referenz, ohne DwgOut. Liefert die
    /// Zahl der Einträge, die nicht zur Fassung gehören (ob benutzt, zeigt erst die Palette).
    /// </summary>
    public static (int Anzahl, string Referenz) Vorpruefung(Database db, Transaction tr, string? referenzOrdner)
    {
        var referenz = Referenz.Fuer(Fassung(db, tr), referenzOrdner);
        var ausnahmen = Ausnahmen(db, tr);
        var gefunden = 0;
        void Pruefe(string art, string name, string pfad)
        {
            if (!referenz.Pfade.Contains(pfad) && !ausnahmen.ContainsKey(pfad) && !Einteilung.IstGeschuetzt(art, name)) gefunden++;
        }
        (ObjectId Tabelle, string Art)[] tabellen =
        [
            (db.BlockTableId, "Block"), (db.LayerTableId, "Layer"), (db.LinetypeTableId, "Linientyp"),
            (db.TextStyleTableId, "Textstil"), (db.DimStyleTableId, "Bemaßungsstil"), (db.RegAppTableId, "RegApp"),
        ];
        foreach (var (tabelleId, art) in tabellen)
        {
            var t = (SymbolTable)tr.GetObject(tabelleId, OpenMode.ForRead);
            var vorn = t.GetRXClass().Name + "#0/";
            foreach (ObjectId id in t)
            {
                var r = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (r.Name.Length == 0 || (art == "Block" && r.Name.StartsWith('*'))) continue;
                Pruefe(art, r.Name, vorn + Kanon.Esc(r.Name).Replace('/', '∕'));
            }
        }
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        foreach (var e in nod)
        {
            var pfad = "NOD/" + Kanon.Esc(e.Key).Replace('/', '∕');
            Pruefe("Wörterbuch", e.Key, pfad);
            if (!(e.Key.StartsWith("AEC", StringComparison.OrdinalIgnoreCase) || e.Key.StartsWith("ACAD_", StringComparison.OrdinalIgnoreCase))) continue;
            if (tr.GetObject(e.Value, OpenMode.ForRead) is not DBDictionary unter) continue;
            foreach (var u in unter)
            {
                if (u.Key.StartsWith('*')) continue;
                var art = u.Value.ObjectClass?.Name == "AecDbPropertySetDef" ? "Satzdefinition" : "Stil/Definition";
                Pruefe(art, u.Key, pfad + "/" + Kanon.Esc(u.Key).Replace('/', '∕'));
            }
        }
        return (gefunden, referenz.Fassung);
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
