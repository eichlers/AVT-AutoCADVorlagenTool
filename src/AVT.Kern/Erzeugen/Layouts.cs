using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Avt.Kern.Erzeugen;

/// <summary>Ein Ansichtsfenster aus Layouts.csv: „x1,y1,x2,y2,1:50“ (Papier-mm).</summary>
public sealed record Fenster(double X1, double Y1, double X2, double Y2, double Massstab)
{
    public static IReadOnlyList<Fenster> Lies(string text) => text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t =>
    {
        var m = Regex.Match(t, @"^(-?[\d.]+),(-?[\d.]+),(-?[\d.]+),(-?[\d.]+),1:([\d.]+)$");
        if (!m.Success) throw new FormatException($"Ansichtsfenster „{t}“ – erwartet x1,y1,x2,y2,1:n");
        double Z(int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        if (Z(3) <= Z(1) || Z(4) <= Z(2) || Z(5) <= 0) throw new FormatException($"Ansichtsfenster „{t}“ – Größe oder Maßstab ungültig");
        return new Fenster(Z(1), Z(2), Z(3), Z(4), 1.0 / Z(5));
    }).ToList();
}

/// <summary>
/// AVT 0.4.0 Teil 2 (AMK/Nutzer 24.10.2026): Rahmen für den Zeichenbereich wie im RI-9.1-Layout, neutral. Der Rahmen-Block
/// kommt als Baustein (Bausteine.csv, Prüfsumme) in die Vorlage, das Layout nach Layouts.csv: Plotter, Papier, Drehung,
/// Plotbereich, Maßstab, Rahmen mit Format, Ansichtsfenster. Ein Plankopf folgt später (Lage bleibt frei).
/// </summary>
internal static class Layouts
{
    public static List<string> Bauen(Database db, string stammdaten, Action<DBObject, string> id)
    {
        var aus = new List<string>();
        var bausteine = Tabelle.Lies(Path.Combine(stammdaten, "Bausteine.csv"), "ID", "Name", "Datei", "SHA256");
        foreach (var (_, w) in bausteine.Zeilen) aus.Add(BlockAusBaustein(db, stammdaten, w, id));
        foreach (var (_, w) in Tabelle.Lies(Path.Combine(stammdaten, "Layouts.csv"), "ID", "Layout", "Plotter", "Papier", "Drehung", "Plotbereich", "Massstab", "Rahmenblock", "Rahmenformat", "Rahmenlayer").Zeilen)
            aus.AddRange(Layout(db, w, id));
        return aus;
    }

    static string BlockAusBaustein(Database db, string stammdaten, IReadOnlyDictionary<string, string> w, Action<DBObject, string> id)
    {
        var datei = Path.Combine(stammdaten, w["Datei"]);
        string ist;
        using (var s = File.OpenRead(datei)) ist = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
        if (!ist.Equals(w["SHA256"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Baustein {w["Datei"]}: Prüfsumme {ist[..16]} … ≠ Bausteine.csv {w["SHA256"][..Math.Min(16, w["SHA256"].Length)]} …");
        using var bs = new Database(false, true);
        bs.ReadDwgFile(datei, FileOpenMode.OpenForReadAndAllShare, true, "");
        ObjectId quelle;
        using (var tr = bs.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(bs.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(w["Name"])) throw new InvalidDataException($"Baustein {w["Datei"]}: Block {w["Name"]} fehlt");
            quelle = bt[w["Name"]];
            tr.Commit();
        }
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)).Has(w["Name"])) throw new InvalidOperationException($"Block {w["Name"]} gibt es schon");
            tr.Commit();
        }
        var map = new IdMapping();
        bs.WblockCloneObjects(new ObjectIdCollection { quelle }, db.BlockTableId, map, DuplicateRecordCloning.Ignore, false);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(map[quelle].Value, OpenMode.ForWrite);
            id(btr, w["ID"]);
            tr.Commit();
        }
        return $"AVTERZEUGEN Baustein | {w["Name"]} | aus {w["Datei"]} (SHA256 {ist[..16]} …)";
    }

    static List<string> Layout(Database db, IReadOnlyDictionary<string, string> w, Action<DBObject, string> id)
    {
        var aus = new List<string>();
        var alt = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            var lm = LayoutManager.Current;
            var name = w["Layout"];
            var lid = lm.GetLayoutId(name);
            if (lid.IsNull) lid = lm.CreateLayout(name);
            using var tr = db.TransactionManager.StartTransaction();
            var layout = (Layout)tr.GetObject(lid, OpenMode.ForWrite);
            var psv = PlotSettingsValidator.Current;
            psv.SetPlotConfigurationName(layout, w["Plotter"], w["Papier"]);
            psv.SetPlotPaperUnits(layout, PlotPaperUnit.Millimeters);
            psv.SetPlotRotation(layout, w["Drehung"] switch
            {
                "0" => PlotRotation.Degrees000, "90" => PlotRotation.Degrees090, "180" => PlotRotation.Degrees180, "270" => PlotRotation.Degrees270,
                var d => throw new FormatException($"Layouts.csv: Drehung {d}"),
            });
            psv.SetPlotType(layout, Enum.Parse<Autodesk.AutoCAD.DatabaseServices.PlotType>(w["Plotbereich"]));
            if (w["Massstab"] != "1:1") throw new FormatException($"Layouts.csv: Layout-Maßstab {w["Massstab"]} – nur 1:1 vorgesehen");
            psv.SetUseStandardScale(layout, true);
            psv.SetStdScaleType(layout, StdScaleType.StdScale1To1);
            if (w.GetValueOrDefault("Plotstil") is { Length: > 0 } ctb) psv.SetCurrentStyleSheet(layout, ctb);
            psv.SetPlotCentered(layout, false);
            id(layout, w["ID"]);

            var raum = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
            // Leeres Papier: was die Basis mitbringt (Ansichtsfenster ohne Inhalt), räumt der Neubau ab.
            foreach (ObjectId e in raum.Cast<ObjectId>().ToList()) tr.GetObject(e, OpenMode.ForWrite).Erase();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(w["Rahmenblock"])) throw new InvalidDataException($"Layouts.csv: Rahmenblock {w["Rahmenblock"]} fehlt (Bausteine.csv)");
            var rahmen = new BlockReference(Point3d.Origin, bt[w["Rahmenblock"]]);
            rahmen.SetDatabaseDefaults(db);
            rahmen.Layer = w["Rahmenlayer"];
            raum.AppendEntity(rahmen);
            tr.AddNewlyCreatedDBObject(rahmen, true);
            var format = rahmen.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                .FirstOrDefault(p => p.GetAllowedValues().Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)).Contains(w["Rahmenformat"]))
                ?? throw new InvalidDataException($"Layouts.csv: Format „{w["Rahmenformat"]}“ gibt es im Rahmen nicht");
            format.Value = w["Rahmenformat"];
            aus.Add($"AVTERZEUGEN Layout | {name} | {w["Plotter"]} {w["Papier"]} {w["Drehung"]}° {w["Plotbereich"]} | Rahmen {w["Rahmenblock"]} {format.PropertyName} = {w["Rahmenformat"]}");

            var nr = 0;
            foreach (var f in Fenster.Lies(w.GetValueOrDefault("Ansichtsfenster") ?? ""))
            {
                var vp = new Viewport();
                vp.SetDatabaseDefaults(db);
                vp.Layer = w.GetValueOrDefault("Fensterlayer") is { Length: > 0 } fl ? fl : "0";
                vp.CenterPoint = new Point3d((f.X1 + f.X2) / 2, (f.Y1 + f.Y2) / 2, 0);
                vp.Width = f.X2 - f.X1;
                vp.Height = f.Y2 - f.Y1;
                raum.AppendEntity(vp);
                tr.AddNewlyCreatedDBObject(vp, true);
                vp.CustomScale = f.Massstab;
                vp.Locked = true;
                nr++;
                aus.Add($"AVTERZEUGEN Ansichtsfenster | {name} {nr} | {f.X1},{f.Y1}–{f.X2},{f.Y2} | 1:{Math.Round(1 / f.Massstab)}");
            }
            tr.Commit();
        }
        finally { HostApplicationServices.WorkingDatabase = alt; }
        return aus;
    }
}
