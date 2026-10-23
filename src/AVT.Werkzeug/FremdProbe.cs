using System.Collections.Specialized;
using Avt.Kern;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.FremdProbe))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTFREMDPROBE (Nachweis zum Review 0.3.0): legt an einer Kopie Fälle an, die das Bereinigen nicht für „unbenutzt“
/// halten darf, und speichert als neue Datei. Erwartung nach AVTFREMDBEREINIGEN:
/// Probe_LeerAktuell (aktueller Layer, leer) bleibt · Probe_Pfeil (nur über Bemaßungs-Override per XData-Handle benutzt)
/// bleibt · ProbeSatz (nur per Name „ProbeSatz:Feld“ in einem Text) bleibt · Probe_Frei (unbenutzt, ohne Fremdspur) bleibt,
/// weil nicht vorab angehakt · TAGProbe (unbenutzt, alter Stempelblock) wird entfernt.
/// </summary>
public sealed class FremdProbe
{
    [CommandMethod("AVTFREMDPROBE", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Anlegen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei
            || Befehle.Pfad(doc.Editor, "Ergebniszeichnung") is not { } ziel) return;
        if (string.Equals(ziel, Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase) || string.Equals(ziel, datei, StringComparison.OrdinalIgnoreCase))
        {
            doc.Editor.WriteMessage("\nAVT abgelehnt: Ergebniszeichnung muss eine neue .dwg sein.");
            return;
        }
        var db = doc.Database;
        var aus = new List<string>();
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var modell = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ObjectId Block(string name)
                {
                    var b = new BlockTableRecord { Name = name };
                    bt.Add(b); tr.AddNewlyCreatedDBObject(b, true);
                    var l = new Line(new Point3d(0, 0, 0), new Point3d(1, 1, 0));
                    l.SetDatabaseDefaults(db);
                    b.AppendEntity(l); tr.AddNewlyCreatedDBObject(l, true);
                    return b.ObjectId;
                }
                // 1) leerer Layer als aktueller Layer
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = "Probe_LeerAktuell" };
                lt.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                db.Clayer = layer.ObjectId;
                // 2) Pfeilblock nur über Bemaßungs-Override (XData ACAD/DSTYLE mit Handle)
                var pfeil = Block("Probe_Pfeil");
                var bem = new AlignedDimension(new Point3d(0, 0, 0), new Point3d(100, 0, 0), new Point3d(50, 20, 0), "", db.Dimstyle);
                bem.SetDatabaseDefaults(db);
                bem.Layer = "0";
                modell.AppendEntity(bem); tr.AddNewlyCreatedDBObject(bem, true);
                bem.Dimblk = pfeil;
                // 3) Satzdefinition nur per Name in einem Text
                var dict = new Aecp.DictionaryPropertySetDefinitions(db);
                var satz = new Aecp.PropertySetDefinition();
                satz.SetToStandard(db);
                satz.SubSetDatabaseDefaults(db);
                satz.SetAppliesToFilter(new StringCollection { "AecbDbSchematicSymbol" }, false);
                dict.AddNewRecord("ProbeSatz", satz);
                tr.AddNewlyCreatedDBObject(satz, true);
                var text = new MText { Contents = "ProbeSatz:Feld", Location = new Point3d(0, 50, 0) };
                text.SetDatabaseDefaults(db);
                text.Layer = "0";
                modell.AppendEntity(text); tr.AddNewlyCreatedDBObject(text, true);
                // 4) unbenutzt ohne Fremdspur / 5) unbenutzt mit Fremdspur
                Block("Probe_Frei");
                Block("TAGProbe");
                tr.Commit();
            }
            db.SaveAs(ziel, DwgVersion.Current);
            aus.Add($"AVTFREMDPROBE gespeichert | {ziel}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Fremdprobe");
    }
}
