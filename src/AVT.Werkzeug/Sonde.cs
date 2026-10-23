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
        Versuch("Linientyp aus acadiso.lin", _ => { db.LoadLineTypeFile("BORDER", "acadiso.lin"); return "BORDER geladen"; });
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
        Versuch("Fassungsstempel + RegApp EHS", tr => Stempel(db, tr));
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
        var s = new TableStyle { Name = P + "_Tabelle", Description = "Sonde" };
        s.SetTextHeight(2.5, (int)RowType.DataRow);
        d.SetAt(s.Name, s); tr.AddNewlyCreatedDBObject(s, true);
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
        f.Name = "Handle";
        f.Automatic = true;
        f.SetAutomaticData("AecbDbSchematicSymbol", "Handle");
        f.SetAutomaticData("AecbDbSchematic", "Handle");
        def.Definitions.Add(f);
        var quellen = f.GetAutomaticData().Select(a => a.ToString()).ToList();
        return $"Quelle Handle, {quellen.Count} Zuordnungen";
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
        var s = ids.Select(i => (AecDb.LayerKeyStyle)tr.GetObject(i, OpenMode.ForWrite)).First(x => !x.Name.StartsWith(P));
        var vorher = s.KeyDefinitions.Count;
        s.KeyDefinitions.Add(new AecDb.LayerKeyDefinition { Key = "AVTSONDE2", LayerName = P + "_Layer2", Color = Color.FromColorIndex(ColorMethod.ByAci, 5) });
        return $"„{s.Name}“: {vorher} → {s.KeyDefinitions.Count} Schlüssel";
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
        // Ansichtsblock je Darstellung: Aufbau der Sammlung über dynamic, Ergebnis wird gemeldet.
        dynamic darst = m.DisplayRepresentationDefinitions;
        string ansicht;
        try
        {
            var n = (int)darst.Count;
            ansicht = $"{n} Darstellungen vorbelegt";
            if (n > 0)
            {
                dynamic erste = darst[0];
                dynamic ansichten = erste.ViewBlocks;
                dynamic vb = Activator.CreateInstance(typeof(AecDb.MultiViewBlockDefinition).Assembly.GetType("Autodesk.Aec.DatabaseServices.ViewBlock")!)!;
                vb.BlockId = block.ObjectId;
                ansichten.Add(vb);
                ansicht += ", Ansichtsblock in Darstellung 1 gesetzt";
            }
        }
        catch (System.Exception ex) { ansicht = $"Ansichtsblock nicht gesetzt ({ex.GetBaseException().Message})"; }
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

    static string Stempel(Database db, Transaction tr)
    {
        var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
        if (!apps.Has("EHS"))
        {
            var r = new RegAppTableRecord { Name = "EHS" };
            apps.Add(r); tr.AddNewlyCreatedDBObject(r, true);
        }
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        var x = new Xrecord
        {
            Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "Fassung"), new TypedValue((int)DxfCode.Text, "0.0.0-sonde"),
                new TypedValue((int)DxfCode.Text, "Erzeugt"), new TypedValue((int)DxfCode.Text, "AVT 0.2.0 Sonde")),
        };
        nod.SetAt(BefehleKanon.Stempel, x); tr.AddNewlyCreatedDBObject(x, true);
        var layer = (LayerTableRecord)tr.GetObject(((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))[P + "_Layer"], OpenMode.ForWrite);
        layer.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "EHS"),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ID:L-0001"));
        return "EHS_Zeichnung (Xrecord) + XData EHS an Layer";
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
            var themen = new AecDb.DictionaryDisplayThemeStyle(db);
            foreach (var n in new[] { P + "_Thema_neu", P + "_Thema_Kopie" }.Where(n => themen.Has(n, tr)))
            {
                var t = (AecDb.DisplayThemeStyle)tr.GetObject(themen.GetAt(n), OpenMode.ForRead);
                var regeln = t.DisplayThemeComponents.Cast<AecDb.DisplayThemeComponentBase>().Select(k => $"{k.Name}: {k.Rules?.DisplayString}");
                aus.Add($"AVTSONDELESEN Thema | {n} | {string.Join("; ", regeln)}");
            }
            var tab = new Aecp.DictionaryScheduleTableStyle(db);
            if (tab.Has(P + "_Bauteiltabelle", tr))
            {
                var s = (Aecp.ScheduleTableStyle)tr.GetObject(tab.GetAt(P + "_Bauteiltabelle"), OpenMode.ForRead);
                aus.Add($"AVTSONDELESEN Bauteiltabelle | Spalten {string.Join(", ", s.Columns.Cast<Aecp.ScheduleTableStyleColumn>().Select(c => $"{c.Heading}={c.FullPropertyName}"))}");
            }
            aus.AddRange(BefehleKanon.FassungLesen(db, tr).Select(f => $"AVTSONDELESEN Fassung | {f}"));
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Sonde lesen");
    }
}
