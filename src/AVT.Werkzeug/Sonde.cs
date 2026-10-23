using Avt.Kern;
using System.Collections.Specialized;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecb = Autodesk.Aec.Building.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.Sonde))]
namespace Avt.Werkzeug;

/// <summary>
/// API-Sonde (AVT 0.2.0, Auftrag AMK 23.10.2026): Was lässt sich in MEP 2026 headless per .NET anlegen? Je Objekttyp ein
/// Versuch in eigener Transaktion (Fehler verwirft nur diesen Versuch). AVTSONDE legt an und speichert unter neuem Namen,
/// AVTSONDELESEN liest am gespeicherten Ergebnis zurück, ob alles da ist. Alle Namen beginnen mit „AVT_Sonde“.
/// Nur an Kopien unter C:\Temp.
/// </summary>
public sealed class Sonde
{
    const string P = "AVT_Sonde";

    [CommandMethod("AVTSONDE", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Anlegen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei
            || Befehle.Pfad(doc.Editor, "Ergebniszeichnung") is not { } ziel) return;
        // Ergebnis muss eine neue .dwg sein – nie die offene Kopie, nie die Ausgabedatei (Review 23.10.2026).
        if (string.Equals(ziel, Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase)
            || string.Equals(ziel, datei, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(ziel), ".dwg", StringComparison.OrdinalIgnoreCase))
        {
            doc.Editor.WriteMessage("\nAVT abgelehnt: Ergebniszeichnung muss eine neue .dwg sein.");
            return;
        }
        // Schritte, die ausgelassen werden (Teil des Namens, durch | getrennt, - = keiner) – zum Eingrenzen von AUDIT-Befunden.
        var auslassen = doc.Editor.GetString(new Autodesk.AutoCAD.EditorInput.PromptStringOptions("\nAuslassen (| getrennt, - = keiner): ") { AllowSpaces = true })
            .StringResult.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(v => v != "-").ToList();
        var db = doc.Database;
        var aus = new List<string>();
        void Versuch(string typ, Func<Transaction, string> tun)
        {
            if (auslassen.Any(a => typ.Contains(a, StringComparison.OrdinalIgnoreCase))) { aus.Add($"AVTSONDE {typ} | ausgelassen |"); return; }
            using var tr = db.TransactionManager.StartTransaction();
            try
            {
                var d = tun(tr);
                // „!…“ = nicht möglich: nichts behalten.
                if (d.StartsWith('!')) { tr.Abort(); aus.Add($"AVTSONDE {typ} | nicht möglich | {d[1..]}"); return; }
                tr.Commit();
                aus.Add($"AVTSONDE {typ} | angelegt | {d}");
            }
            catch (System.Exception ex) { tr.Abort(); aus.Add($"AVTSONDE {typ} | FEHLER | {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        }
        Versuch("Layer", tr => Layer(db, tr));
        Versuch("Linientyp aus Strichmuster", tr => Linientyp(db, tr));
        Versuch("Linientyp aus acadiso.lin", _ => LinDatei(db));
        Versuch("Textstil", tr => Textstil(db, tr));
        Versuch("Bemaßungsstil", tr => Bemassungsstil(db, tr));
        Versuch("AutoCAD-Tabellenstil", tr => Tabellenstil(db, tr));
        Versuch("Satzdefinition (manuell)", tr => Satz(db, tr));
        Versuch("Satzfeld automatisch", tr => FeldAutomatisch(db, tr));
        Versuch("Satzfeld Formel", tr => FeldFormel(db, tr));
        Versuch("Layer-Schlüsselstil neu", tr => LayerSchluesselNeu(db, tr));
        Versuch("Layer-Schlüssel in vorhandenem Stil", tr => LayerSchluesselErgaenzen(db, tr));
        Versuch("Systemdefinition (Schema)", tr => System(db, tr));
        Versuch("Mehransichtsblock", tr => Mehransicht(db, tr));
        Versuch("Bauteiltabellenstil", tr => Bauteiltabelle(db, tr));
        Versuch("Anzeigethema neu (ohne Vorlage)", tr => ThemaNeu(db, tr));
        Versuch("Anzeigethema Farbe", tr => ThemaFarbe(db, tr));
        Versuch("Anzeigethema als Kopie", tr => ThemaKopie(db, tr));
        Versuch("Anzeigesatz", tr => Anzeigesatz(db, tr));
        Versuch("Layout", _ => Layout(db));
        Versuch("DWGPROPS schreiben", _ => DwgPropsSchreiben(db));
        Versuch("Fassungsstempel + RegApp", tr => Stempel(db, tr));
        try
        {
            db.SaveAs(ziel, DwgVersion.Current);
            aus.Add($"AVTSONDE gespeichert | {ziel}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER Speichern: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Sonde");
    }

    static string Layer(Database db, Transaction tr)
    {
        var t = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        var l = new LayerTableRecord { Name = P + "_Layer", Color = Color.FromColorIndex(ColorMethod.ByAci, 3), LineWeight = LineWeight.LineWeight025,
            Description = "Sonde" };
        t.Add(l); tr.AddNewlyCreatedDBObject(l, true);
        return "Farbe 3, 0,25 mm, Beschreibung";
    }

    static string Linientyp(Database db, Transaction tr)
    {
        var t = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForWrite);
        var l = new LinetypeTableRecord { Name = P + "_Strich", AsciiDescription = "__ __ __", PatternLength = 7.5, NumDashes = 2 };
        l.SetDashLengthAt(0, 5); l.SetDashLengthAt(1, -2.5);
        t.Add(l); tr.AddNewlyCreatedDBObject(l, true);
        return "2 Striche, Länge 7,5";
    }

    static string Textstil(Database db, Transaction tr)
    {
        var t = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
        var s = new TextStyleTableRecord { Name = P + "_Text", Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Arial", false, false, 0, 0), TextSize = 0 };
        t.Add(s); tr.AddNewlyCreatedDBObject(s, true);
        return "Arial (TrueType)";
    }

    static string Bemassungsstil(Database db, Transaction tr)
    {
        var t = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForWrite);
        var s = new DimStyleTableRecord { Name = P + "_Bem", Dimtxt = 2.5, Dimasz = 2.5 };
        t.Add(s); tr.AddNewlyCreatedDBObject(s, true);
        return "Text 2,5, Pfeil 2,5";
    }

    static string Tabellenstil(Database db, Transaction tr)
    {
        // Neu angelegt (new TableStyle, auch über PostTableStyleToDatabase) ergab 6 AUDIT-Fehler (Eingrenzung 23.10.2026).
        // Darum als Kopie des Tabellenstils „Standard“ der Zeichnung, dann ändern.
        var d = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForWrite);
        if (!d.Contains("Standard")) return "!kein Tabellenstil „Standard“ als Vorlage";
        var vorlage = (TableStyle)tr.GetObject(d.GetAt("Standard"), OpenMode.ForRead);
        var s = (TableStyle)vorlage.Clone();
        d.SetAt(P + "_Tabelle", s); tr.AddNewlyCreatedDBObject(s, true);
        s.Description = "Sonde";
        s.SetTextHeight(2.5, (int)RowType.DataRow);
        return "Kopie von „Standard“, Texthöhe Daten 2,5";
    }

    static Aecp.PropertySetDefinition NeueSatzdefinition(Database db, Transaction tr, string name, params string[] klassen)
    {
        var dict = new Aecp.DictionaryPropertySetDefinitions(db);
        var def = new Aecp.PropertySetDefinition();
        def.SetToStandard(db);
        def.SubSetDatabaseDefaults(db);
        def.AlternateName = name;
        var filter = new StringCollection();
        filter.AddRange(klassen);
        def.SetAppliesToFilter(filter, false);
        dict.AddNewRecord(name, def);
        tr.AddNewlyCreatedDBObject(def, true);
        return def;
    }

    static Aecp.PropertyDefinition Feld(Database db, string name, string datentyp, object standard)
    {
        var f = new Aecp.PropertyDefinition();
        f.SetToStandard(db);
        f.SubSetDatabaseDefaults(db);
        f.Name = name;
        f.DataType = Enum.Parse<Autodesk.Aec.PropertyData.DataType>(datentyp);
        f.DefaultData = standard;
        return f;
    }

    static string Satz(Database db, Transaction tr)
    {
        var def = NeueSatzdefinition(db, tr, P + "_Satz", "AecbDbSchematicSymbol", "AecbDbSchematic");
        def.Definitions.Add(Feld(db, "Text", "Text", ""));
        def.Definitions.Add(Feld(db, "Zahl", "Real", 0.0));
        def.Definitions.Add(Feld(db, "Ja", "TrueFalse", false));
        return $"3 Felder, gilt für {string.Join(",", def.AppliesToFilter.Cast<string>())}";
    }

    static Aecp.PropertySetDefinition Hole(Database db, Transaction tr, string name) =>
        (Aecp.PropertySetDefinition)tr.GetObject(new Aecp.DictionaryPropertySetDefinitions(db).GetAt(name), OpenMode.ForWrite);

    static string FeldAutomatisch(Database db, Transaction tr)
    {
        var def = Hole(db, tr, P + "_Satz");
        var f = new Aecp.PropertyDefinition();
        f.SetToStandard(db);
        f.SubSetDatabaseDefaults(db);
        f.Name = "Auto";
        f.Automatic = true;
        // Gültige Quellen je Klasse liefert die API; ohne gültige Quelle wird kein Feld angelegt (ein automatisches Feld
        // ohne Quelle wäre ungültig).
        var versuche = new List<string>();
        foreach (var klasse in new[] { "AecbDbSchematicSymbol", "AecbDbSchematic" })
        {
            var quellen = Aecp.PropertyDataServices.FindAutomaticSourceNames(klasse, db) ?? [];
            versuche.Add($"{klasse}: {quellen.Length} Quellen ({string.Join(", ", quellen.Take(6))} …)");
            var quelle = quellen.FirstOrDefault(q => q == "Handle") ?? quellen.FirstOrDefault();
            if (quelle is null) continue;
            f.SetAutomaticData(klasse, quelle);
            versuche.Add($"gesetzt {klasse}/{quelle}");
        }
        if (f.GetAutomaticData().Length == 0) return "!keine gültige Quelle – Feld nicht angelegt; " + string.Join("; ", versuche);
        def.Definitions.Add(f);
        return string.Join("; ", versuche);
    }

    static string FeldFormel(Database db, Transaction tr)
    {
        var def = Hole(db, tr, P + "_Satz");
        var f = new Aecp.PropertyDefinitionFormula();
        f.SetToStandard(db);
        f.SubSetDatabaseDefaults(db);
        f.Name = "Formel";
        def.Definitions.Add(f);
        var ok = f.SetFormulaString("RESULT = \"[Text]\" & \"-\" & \"[Zahl]\"");
        return $"SetFormulaString {(ok ? "angenommen" : "abgelehnt")}: {f.GetFormulaString()}";
    }

    static string LayerSchluesselNeu(Database db, Transaction tr)
    {
        var dict = new AecDb.DictionaryLayerKeyStyle(db);
        var s = new AecDb.LayerKeyStyle();
        s.SetToStandard(db);
        s.SubSetDatabaseDefaults(db);
        var k = new AecDb.LayerKeyDefinition { Key = "AVTSONDE", LayerName = P + "_Layer", Color = Color.FromColorIndex(ColorMethod.ByAci, 4), Linetype = "Continuous" };
        s.KeyDefinitions.Add(k);
        dict.AddNewRecord(P + "_Schluesselstil", s);
        tr.AddNewlyCreatedDBObject(s, true);
        return $"{s.KeyDefinitions.Count} Schlüssel";
    }

    static string LayerSchluesselErgaenzen(Database db, Transaction tr)
    {
        var dict = new AecDb.DictionaryLayerKeyStyle(db);
        var ids = dict.Records.Cast<ObjectId>().Where(i => !i.IsErased).ToList();
        if (ids.Count == 0) return "!kein vorhandener Stil";
        var s = ids.Select(i => (AecDb.LayerKeyStyle)tr.GetObject(i, OpenMode.ForWrite)).FirstOrDefault(x => !x.Name.StartsWith(P));
        if (s is null) return "!kein vorhandener (fremder) Layer-Schlüsselstil in der Zeichnung";
        var vorher = s.KeyDefinitions.Count;
        s.KeyDefinitions.Add(new AecDb.LayerKeyDefinition { Key = "AVTSONDE2", LayerName = P + "_Layer2", Color = Color.FromColorIndex(ColorMethod.ByAci, 5) });
        return $"„{s.Name}“: {vorher} → {s.KeyDefinitions.Count} Schlüssel";
    }

    static string LinDatei(Database db)
    {
        var datei = HostApplicationServices.Current.FindFile("acadiso.lin", db, FindFileHint.Default);
        var name = File.ReadLines(datei, global::System.Text.Encoding.Latin1).First(z => z.StartsWith('*'))[1..].Split(',')[0].Trim();
        db.LoadLineTypeFile(name, datei);
        return $"„{name}“ aus {datei}";
    }

    /// <summary>Automatische Felder der Zeichnung mit Quelle (Klasse, Eigenschaft), aus GetAutomaticData per Reflexion.</summary>
    internal static List<(string Satz, string Feld, string Klasse, string Quelle)?> AutomatischeFelder(Database db, Transaction tr)
    {
        var aus = new List<(string, string, string, string)?>();
        foreach (ObjectId id in new Aecp.DictionaryPropertySetDefinitions(db).Records)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Aecp.PropertySetDefinition d) continue;
            foreach (Aecp.PropertyDefinition f in d.Definitions)
            {
                if (!f.Automatic || f is Aecp.PropertyDefinitionFormula) continue;
                foreach (var a in f.GetAutomaticData())
                {
                    var props = a.GetType().GetProperties().ToDictionary(p => p.Name, p => Convert.ToString(p.GetValue(a)) ?? "");
                    var klasse = props.FirstOrDefault(p => p.Key.Contains("Class") || p.Key.Contains("Object") || p.Key.Contains("Type")).Value ?? "";
                    var quelle = props.FirstOrDefault(p => p.Key.Contains("Property") || p.Key.Contains("Source") || p.Key.Contains("Name")).Value ?? "";
                    aus.Add((d.Name, f.Name, klasse, quelle));
                }
            }
        }
        return aus;
    }

    static string System(Database db, Transaction tr)
    {
        var dict = new Aecb.DictionarySchematicSystemDefinition(db);
        var s = new Aecb.SchematicSystemDefinition();
        s.SetToStandard(db);
        s.SubSetDatabaseDefaults(db);
        s.LabelName = "SON";
        s.FamilyName = "Sonde";
        s.LayerKey = "AVTSONDE";
        dict.AddNewRecord(P + "_System", s);
        tr.AddNewlyCreatedDBObject(s, true);
        return $"Kürzel {s.LabelName}, Gruppe {s.FamilyName}, Layer-Schlüssel {s.LayerKey}";
    }

    static string Mehransicht(Database db, Transaction tr)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var block = new BlockTableRecord { Name = P + "_Block" };
        bt.Add(block); tr.AddNewlyCreatedDBObject(block, true);
        var kreis = new Circle { Radius = 2.5 };
        kreis.SetDatabaseDefaults(db);
        block.AppendEntity(kreis); tr.AddNewlyCreatedDBObject(kreis, true);
        var dict = new AecDb.DictionaryMultiViewBlockDefinition(db);
        var m = new AecDb.MultiViewBlockDefinition();
        m.SetToStandard(db);
        m.SubSetDatabaseDefaults(db);
        dict.AddNewRecord(P + "_MvB", m);
        tr.AddNewlyCreatedDBObject(m, true);
        // Darstellung „Allgemein“ o. ä. der Mehransichtsblock-Referenz suchen und den Block als Ansicht eintragen.
        string ansicht;
        try
        {
            var mgr = new AecDb.DisplayRepresentationManager(db);
            var klasse = RXObject.GetClass(typeof(AecDb.MultiViewBlockReference));
            var reps = mgr.GetAllDisplayRepresentationsWorkForSpecifiedClass(klasse).Cast<ObjectId>().ToList();
            var asm = typeof(AecDb.MultiViewBlockDefinition).Assembly;
            var darst = new AecDb.MultiViewBlockDisplayRepresentationDefinition { DisplayRepresentationId = reps[0] };
            dynamic sicht = Activator.CreateInstance(asm.GetType("Autodesk.Aec.DatabaseServices.MultiViewBlockViewDefinition")!)!;
            sicht.BlockId = block.ObjectId;
            darst.ViewDefinitions.Add(sicht);
            m.DisplayRepresentationDefinitions.Add(darst);
            ansicht = $"{reps.Count} Darstellungen verfügbar, „{((DBObject)tr.GetObject(reps[0], OpenMode.ForRead)).GetType().Name}“ mit Ansichtsblock belegt, "
                + $"jetzt {m.DisplayRepresentationDefinitions.Count} Darstellung(en)";
        }
        catch (System.Exception ex) { throw new InvalidOperationException($"Ansichtsblock nicht gesetzt ({ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message})", ex); }
        return ansicht;
    }

    static string Bauteiltabelle(Database db, Transaction tr)
    {
        var satz = Hole(db, tr, P + "_Satz");
        var dict = new Aecp.DictionaryScheduleTableStyle(db);
        var s = new Aecp.ScheduleTableStyle();
        s.SetToStandard(db);
        s.SubSetDatabaseDefaults(db);
        s.Title = "Sonde";
        var filter = new StringCollection { "AecbDbSchematicSymbol" };
        s.AppliesToFilter = filter;
        dict.AddNewRecord(P + "_Bauteiltabelle", s);
        tr.AddNewlyCreatedDBObject(s, true);
        var spalten = 0;
        foreach (var name in new[] { "Text", "Zahl" })
        {
            var feld = satz.Definitions.Cast<Aecp.PropertyDefinition>().First(f => f.Name == name);
            var c = new Aecp.ScheduleTableStyleColumn { Heading = name, PropertySetDefinitionId = satz.ObjectId, PropertyId = feld.Id };
            s.Columns.Add(c);
            spalten++;
        }
        string format;
        // Welche Formatangaben die API anbietet (Texthöhe, Stil, Ausrichtung …), wird nur gemeldet.
        try { format = "Titelformat: " + string.Join(", ", s.TitleFormat.GetType().GetProperties().Where(p => p.CanWrite).Select(p => p.Name)); }
        catch (System.Exception ex) { format = $"Format nicht lesbar ({ex.GetBaseException().Message})"; }
        return $"{spalten} Spalten, {format}";
    }

    static string ThemaNeu(Database db, Transaction tr)
    {
        var satz = Hole(db, tr, P + "_Satz");
        var dict = new AecDb.DictionaryDisplayThemeStyle(db);
        var t = new AecDb.DisplayThemeStyle();
        t.SetToStandard(db);
        t.SubSetDatabaseDefaults(db);
        t.Title = "Sonde neu";
        dict.AddNewRecord(P + "_Thema_neu", t);
        tr.AddNewlyCreatedDBObject(t, true);
        var k = new AecDb.DisplayThemeComponentBase { Name = "Ja" };
        var r = new Aecp.DisplayThemeScheduleDataRule();
        r.SetPropertySetDefinitionId(satz.ObjectId, false);
        var feld = satz.Definitions.Cast<Aecp.PropertyDefinition>().First(f => f.Name == "Text");
        r.SetPropertyDefinitionId(feld.Id, false);
        r.ComparisonOperator = AecDb.DisplayThemeRuleComparisonOperator.RuleComparisonOperatorEqualTo;
        r.Value = "x";
        k.Rules = r;
        // Darstellung der Komponente: Farbe Rot (RGB), Linienstärke 0,50 mm.
        t.DisplayThemeComponents.Add(k);
        return $"{t.DisplayThemeComponents.Count} Komponente, Regel {t.DisplayThemeComponents[0].Rules?.DisplayString}";
    }

    /// <summary>Farbe der Themen-Komponente in eigener Transaktion setzen (DisplayProperties ist beim Anlegen noch leer).</summary>
    static string ThemaFarbe(Database db, Transaction tr)
    {
        var dict = new AecDb.DictionaryDisplayThemeStyle(db);
        var t = (AecDb.DisplayThemeStyle)tr.GetObject(dict.GetAt(P + "_Thema_neu"), OpenMode.ForWrite);
        var k = t.DisplayThemeComponents[0];
        var dp = k.DisplayProperties;
        if (dp is null) return "!DisplayProperties auch nach dem Speichern der Komponente leer – Farbe nicht setzbar";
        dp.Color = Color.FromRgb(200, 30, 30);
        dp.LineWeight = LineWeight.LineWeight050;
        return $"{t.DisplayThemeComponents.Count} Komponente, Regel {t.DisplayThemeComponents[0].Rules?.DisplayString}";
    }

    static string ThemaKopie(Database db, Transaction tr)
    {
        var dict = new AecDb.DictionaryDisplayThemeStyle(db);
        // Vorzugsweise ein Thema mit Komponenten (deren Darstellungseigenschaften sind gefüllt) – dann Farbe der Kopie ändern.
        var themen = dict.Records.Cast<ObjectId>().Where(i => !i.IsErased)
            .Select(i => (AecDb.DisplayThemeStyle)tr.GetObject(i, OpenMode.ForRead)).Where(x => !x.Name.StartsWith(P)).ToList();
        var vorlage = themen.FirstOrDefault(x => x.DisplayThemeComponents.Count > 0) ?? themen.FirstOrDefault();
        if (vorlage is null) return "!kein Thema als Vorlage in der Zeichnung";
        var kopie = (AecDb.DisplayThemeStyle)vorlage.Clone();
        dict.AddNewRecord(P + "_Thema_Kopie", kopie);
        tr.AddNewlyCreatedDBObject(kopie, true);
        var farbe = "keine Komponente";
        if (kopie.DisplayThemeComponents.Count > 0)
        {
            var dp = kopie.DisplayThemeComponents[0].DisplayProperties;
            if (dp is null) farbe = "Komponente ohne Darstellungseigenschaften";
            else { dp.Color = Color.FromRgb(200, 30, 30); farbe = "Farbe der 1. Komponente auf 200,30,30 gesetzt"; }
        }
        return $"Kopie von „{vorlage.Name}“, {kopie.DisplayThemeComponents.Count} Komponenten, {farbe}";
    }

    static string Anzeigesatz(Database db, Transaction tr)
    {
        var dict = new AecDb.DictionaryDisplaySet(db, false);
        var s = new AecDb.DisplaySet();
        s.SetToStandard(db);
        s.SubSetDatabaseDefaults(db);
        dict.AddNewRecord(P + "_Anzeigesatz", s);
        tr.AddNewlyCreatedDBObject(s, true);
        return $"leer angelegt; Konfigurationen in der Zeichnung: {new AecDb.DictionaryDisplayConfiguration(db).Records.Count}";
    }

    static string Layout(Database db)
    {
        var alt = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            var id = LayoutManager.Current.CreateLayout(P + "_Layout");
            return $"Layout angelegt ({!id.IsNull})";
        }
        finally { HostApplicationServices.WorkingDatabase = alt; }
    }

    /// <summary>Standardfelder und alle Pflicht-Felder (benutzerdefiniert) mit Probewerten; Hyperlink-Basis bleibt leer.</summary>
    static string DwgPropsSchreiben(Database db)
    {
        var b = new DatabaseSummaryInfoBuilder(db.SummaryInfo)
        {
            Title = $"{Standard.Kuerzel} – {Standard.Langname} · Sonde",
            Subject = "TGA-Schema, AutoCAD MEP 2026 · Sonde",
            Author = "S. Eichler · Eichler-GmbH",
            Keywords = $"{Standard.Kuerzel}; 0.0.0-sonde; MEP 2026",
            Comments = "Probewerte der AVT-Sonde" + Environment.NewLine + "zweite Zeile",
            RevisionNumber = "0.0.0-sonde",
            HyperlinkBase = "",
        };
        foreach (var f in Standard.PflichtFelder) b.CustomPropertyTable[f] = $"Probe {f}";
        db.SummaryInfo = b.ToDatabaseSummaryInfo();
        return $"6 Standardfelder + {Standard.PflichtFelder.Length} benutzerdefinierte";
    }

    static string Stempel(Database db, Transaction tr)
    {
        var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
        if (!apps.Has(Standard.RegApp))
        {
            var r = new RegAppTableRecord { Name = Standard.RegApp };
            apps.Add(r); tr.AddNewlyCreatedDBObject(r, true);
        }
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        var x = new Xrecord
        {
            Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "Fassung"), new TypedValue((int)DxfCode.Text, "0.0.0-sonde"),
                new TypedValue((int)DxfCode.Text, "Erzeugt"), new TypedValue((int)DxfCode.Text, "AVT 0.2.0 Sonde")),
        };
        nod.SetAt(Standard.Stempel, x); tr.AddNewlyCreatedDBObject(x, true);
        var layer = (LayerTableRecord)tr.GetObject(((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))[P + "_Layer"], OpenMode.ForWrite);
        layer.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, Standard.RegApp),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ID:L-0001"));
        return $"{Standard.Stempel} (Xrecord) + XData {Standard.RegApp} an Layer";
    }

    [CommandMethod("AVTSONDELESEN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Lesen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var db = doc.Database;
        var aus = new List<string>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var b = Bestand.Lies(db, tr);
            foreach (var o in b.Alle.Values.Where(o => o.Name.StartsWith(P, StringComparison.Ordinal)).OrderBy(o => o.Name))
                aus.Add($"AVTSONDELESEN da | {o.Klasse} | {o.Name}");
            var satz = new Aecp.DictionaryPropertySetDefinitions(db);
            if (satz.Has(P + "_Satz", tr))
            {
                var def = (Aecp.PropertySetDefinition)tr.GetObject(satz.GetAt(P + "_Satz"), OpenMode.ForRead);
                foreach (Aecp.PropertyDefinition f in def.Definitions)
                    aus.Add($"AVTSONDELESEN Feld | {f.Name} | {f.GetType().Name} | {f.DataType} | automatisch {f.Automatic}"
                        + (f is Aecp.PropertyDefinitionFormula fo ? $" | Formel {fo.GetFormulaString()}" : ""));
            }
            foreach (var a in AutomatischeFelder(db, tr).Take(12))
                aus.Add($"AVTSONDELESEN AutoFeld | {a!.Value.Satz} | {a.Value.Feld} | {a.Value.Klasse} | {a.Value.Quelle}");
            var themen = new AecDb.DictionaryDisplayThemeStyle(db);
            foreach (var n in new[] { P + "_Thema_neu", P + "_Thema_Kopie" }.Where(n => themen.Has(n, tr)))
            {
                var t = (AecDb.DisplayThemeStyle)tr.GetObject(themen.GetAt(n), OpenMode.ForRead);
                var regeln = t.DisplayThemeComponents.Cast<AecDb.DisplayThemeComponentBase>().Select(k =>
                {
                    var r = k.Rules;
                    var satzName = r is Aecp.DisplayThemeScheduleDataRule sr && !sr.PropertySetDefinitionId.IsNull
                        ? ((AecDb.DictionaryRecord)tr.GetObject(sr.PropertySetDefinitionId, OpenMode.ForRead)).Name : "-";
                    var feldId = r is Aecp.DisplayThemeScheduleDataRule sr2 ? sr2.PropertyDefinitionId : -1;
                    var dp = k.DisplayProperties;
                    return $"{k.Name}: Regel {r?.GetType().Name} Satz {satzName} Feld-Id {feldId} ({r?.PropertyName}) {r?.ComparisonOperator} „{r?.ValueAsString}“, "
                        + $"Farbe {(dp is null ? "-" : dp.Color.ColorMethod + " " + dp.Color.ColorValue.R + "," + dp.Color.ColorValue.G + "," + dp.Color.ColorValue.B)}, "
                        + $"Linienstärke {dp?.LineWeight}";
                });
                aus.Add($"AVTSONDELESEN Thema | {n} | {string.Join("; ", regeln)}");
            }
            var mvb = new AecDb.DictionaryMultiViewBlockDefinition(db);
            if (mvb.Has(P + "_MvB", tr))
            {
                var m = (AecDb.MultiViewBlockDefinition)tr.GetObject(mvb.GetAt(P + "_MvB"), OpenMode.ForRead);
                aus.Add($"AVTSONDELESEN Mehransichtsblock | Darstellungen {m.DisplayRepresentationDefinitions.Count} | Blöcke {m.GetAllBlocksReferenced().Count}");
            }
            var tab = new Aecp.DictionaryScheduleTableStyle(db);
            if (tab.Has(P + "_Bauteiltabelle", tr))
            {
                var s = (Aecp.ScheduleTableStyle)tr.GetObject(tab.GetAt(P + "_Bauteiltabelle"), OpenMode.ForRead);
                aus.Add($"AVTSONDELESEN Bauteiltabelle | Spalten {string.Join(", ", s.Columns.Cast<Aecp.ScheduleTableStyleColumn>().Select(c => $"{c.Heading}={c.FullPropertyName}"))}");
            }
            aus.AddRange(BefehleKanon.FassungLesen(db, tr).Select(f => $"AVTSONDELESEN Fassung | {f}"));
            aus.AddRange(BefehleKanon.DwgProps(db).Select(f => $"AVTSONDELESEN DWGPROPS | {f}"));
            var fehlend = BefehleKanon.FehlendePflichtfelder(db);
            aus.Add($"AVTSONDELESEN DWGPROPS Pflicht | {(fehlend.Count == 0 ? "alle da" : "fehlt: " + string.Join(", ", fehlend))}");
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Sonde lesen");
    }
}
