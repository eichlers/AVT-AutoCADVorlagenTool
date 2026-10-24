using Avt.Kern.Fremd;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecb = Autodesk.Aec.Building.DatabaseServices;

namespace Avt.Kern.Erzeugen;

/// <summary>Angaben eines Bauauftrags, die nicht in den Stammdaten stehen (Fassung, Herkunft) – alles ohne Datum, damit gleiche Angaben gleiche Vorlagen ergeben.</summary>
public sealed record Bauauftrag(string Fassung, string Vorgaenger, string StammdatenStand, string Basis, string AvtVersion);

/// <summary>
/// AVT 0.4.0 Teil 1 „Erzeugen“ (Auftrag AMK 23.10.2026): baut auf der offenen Leervorlage (Kopie) aus den Stammdaten:
/// Autodesk-Fachinhalt entfernen (Basis_entfernen.csv), Textstile (nur Arial), Linientypen, Layer, Systeme mit
/// Layer-Schlüsseln, feste IDs (XData RegApp), Fassungsstempel und DWGPROPS. Satzdefinitionen und Symbole folgen.
/// Schreibt nur in die offene Datenbank; Speichern macht der Aufrufer (nur unter C:\Temp).
/// </summary>
internal static class Erzeuger
{
    public const string LayerSchluesselstil = Standard.Kuerzel;

    public static List<string> Bauen(Database db, string stammdaten, string systemeCsv, Bauauftrag auftrag)
    {
        var aus = new List<string>();
        var standard = Tabelle.Lies(Path.Combine(stammdaten, "Standard.csv"), "Schluessel", "Wert").Zeilen
            .ToDictionary(z => z.Werte["Schluessel"], z => z.Werte["Wert"], StringComparer.Ordinal);
        if (standard.GetValueOrDefault("Kuerzel") != Standard.Kuerzel)
            throw new InvalidDataException($"Standard.csv: Kürzel „{standard.GetValueOrDefault("Kuerzel")}“ passt nicht zu AVT ({Standard.Kuerzel})");

        aus.AddRange(BasisEntfernen(db, Tabelle.Lies(Path.Combine(stammdaten, "Basis_entfernen.csv"), "Muster", "Grund")));
        using (var tr = db.TransactionManager.StartTransaction())
        {
            RegApp(db, tr);
            aus.AddRange(Textstile(db, tr, Tabelle.Lies(Path.Combine(stammdaten, "Textstile.csv"), "ID", "Name", "Schrift", "Hoehe_mm")));
            tr.Commit();
        }
        aus.AddRange(Linientypen(db, stammdaten, Tabelle.Lies(Path.Combine(stammdaten, "Linientypen.csv"), "ID", "Name", "Quelle")));
        using (var tr = db.TransactionManager.StartTransaction())
        {
            aus.AddRange(Layer(db, tr, Tabelle.Lies(Path.Combine(stammdaten, "Layer.csv"), "ID", "Name", "Farbe", "Linientyp", "Linienstaerke", "Plotten")));
            aus.AddRange(Systeme(db, tr, Systemtabelle(systemeCsv), standard));
            Stempel(db, tr, auftrag);
            tr.Commit();
        }
        using (var tr = db.TransactionManager.StartTransaction())
        {
            aus.AddRange(Saetze.Bauen(db, tr, stammdaten, Id));
            tr.Commit();
        }
        aus.AddRange(Bausteine.Bauen(db, stammdaten, Id));
        using (var tr = db.TransactionManager.StartTransaction())
        {
            aus.AddRange(Themen.Bauen(db, tr, stammdaten, Id));
            tr.Commit();
        }
        aus.AddRange(Layouts.Bauen(db, stammdaten, Id));
        return aus;
    }

    /// <summary>
    /// Abschließen am als Dokument geöffneten Zwischenstand (AVTABSCHLIESSEN): Inhaltsauszug bilden und mit den DWGPROPS
    /// (Pflichtfelder samt EHS_Inhaltspruefsumme) versehen. Die Angaben des Bauauftrags kommen aus dem Fassungsstempel.
    /// Grund: Beim Speichern und beim Öffnen als Dokument ergänzt AutoCAD Objekte – die Prüfsumme gilt für die Datei, wie
    /// sie geöffnet wird (Befund 23.10.2026: Seitendatenbank und Dokument unterschieden sich).
    /// </summary>
    public static string Abschliessen(Database db, string stammdaten)
    {
        var standard = Tabelle.Lies(Path.Combine(stammdaten, "Standard.csv"), "Schluessel", "Wert").Zeilen
            .ToDictionary(z => z.Werte["Schluessel"], z => z.Werte["Wert"], StringComparer.Ordinal);
        string gesamt;
        Bauauftrag auftrag;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var stempel = Zeichnungsangaben.FassungLesen(db, tr).Select(z => z.Split(" = ", 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
            string W(string k) => stempel.TryGetValue(k, out var v) ? v : throw new InvalidDataException($"Fassungsstempel: {k} fehlt");
            auftrag = new Bauauftrag(W("Fassung"), W("Vorgaenger"), W("Stammdaten"), W("Basis"), W("AVT"));
            var b = Bestand.Lies(db, tr);
            gesamt = Kanon.Gesamt(Kanon.Lies(db, tr, b, []));
            tr.Commit();
        }
        DwgProps(db, standard, auftrag, gesamt);
        return gesamt;
    }

    /// <summary>Zwischenstand speichern (ohne Inhaltsprüfsumme – die setzt <see cref="Abschliessen"/>).</summary>
    public static string Speichern(Database db, string ziel, string stammdaten, Bauauftrag auftrag)
    {
        db.SaveAs(ziel, DwgVersion.Current);
        return "folgt mit AVTABSCHLIESSEN";
    }

    /// <summary>Autodesk-Fachinhalt nach Muster entfernen – nur, was nach dem Verweisgraph unbenutzt ist; Purge prüft zusätzlich.</summary>
    static List<string> BasisEntfernen(Database db, Tabelle muster)
    {
        var aus = new List<string>();
        using var tr = db.TransactionManager.StartTransaction();
        var b = Bestand.Lies(db, tr);
        if (b.Unlesbar + b.OhneZeiger.Count > 0) throw new InvalidOperationException($"Basis: Verweisgraph unvollständig ({b.Unlesbar + b.OhneZeiger.Count}) – nichts entfernt");
        var pfade = Kanon.Pfade(db, b, tr);
        var kinder = b.Alle.Values.GroupBy(o => o.Besitzer).ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToList());
        var extra = new Dictionary<ObjectId, List<ObjectId>>();
        foreach (var o in b.Alle.Values)
            foreach (var z in o.XDataZiele ?? []) (extra.TryGetValue(z, out var l) ? l : extra[z] = []).Add(o.Id);
        var kopf = Fremdanalyse.KopfVerweise(db).Select(k => k.Id).ToHashSet();

        var ziele = new Dictionary<ObjectId, (string Pfad, string Muster, HashSet<ObjectId> Teilbaum)>();
        foreach (var (id, pfad) in pfade)
        {
            var m = muster.Zeilen.Select(z => z.Werte["Muster"]).FirstOrDefault(x => Stammwerte.Passt(x, pfad));
            if (m != null) ziele[id] = (pfad, m, Fremdanalyse.Teilbaum(id, kinder));
        }
        // Fixpunkt: ein Ziel bleibt, wenn etwas außerhalb aller Ziele es (oder etwas in ihm) benutzt.
        var behalten = new Dictionary<ObjectId, string>();
        bool weiter;
        do
        {
            weiter = false;
            var alleTeile = ziele.Values.SelectMany(z => z.Teilbaum).ToHashSet();
            foreach (var (id, z) in ziele.ToList())
            {
                var besitzer = b.Besitzer(id).ToHashSet();
                var benutzer = z.Teilbaum.SelectMany(t => (b.Verwiesen.GetValueOrDefault(t) ?? []).Concat(extra.GetValueOrDefault(t) ?? []))
                    .Where(u => !alleTeile.Contains(u) && !besitzer.Contains(u)).ToList();
                var grund = z.Teilbaum.Any(kopf.Contains) ? "Kopfvariable" : benutzer.Count > 0 ? (pfade.GetValueOrDefault(benutzer[0]) ?? "?") : null;
                if (grund == null) continue;
                ziele.Remove(id);
                behalten[id] = $"{z.Pfad} | benutzt von {grund}";
                weiter = true;
            }
        } while (weiter);
        var (weg, uebrig, runden) = Entfernen.InRunden(db, tr, ziele.ToDictionary(z => z.Value.Pfad, z => (z.Key, true), StringComparer.Ordinal));
        tr.Commit();
        foreach (var g in muster.Zeilen.Select(z => z.Werte["Muster"]))
            aus.Add($"AVTERZEUGEN Basis entfernt | {g} | {weg.Count(p => Stammwerte.Passt(g, p))}");
        foreach (var t in behalten.Values) aus.Add($"AVTERZEUGEN Basis bleibt | {t}");
        foreach (var p in uebrig) aus.Add($"AVTERZEUGEN Basis bleibt | {p} | Purge gibt es nicht frei");
        aus.Add($"AVTERZEUGEN Basis | entfernt {weg.Count} in {runden} Runden | bleibt {behalten.Count + uebrig.Count}");
        return aus;
    }

    static void RegApp(Database db, Transaction tr)
    {
        var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (apps.Has(Standard.RegApp)) return;
        apps.UpgradeOpen();
        var r = new RegAppTableRecord { Name = Standard.RegApp };
        apps.Add(r); tr.AddNewlyCreatedDBObject(r, true);
    }

    /// <summary>Feste ID als XData unter der RegApp des Standards („ID:TS-025“).</summary>
    static void Id(DBObject o, string id) =>
        o.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, Standard.RegApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ID:" + id));

    static List<string> Textstile(Database db, Transaction tr, Tabelle t)
    {
        var aus = new List<string>();
        var tabelle = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
        var eigene = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, w) in t.Zeilen)
        {
            if (!w["Schrift"].Equals("Arial", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Textstile.csv: {w["Name"]} mit Schrift {w["Schrift"]} – nur Arial erlaubt (Nutzer 23.10.2026)");
            var s = tabelle.Has(w["Name"]) ? (TextStyleTableRecord)tr.GetObject(tabelle[w["Name"]], OpenMode.ForWrite) : null;
            if (s == null)
            {
                s = new TextStyleTableRecord { Name = w["Name"] };
                tabelle.Add(s); tr.AddNewlyCreatedDBObject(s, true);
            }
            // L9: Fett als Arial Bold (TrueType, im PDF durchsuchbar); L8: Rollen annotativ, Höhe = Papierhöhe.
            var fett = Stammwerte.JaNein(w.GetValueOrDefault("Fett"));
            var annotativ = Stammwerte.JaNein(w.GetValueOrDefault("Annotativ"));
            s.FileName = "";
            s.Font = new FontDescriptor("Arial", fett, false, 0, 0);
            s.TextSize = Stammwerte.Zahl(w["Hoehe_mm"]);
            s.XScale = w.TryGetValue("Breitenfaktor", out var bf) && bf.Length > 0 ? Stammwerte.Zahl(bf) : 1.0;
            s.Annotative = annotativ ? AnnotativeStates.True : AnnotativeStates.False;
            Id(s, w["ID"]);
            eigene.Add(w["Name"]);
            aus.Add($"AVTERZEUGEN Textstil | {w["Name"]} | Arial{(fett ? " Bold" : "")} {s.TextSize} mm{(annotativ ? " annotativ" : "")}");
        }
        // Jeder andere Textstil (aus der Leervorlage) wird Arial – Texte sollen im PDF durchsuchbar sein.
        foreach (ObjectId id in tabelle)
        {
            var s = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (s.Name.Length == 0 || s.IsShapeFile || eigene.Contains(s.Name)) continue;
            if (Stammwerte.IstArial(s.Font.TypeFace, s.FileName)) continue;
            var alt = string.IsNullOrEmpty(s.Font.TypeFace) ? s.FileName : s.Font.TypeFace;
            s.UpgradeOpen();
            s.FileName = "";
            s.Font = new FontDescriptor("Arial", false, false, 0, 0);
            aus.Add($"AVTERZEUGEN Textstil der Basis auf Arial | {s.Name} | vorher {alt}");
        }
        return aus;
    }

    static List<string> Linientypen(Database db, string stammdaten, Tabelle t)
    {
        var aus = new List<string>();
        foreach (var (_, w) in t.Zeilen)
        {
            var quelle = w["Quelle"].Equals("acadiso.lin", StringComparison.OrdinalIgnoreCase)
                ? HostApplicationServices.Current.FindFile("acadiso.lin", db, FindFileHint.Default)
                : Path.Combine(stammdaten, w["Quelle"]);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tab = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (!tab.Has(w["Name"])) db.LoadLineTypeFile(w["Name"], quelle);
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tab = (LinetypeTableRecord)tr.GetObject(((LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))[w["Name"]], OpenMode.ForWrite);
                Id(tab, w["ID"]);
                tr.Commit();
            }
            aus.Add($"AVTERZEUGEN Linientyp | {w["Name"]} | aus {Path.GetFileName(quelle)}");
        }
        return aus;
    }

    static Color Farbwert(Farbe f) => f.Rgb ? Color.FromRgb(f.R, f.G, f.B) : Color.FromColorIndex(ColorMethod.ByAci, f.Index);

    static ObjectId LinientypId(Database db, Transaction tr, string name)
    {
        var tab = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        return tab.Has(name) ? tab[name] : throw new InvalidDataException($"Linientyp „{name}“ fehlt – in Linientypen.csv aufnehmen");
    }

    static List<string> Layer(Database db, Transaction tr, Tabelle t)
    {
        var aus = new List<string>();
        var tab = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        foreach (var (_, w) in t.Zeilen)
        {
            var l = tab.Has(w["Name"]) ? (LayerTableRecord)tr.GetObject(tab[w["Name"]], OpenMode.ForWrite) : null;
            if (l == null)
            {
                l = new LayerTableRecord { Name = w["Name"] };
                tab.Add(l); tr.AddNewlyCreatedDBObject(l, true);
            }
            l.Color = Farbwert(Farbe.Lies(w["Farbe"]));
            l.LinetypeObjectId = LinientypId(db, tr, w["Linientyp"]);
            l.LineWeight = (LineWeight)Stammwerte.Linienstaerke(w["Linienstaerke"]);
            l.IsPlottable = !w["Plotten"].Equals("nein", StringComparison.OrdinalIgnoreCase);
            l.Description = w.GetValueOrDefault("Beschreibung") ?? "";
            Id(l, w["ID"]);
            aus.Add($"AVTERZEUGEN Layer | {w["Name"]}");
        }
        return aus;
    }

    /// <summary>
    /// Systeme: je Zeile ein Schemasystem (Name, Kürzel, Gruppe, Layer-Schlüssel = Kürzel) und ein Schlüssel im
    /// Layer-Schlüsselstil des Standards (Layername nach Standard.csv „Systemlayer“, Farbe/Linientyp/Linienstärke aus dem
    /// Thema Medien). Der Stil enthält nur diese Schlüssel (keine Autodesk-Standardschlüssel).
    /// </summary>
    static List<string> Systeme(Database db, Transaction tr, Tabelle t, IReadOnlyDictionary<string, string> standard)
    {
        var aus = new List<string>();
        var vorlage = standard.GetValueOrDefault("Systemlayer") ?? throw new InvalidDataException("Standard.csv: „Systemlayer“ fehlt");
        var stile = new AecDb.DictionaryLayerKeyStyle(db);
        AecDb.LayerKeyStyle stil;
        if (stile.Has(LayerSchluesselstil, tr)) stil = (AecDb.LayerKeyStyle)tr.GetObject(stile.GetAt(LayerSchluesselstil), OpenMode.ForWrite);
        else
        {
            stil = new AecDb.LayerKeyStyle();
            stil.SetToStandard(db);
            stil.SubSetDatabaseDefaults(db);
            stile.AddNewRecord(LayerSchluesselstil, stil);
            tr.AddNewlyCreatedDBObject(stil, true);
        }
        while (stil.KeyDefinitions.Count > 0) ((dynamic)stil.KeyDefinitions).RemoveAt(0);
        var systeme = new Aecb.DictionarySchematicSystemDefinition(db);
        var kuerzel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (zeile, w) in t.Zeilen)
        {
            var k = w["Kuerzel"];
            if (!kuerzel.Add(k)) throw new InvalidDataException($"{t.Datei}, Zeile {zeile}: Kürzel {k} doppelt");
            var linientyp = w["Linientyp"];
            LinientypId(db, tr, linientyp);
            var layer = Stammwerte.Fuelle(vorlage, new Dictionary<string, string> { ["Kuerzel"] = k });
            stil.KeyDefinitions.Add(new AecDb.LayerKeyDefinition
            {
                Key = k, LayerName = layer, Color = Farbwert(Farbe.Lies(w["Farbe"])), Linetype = linientyp,
                Lineweight = (LineWeight)Stammwerte.Linienstaerke(w["Linienstaerke"]),
            });
            Systemlayer(db, tr, layer, w, "LA-S-" + k);
            Aecb.SchematicSystemDefinition s;
            if (systeme.Has(w["Benennung"], tr)) s = (Aecb.SchematicSystemDefinition)tr.GetObject(systeme.GetAt(w["Benennung"]), OpenMode.ForWrite);
            else
            {
                s = new Aecb.SchematicSystemDefinition();
                s.SetToStandard(db);
                s.SubSetDatabaseDefaults(db);
                systeme.AddNewRecord(w["Benennung"], s);
                tr.AddNewlyCreatedDBObject(s, true);
            }
            s.LabelName = k;
            s.FamilyName = w["Gruppe"];
            s.LayerKey = k;
            Id(s, "SYS-" + k);
        }
        Id(stil, "LKS-" + LayerSchluesselstil);
        aus.Add($"AVTERZEUGEN Systeme | {t.Zeilen.Count} | Layer-Schlüsselstil {LayerSchluesselstil} mit {stil.KeyDefinitions.Count} Schlüsseln");
        return aus;
    }

    /// <summary>L7 (Nutzer 24.10.2026): je System ein Layer EHS_S_&lt;Kürzel&gt; mit den Werten aus dem Thema Medien – in der Vorlage angelegt, nicht erst beim Zeichnen.</summary>
    static void Systemlayer(Database db, Transaction tr, string name, IReadOnlyDictionary<string, string> w, string id)
    {
        var tab = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        var l = tab.Has(name) ? (LayerTableRecord)tr.GetObject(tab[name], OpenMode.ForWrite) : null;
        if (l == null)
        {
            l = new LayerTableRecord { Name = name };
            tab.Add(l); tr.AddNewlyCreatedDBObject(l, true);
        }
        l.Color = Farbwert(Farbe.Lies(w["Farbe"]));
        l.LinetypeObjectId = LinientypId(db, tr, w["Linientyp"]);
        l.LineWeight = (LineWeight)Stammwerte.Linienstaerke(w["Linienstaerke"]);
        l.Description = w["Benennung"];
        Id(l, id);
    }

    /// <summary>Systeme.csv aus dem Schema-Ordner (Eigentümer AMK): Kuerzel, Benennung, Gruppe, Farbe, Linientyp, Linienstaerke.</summary>
    internal static Tabelle Systemtabelle(string datei) => Tabelle.Lies(datei, "Kuerzel", "Benennung", "Gruppe", "Farbe", "Linientyp", "Linienstaerke");

    static void Stempel(Database db, Transaction tr, Bauauftrag a)
    {
        var werte = new[] { ("Fassung", a.Fassung), ("Vorgaenger", a.Vorgaenger), ("Stammdaten", a.StammdatenStand), ("Basis", a.Basis), ("AVT", a.AvtVersion) }
            .SelectMany(p => new[] { new TypedValue((int)DxfCode.Text, p.Item1), new TypedValue((int)DxfCode.Text, p.Item2) }).ToArray();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        if (nod.Contains(Standard.Stempel)) ((Xrecord)tr.GetObject(nod.GetAt(Standard.Stempel), OpenMode.ForWrite)).Data = new ResultBuffer(werte);
        else
        {
            var x = new Xrecord { Data = new ResultBuffer(werte) };
            nod.SetAt(Standard.Stempel, x); tr.AddNewlyCreatedDBObject(x, true);
        }
    }

    static void DwgProps(Database db, IReadOnlyDictionary<string, string> standard, Bauauftrag a, string gesamt)
    {
        var b = new DatabaseSummaryInfoBuilder(db.SummaryInfo)
        {
            Title = standard.GetValueOrDefault("Titel") ?? "",
            Subject = standard.GetValueOrDefault("Thema") ?? "",
            Author = standard.GetValueOrDefault("Autor") ?? "",
            Keywords = $"{Standard.Kuerzel}; {a.Fassung}; Kern {a.Fassung}; MEP 2026",
            Comments = $"Entwurf {a.Fassung} – erzeugt mit AVT {a.AvtVersion} aus den Stammdaten {a.StammdatenStand}. Nicht freigegeben.",
            RevisionNumber = a.Fassung,
            HyperlinkBase = "",
        };
        var felder = new Dictionary<string, string>
        {
            ["Fassung"] = a.Fassung, ["Vorgaenger"] = a.Vorgaenger, ["Freigabe"] = "Entwurf – nicht freigegeben",
            ["Profile"] = $"Kern {a.Fassung}", ["Stammdaten"] = a.StammdatenStand, ["Symbolfassung"] = "keine (folgt mit ASG-Register)",
            ["Basis"] = a.Basis, ["Inhaltspruefsumme"] = gesamt, ["AVT"] = a.AvtVersion,
        };
        foreach (var (k, v) in felder) b.CustomPropertyTable[Standard.Feld + k] = v;
        db.SummaryInfo = b.ToDatabaseSummaryInfo();
    }
}
