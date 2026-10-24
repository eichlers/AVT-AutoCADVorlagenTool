using Avt.Kern.Fremd;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecb = Autodesk.Aec.Building.DatabaseServices;

namespace Avt.Kern.Erzeugen;

/// <summary>
/// Prüfen einer Vorlage gegen die Stammdaten (AVT 0.4.0): je Tabellenzeile Soll/Ist, dazu Regeln des Standards
/// (alle Textstile Arial, keine Systeme außerhalb der Stammdaten, feste IDs, DWGPROPS-Pflichtfelder, Fassungsstempel).
/// Nur lesen. Zeilen „AVTSTAMM FEHLER|HINWEIS|OK“.
/// </summary>
internal static class Stammpruefung
{
    public static List<string> Pruefen(Database db, Transaction tr, string stammdaten, string systemeCsv)
    {
        var aus = new List<string>();
        int fehler = 0, hinweise = 0;
        void F(string t) { aus.Add($"AVTSTAMM FEHLER | {t}"); fehler++; }
        void H(string t) { aus.Add($"AVTSTAMM HINWEIS | {t}"); hinweise++; }

        // Textstile: Soll aus der Tabelle, Regel „alle Arial“.
        var ts = Tabelle.Lies(Path.Combine(stammdaten, "Textstile.csv"), "ID", "Name", "Schrift", "Hoehe_mm");
        var tsTab = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        foreach (var (_, w) in ts.Zeilen)
        {
            if (!tsTab.Has(w["Name"])) { F($"Textstil {w["Name"]} fehlt"); continue; }
            var s = (TextStyleTableRecord)tr.GetObject(tsTab[w["Name"]], OpenMode.ForRead);
            if (Math.Abs(s.TextSize - Stammwerte.Zahl(w["Hoehe_mm"])) > 1e-9) F($"Textstil {w["Name"]}: Höhe {s.TextSize} statt {w["Hoehe_mm"]}");
            if (s.Font.Bold != Stammwerte.JaNein(w.GetValueOrDefault("Fett"))) F($"Textstil {w["Name"]}: Fett {s.Font.Bold} statt {w.GetValueOrDefault("Fett")}");
            if ((s.Annotative == AnnotativeStates.True) != Stammwerte.JaNein(w.GetValueOrDefault("Annotativ"))) F($"Textstil {w["Name"]}: annotativ {s.Annotative} statt {w.GetValueOrDefault("Annotativ")}");
            if (IdVon(s) != w["ID"]) F($"Textstil {w["Name"]}: ID „{IdVon(s)}“ statt {w["ID"]}");
        }
        foreach (ObjectId id in tsTab)
        {
            var s = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (s.Name.Length == 0 || s.IsShapeFile) continue;
            if (!Stammwerte.IstArial(s.Font.TypeFace, s.FileName))
                F($"Textstil {s.Name}: Schrift „{(string.IsNullOrEmpty(s.Font.TypeFace) ? s.FileName : s.Font.TypeFace)}“ – nur Arial erlaubt");
        }

        // Linientypen
        var ltTab = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        foreach (var (_, w) in Tabelle.Lies(Path.Combine(stammdaten, "Linientypen.csv"), "ID", "Name", "Quelle").Zeilen)
        {
            if (!ltTab.Has(w["Name"])) { F($"Linientyp {w["Name"]} fehlt"); continue; }
            if (IdVon(tr.GetObject(ltTab[w["Name"]], OpenMode.ForRead)) != w["ID"]) F($"Linientyp {w["Name"]}: ID fehlt/falsch");
        }

        // Layer
        var laTab = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var (_, w) in Tabelle.Lies(Path.Combine(stammdaten, "Layer.csv"), "ID", "Name", "Farbe", "Linientyp", "Linienstaerke", "Plotten").Zeilen)
        {
            if (!laTab.Has(w["Name"])) { F($"Layer {w["Name"]} fehlt"); continue; }
            var l = (LayerTableRecord)tr.GetObject(laTab[w["Name"]], OpenMode.ForRead);
            if (!Gleich(l.Color, Farbe.Lies(w["Farbe"]))) F($"Layer {w["Name"]}: Farbe {l.Color} statt {w["Farbe"]}");
            if ((int)l.LineWeight != Stammwerte.Linienstaerke(w["Linienstaerke"])) F($"Layer {w["Name"]}: Linienstärke {l.LineWeight} statt {w["Linienstaerke"]}");
            if (IdVon(l) != w["ID"]) F($"Layer {w["Name"]}: ID fehlt/falsch");
        }

        // Systeme + Layer-Schlüssel
        var sysTab = Erzeuger.Systemtabelle(systemeCsv);
        var systeme = new Aecb.DictionarySchematicSystemDefinition(db);
        var stile = new AecDb.DictionaryLayerKeyStyle(db);
        var schluessel = new Dictionary<string, AecDb.LayerKeyDefinition>(StringComparer.OrdinalIgnoreCase);
        if (stile.Has(Erzeuger.LayerSchluesselstil, tr))
        {
            var stil = (AecDb.LayerKeyStyle)tr.GetObject(stile.GetAt(Erzeuger.LayerSchluesselstil), OpenMode.ForRead);
            foreach (AecDb.LayerKeyDefinition k in stil.KeyDefinitions) schluessel[k.Key] = k;
        }
        else F($"Layer-Schlüsselstil {Erzeuger.LayerSchluesselstil} fehlt");
        var soll = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, w) in sysTab.Zeilen)
        {
            var name = w["Benennung"];
            soll.Add(name);
            if (!systeme.Has(name, tr)) { F($"System {name} fehlt"); continue; }
            var s = (Aecb.SchematicSystemDefinition)tr.GetObject(systeme.GetAt(name), OpenMode.ForRead);
            var k = w["Kuerzel"];
            if (s.LabelName != k) F($"System {name}: Kürzel {s.LabelName} statt {k}");
            if (s.FamilyName != w["Gruppe"]) F($"System {name}: Gruppe {s.FamilyName} statt {w["Gruppe"]}");
            if (s.LayerKey != k) F($"System {name}: Layer-Schlüssel {s.LayerKey} statt {k}");
            if (IdVon(s) != "SYS-" + k) F($"System {name}: ID fehlt/falsch");
            if (!schluessel.TryGetValue(k, out var d)) { F($"Layer-Schlüssel {k} fehlt"); continue; }
            if (!Gleich(d.Color, Farbe.Lies(w["Farbe"]))) F($"Layer-Schlüssel {k}: Farbe {d.Color} statt {w["Farbe"]}");
            if (!d.Linetype.Equals(w["Linientyp"], StringComparison.OrdinalIgnoreCase)) F($"Layer-Schlüssel {k}: Linientyp {d.Linetype} statt {w["Linientyp"]}");
            if ((int)d.Lineweight != Stammwerte.Linienstaerke(w["Linienstaerke"])) F($"Layer-Schlüssel {k}: Linienstärke {d.Lineweight} statt {w["Linienstaerke"]}");
            // L7: Systemlayer angelegt, Werte wie der Schlüssel.
            if (!laTab.Has(d.LayerName)) { F($"Systemlayer {d.LayerName} fehlt"); continue; }
            var sl = (LayerTableRecord)tr.GetObject(laTab[d.LayerName], OpenMode.ForRead);
            if (!Gleich(sl.Color, Farbe.Lies(w["Farbe"]))) F($"Systemlayer {d.LayerName}: Farbe {sl.Color} statt {w["Farbe"]}");
            if ((int)sl.LineWeight != Stammwerte.Linienstaerke(w["Linienstaerke"])) F($"Systemlayer {d.LayerName}: Linienstärke {sl.LineWeight} statt {w["Linienstaerke"]}");
            if (IdVon(sl) != "LA-S-" + k) F($"Systemlayer {d.LayerName}: ID fehlt/falsch");
        }
        foreach (ObjectId id in systeme.Records)
        {
            if (id.IsErased) continue;
            var n = ((AecDb.DictionaryRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
            if (!soll.Contains(n)) F($"System {n} steht nicht in den Stammdaten");
        }

        // Basis-Rest: was nach Basis_entfernen.csv weg sollte und blieb (benutzt) – Hinweis.
        var muster = Tabelle.Lies(Path.Combine(stammdaten, "Basis_entfernen.csv"), "Muster", "Grund").Zeilen.Select(z => z.Werte["Muster"]).ToList();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        foreach (var e in nod)
        {
            if (tr.GetObject(e.Value, OpenMode.ForRead) is not DBDictionary unter) continue;
            foreach (var u in unter)
            {
                var pfad = $"NOD/{e.Key}/{u.Key}";
                // Eigene Einträge (mit fester ID) sind kein Rest der Basis.
                if (muster.Any(m => Stammwerte.Passt(m, pfad)) && IdVon(tr.GetObject(u.Value, OpenMode.ForRead)).Length == 0)
                    H($"{pfad} blieb (Autodesk-Fachinhalt, benutzt)");
            }
        }

        // Teil 2: Wertelisten, Sätze, Themen, Bausteine, Layouts.
        Stammpruefung2.Pruefen(db, tr, stammdaten, F, t => aus.Add($"AVTSTAMM OK | {t}"));

        // Fassung und DWGPROPS
        var fassung = Zeichnungsangaben.FassungLesen(db, tr).ToList();
        if (fassung.Count == 1 && fassung[0] is "fehlt" or "unlesbar") F($"Fassungsstempel {Standard.Stempel} {fassung[0]}");
        var pflicht = Zeichnungsangaben.FehlendePflichtfelder(db);
        if (pflicht.Count > 0) F($"DWGPROPS-Pflichtfelder fehlen: {string.Join(", ", pflicht)}");
        if (!((RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead)).Has(Standard.RegApp)) F($"RegApp {Standard.RegApp} fehlt");
        // Inhaltsprüfsumme der Datei = Feld in den DWGPROPS?
        var b = Bestand.Lies(db, tr);
        var gesamt = Kanon.Gesamt(Kanon.Lies(db, tr, b, []));
        var feld = Zeichnungsangaben.DwgProps(db).FirstOrDefault(f => f.StartsWith($"*{Standard.Feld}Inhaltspruefsumme = ", StringComparison.Ordinal))?.Split(" = ")[1] ?? "";
        if (feld != gesamt) F($"Inhaltsprüfsumme der Datei {gesamt[..16]} … ≠ DWGPROPS {(feld.Length >= 16 ? feld[..16] : feld)} …");
        else aus.Add($"AVTSTAMM OK | Inhaltsprüfsumme {gesamt[..16]} … = DWGPROPS");

        aus.Add($"AVTSTAMM Ergebnis | Fehler {fehler} | Hinweise {hinweise} | Textstile {ts.Zeilen.Count} | Systeme {sysTab.Zeilen.Count}");
        return aus;
    }

    static string IdVon(DBObject o)
    {
        using var rb = o.GetXDataForApplication(Standard.RegApp);
        return rb?.AsArray().Select(t => Convert.ToString(t.Value) ?? "").FirstOrDefault(v => v.StartsWith("ID:", StringComparison.Ordinal))?[3..] ?? "";
    }

    static bool Gleich(Color ist, Farbe soll) => soll.Rgb
        ? ist.ColorMethod == ColorMethod.ByColor && ist.Red == soll.R && ist.Green == soll.G && ist.Blue == soll.B
        : ist.ColorMethod == ColorMethod.ByAci && ist.ColorIndex == soll.Index;
}
