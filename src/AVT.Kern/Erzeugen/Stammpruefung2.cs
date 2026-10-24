using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Avt.Kern.Erzeugen;

/// <summary>Prüfen gegen die Stammdaten, Teil 2 (AVT 0.4.0): Wertelisten, Sätze und Felder, Themen, Bausteine, Layouts. Nur lesen.</summary>
internal static class Stammpruefung2
{
    public static void Pruefen(Database db, Transaction tr, string stammdaten, Action<string> fehler, Action<string> ok)
    {
        Saetze(db, tr, stammdaten, fehler, ok);
        Themen(db, tr, stammdaten, fehler, ok);
        Layouts(db, tr, stammdaten, fehler, ok);
    }

    static void Saetze(Database db, Transaction tr, string s, Action<string> F, Action<string> ok)
    {
        var saetze = Feldkatalog.SaetzeLesen(Tabelle.Lies(Path.Combine(s, "Saetze.csv"), "ID", "Satz", "Art", "GiltFuer"));
        var felder = Feldkatalog.FelderLesen(Tabelle.Lies(Path.Combine(s, "Feldkatalog.csv"), "Satz", "Feld", "Typ"));
        var listen = Feldkatalog.ListenLesen(Tabelle.Lies(Path.Combine(s, "Wertelisten.csv"), "Liste", "Nr", "Wert"));
        var ld = new AecDb.DictionaryListDefinition(db);
        foreach (var (name, werte) in listen)
        {
            var voll = Erzeugen.Saetze.ListenVorsilbe + name;
            if (!ld.Has(voll, tr)) { F($"Liste {voll} fehlt"); continue; }
            var l = (AecDb.ListDefinition)tr.GetObject(ld.GetAt(voll), OpenMode.ForRead);
            var ist = l.GetListItems().Cast<ObjectId>().Select(i => ((AecDb.DictionaryRecord)tr.GetObject(i, OpenMode.ForRead)).Name).ToList();
            if (!ist.SequenceEqual(werte)) F($"Liste {voll}: {string.Join("/", ist)} statt {string.Join("/", werte)}");
        }
        var pd = new Aecp.DictionaryPropertySetDefinitions(db);
        var soll = saetze.Select(x => x.Satz).ToHashSet(StringComparer.Ordinal);
        foreach (var sz in saetze)
        {
            if (!pd.Has(sz.Satz, tr)) { F($"Satz {sz.Satz} fehlt"); continue; }
            var d = (Aecp.PropertySetDefinition)tr.GetObject(pd.GetAt(sz.Satz), OpenMode.ForRead);
            if (d.IsStyleBased != sz.Stilbasiert) F($"Satz {sz.Satz}: stilbasiert {d.IsStyleBased} statt {sz.Stilbasiert}");
            var gilt = d.AppliesToFilter.Cast<string>().OrderBy(x => x, StringComparer.Ordinal);
            if (!gilt.SequenceEqual(sz.GiltFuer.OrderBy(x => x, StringComparer.Ordinal))) F($"Satz {sz.Satz}: gilt für {string.Join(",", gilt)} statt {string.Join(",", sz.GiltFuer)}");
            var ist = d.Definitions.Cast<Aecp.PropertyDefinition>().ToDictionary(f => f.Name, StringComparer.Ordinal);
            var sollFelder = felder.Where(f => f.Satz == sz.Satz).ToList();
            foreach (var f in sollFelder)
            {
                if (!ist.TryGetValue(f.Feld, out var p)) { F($"Feld {sz.Satz}.{f.Feld} fehlt"); continue; }
                var typOk = f.Typ switch
                {
                    "Formel" => p is Aecp.PropertyDefinitionFormula,
                    "Auto" => p.Automatic && p is not Aecp.PropertyDefinitionFormula,
                    "Liste" => p.DataType == Autodesk.Aec.PropertyData.DataType.List,
                    "JaNein" => p.DataType == Autodesk.Aec.PropertyData.DataType.TrueFalse,
                    "Integer" => p.DataType == Autodesk.Aec.PropertyData.DataType.Integer,
                    "Real" => p.DataType == Autodesk.Aec.PropertyData.DataType.Real,
                    _ => p.DataType == Autodesk.Aec.PropertyData.DataType.Text && !p.Automatic,
                };
                if (!typOk) F($"Feld {sz.Satz}.{f.Feld}: Typ {p.DataType}/{(p.Automatic ? "auto" : "manuell")} statt {f.Typ}");
            }
            foreach (var n in ist.Keys.Where(n => !sollFelder.Any(f => f.Feld == n))) F($"Feld {sz.Satz}.{n} steht nicht im Feldkatalog");
        }
        foreach (ObjectId i in pd.Records)
        {
            if (i.IsErased) continue;
            var n = ((AecDb.DictionaryRecord)tr.GetObject(i, OpenMode.ForRead)).Name;
            if (!soll.Contains(n)) F($"Satz {n} steht nicht in Saetze.csv");
        }
        ok($"Sätze {saetze.Count}, Felder {felder.Count}, Listen {listen.Count} geprüft");
    }

    static void Themen(Database db, Transaction tr, string s, Action<string> F, Action<string> ok)
    {
        var zeilen = Erzeugen.Themen.Lesen(Tabelle.Lies(Path.Combine(s, "Themen.csv"), "Thema", "Nr", "Komponente"));
        var dict = new AecDb.DictionaryDisplayThemeStyle(db);
        var soll = zeilen.Select(z => z.Thema).ToHashSet(StringComparer.Ordinal);
        foreach (var g in zeilen.GroupBy(z => z.Thema))
        {
            if (!dict.Has(g.Key, tr)) { F($"Thema {g.Key} fehlt"); continue; }
            var t = (AecDb.DisplayThemeStyle)tr.GetObject(dict.GetAt(g.Key), OpenMode.ForRead);
            var komp = g.Where(z => z.Nr > 0).OrderBy(z => z.Nr).ToList();
            if (t.DisplayThemeComponents.Count != komp.Count) { F($"Thema {g.Key}: {t.DisplayThemeComponents.Count} Komponenten statt {komp.Count}"); continue; }
            for (var i = 0; i < komp.Count; i++)
            {
                var k = t.DisplayThemeComponents[i];
                var z = komp[i];
                var wo = $"Thema {g.Key}/{z.Komponente}";
                if (k.Name != z.Komponente) F($"{wo}: Name {k.Name}");
                var r = Erzeugen.Themen.Regel(k);
                if (r is null || $"{r.Value.Satz}.{r.Value.Feld}" != z.Feld || r.Value.Wert != z.Wert)
                    F($"{wo}: Regel {(r is null ? "fehlt" : $"{r.Value.Satz}.{r.Value.Feld} = {r.Value.Wert}")} statt {z.Feld} = {z.Wert}");
                var dp = k.DisplayProperties;
                if (dp is null) { F($"{wo}: Darstellung leer"); continue; }
                if (!Gleich(dp.Color, Farbe.Lies(z.Farbe))) F($"{wo}: Farbe {dp.Color} statt {z.Farbe}");
                if ((int)dp.LineWeight != Stammwerte.Linienstaerke(z.Linienstaerke)) F($"{wo}: Linienstärke {dp.LineWeight} statt {z.Linienstaerke}");
                var lt = dp.LinetypeId.IsNull ? "" : ((SymbolTableRecord)tr.GetObject(dp.LinetypeId, OpenMode.ForRead)).Name;
                if (!lt.Equals(z.Linientyp, StringComparison.OrdinalIgnoreCase)) F($"{wo}: Linientyp {lt} statt {z.Linientyp}");
                if (Math.Abs(dp.LinetypeScale - z.LtFaktor) > 1e-9) F($"{wo}: LT-Faktor {dp.LinetypeScale} statt {z.LtFaktor}");
            }
        }
        foreach (ObjectId i in dict.Records)
        {
            if (i.IsErased) continue;
            var t = (AecDb.DisplayThemeStyle)tr.GetObject(i, OpenMode.ForRead);
            if (!soll.Contains(t.Name) && t.DisplayThemeComponents.Count > 0) F($"Thema {t.Name} steht nicht in Themen.csv");
        }
        ok($"Themen {soll.Count} mit {zeilen.Count(z => z.Nr > 0)} Komponenten geprüft");
    }

    static void Layouts(Database db, Transaction tr, string s, Action<string> F, Action<string> ok)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (var (_, w) in Tabelle.Lies(Path.Combine(s, "Bausteine.csv"), "ID", "Art", "Name").Zeilen.Where(z => z.Werte["Art"] == "Block"))
            if (!bt.Has(w["Name"])) F($"Block {w["Name"]} (Baustein) fehlt");
        var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        var tabelle = Tabelle.Lies(Path.Combine(s, "Layouts.csv"), "ID", "Layout", "Rahmenblock", "Rahmenformat");
        foreach (var (_, w) in tabelle.Zeilen)
        {
            if (!dict.Contains(w["Layout"])) { F($"Layout {w["Layout"]} fehlt"); continue; }
            var l = (Layout)tr.GetObject(dict.GetAt(w["Layout"]), OpenMode.ForRead);
            if (l.PlotConfigurationName != w["Plotter"]) F($"Layout {l.LayoutName}: Plotter {l.PlotConfigurationName} statt {w["Plotter"]}");
            if (l.CanonicalMediaName != w["Papier"]) F($"Layout {l.LayoutName}: Papier {l.CanonicalMediaName} statt {w["Papier"]}");
            var raum = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
            var rahmen = raum.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<BlockReference>()
                .FirstOrDefault(b => ((BlockTableRecord)tr.GetObject(b.DynamicBlockTableRecord, OpenMode.ForRead)).Name == w["Rahmenblock"]);
            if (rahmen is null) { F($"Layout {l.LayoutName}: Rahmen {w["Rahmenblock"]} fehlt"); continue; }
            var format = rahmen.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                .Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(v => v == w["Rahmenformat"]);
            if (format is null) F($"Layout {l.LayoutName}: Rahmenformat ≠ {w["Rahmenformat"]}");
            var fenster = raum.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Viewport>().Count();
            var sollF = Fenster.Lies(w.GetValueOrDefault("Ansichtsfenster") ?? "").Count;
            if (fenster != sollF) F($"Layout {l.LayoutName}: {fenster} Ansichtsfenster statt {sollF}");
        }
        foreach (var e in dict)
        {
            var l = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
            if (!l.ModelType && !tabelle.Zeilen.Any(z => z.Werte["Layout"] == l.LayoutName)) F($"Layout {l.LayoutName} steht nicht in Layouts.csv");
        }
        ok($"Bausteine und {tabelle.Zeilen.Count} Layout(s) geprüft");
    }

    static bool Gleich(Color ist, Farbe soll) => soll.Rgb
        ? ist.ColorMethod == ColorMethod.ByColor && ist.Red == soll.R && ist.Green == soll.G && ist.Blue == soll.B
        : ist.ColorMethod == ColorMethod.ByAci && ist.ColorIndex == soll.Index;
}
