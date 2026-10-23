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
        var db = doc.Database;
        var aus = new List<string>();
        void Versuch(string typ, Func<Transaction, string> tun)
        {
            using var tr = db.TransactionManager.StartTransaction();
            try { var d = tun(tr); tr.Commit(); aus.Add($"AVTSONDE {typ} | angelegt | {d}"); }
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
        var d = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForWrite);
        // Name und Formate erst nach dem Eintragen (vorher eOwnerNotSet, Sonde 23.10.2026); der Name ist der Schlüssel.
        var s = new TableStyle();
        d.SetAt(P + "_Tabelle", s); tr.AddNewlyCreatedDBObject(s, true);
        s.Description = "Sonde";
        // Formate erst nach dem Eintragen (vorher eOwnerNotSet, Sonde 23.10.2026).
        s.SetTextHeight(2.5, (int)RowType.DataRow);
        return "Texthöhe Daten 2,5";
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
        // Quelle erst setzen, wenn das Feld in der Definition steht (die kennt die Klassen, für die sie gilt).
        def.Definitions.Add(f);
        var feld = def.Definitions.Cast<Aecp.PropertyDefinition>().First(x => x.Name == "Auto");
        var versuche = new List<string>();
        foreach (var klasse in new[] { "AecbDbSchematicSymbol", "AecbDbSchematic" })
        {
            try { feld.SetAutomaticData(klasse, "Handle"); versuche.Add($"{klasse}/Handle ok"); }
            catch (System.Exception ex) { versuche.Add($"{klasse}/Handle {ex.GetBaseException().Message}"); }
        }
        return $"{string.Join("; ", versuche)}; Quellen jetzt {feld.GetAutomaticData().Length}";
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
        if (ids.Count == 0) return "kein vorhandener Stil";
        var s = ids.Select(i => (AecDb.LayerKeyStyle)tr.GetObject(i, OpenMode.ForWrite)).FirstOrDefault(x => !x.Name.StartsWith(P));
        if (s is null) return "kein vorhandener (fremder) Layer-Schlüsselstil in der Zeichnung";
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
        catch (System.Exception ex) { ansicht = $"Ansichtsblock nicht gesetzt ({ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message})"; }
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
        t.DisplayThemeComponents.Add(k);
        return $"{t.DisplayThemeComponents.Count} Komponente, Regel {t.DisplayThemeComponents[0].Rules?.DisplayString}";
    }

    static string ThemaKopie(Database db, Transaction tr)
    {
        var dict = new AecDb.DictionaryDisplayThemeStyle(db);
        var vorlage = dict.Records.Cast<ObjectId>().Where(i => !i.IsErased)
            .Select(i => (AecDb.DisplayThemeStyle)tr.GetObject(i, OpenMode.ForRead)).FirstOrDefault(x => !x.Name.StartsWith(P));
        if (vorlage is null) return "kein Thema als Vorlage in der Zeichnung";
        var kopie = (AecDb.DisplayThemeStyle)vorlage.Clone();
        dict.AddNewRecord(P + "_Thema_Kopie", kopie);
        tr.AddNewlyCreatedDBObject(kopie, true);
        return $"Kopie von „{vorlage.Name}“, {kopie.DisplayThemeComponents.Count} Komponenten";
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
                var regeln = t.DisplayThemeComponents.Cast<AecDb.DisplayThemeComponentBase>().Select(k => $"{k.Name}: {k.Rules?.DisplayString}");
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
            var vorhanden = BefehleKanon.DwgProps(db).Where(f => f.StartsWith('*')).Select(f => f[1..].Split(" = ")[0]).ToHashSet();
            var fehlend = Standard.PflichtFelder.Where(f => !vorhanden.Contains(f)).ToList();
            aus.Add($"AVTSONDELESEN DWGPROPS Pflicht | {(fehlend.Count == 0 ? "alle da" : "fehlt: " + string.Join(", ", fehlend))}");
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Sonde lesen");
    }
}
