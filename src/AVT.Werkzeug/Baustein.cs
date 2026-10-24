using Avt.Kern;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.Baustein))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTBAUSTEIN (AVT 0.4.0, Bauplan „Bausteine“): kopiert einen Block aus der offenen Vorlage (Kopie) in eine neue, sonst
/// leere Zeichnung und benennt ihn um (z. B. Rahmen aus RI 9.1 → EHS_Rahmen). Prüft, dass der Baustein nichts Fremdes
/// mitbringt: nur Layer 0, keine Texte, keine Fremd-RegApps, keine verschachtelten Blöcke. Die offene Zeichnung bleibt unverändert.
/// </summary>
public sealed class Baustein
{
    [CommandMethod("AVTBAUSTEIN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Anlegen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei || Befehle.Pfad(doc.Editor, "Baustein-Zeichnung") is not { } ziel) return;
        var quelle = doc.Editor.GetString(new PromptStringOptions("\nBlock in der Vorlage: ") { AllowSpaces = true }).StringResult;
        var neu = doc.Editor.GetString(new PromptStringOptions("\nName im Baustein: ") { AllowSpaces = true }).StringResult;
        var aus = new List<string>();
        try
        {
            if (!string.Equals(Path.GetExtension(ziel), ".dwg", StringComparison.OrdinalIgnoreCase) || File.Exists(ziel))
                throw new InvalidOperationException("Baustein-Zeichnung muss eine neue .dwg sein");
            var db = doc.Database;
            ObjectId id;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(quelle)) throw new InvalidOperationException($"Block {quelle} fehlt");
                id = bt[quelle];
                tr.Commit();
            }
            using var bs = new Database(true, true);
            db.WblockCloneObjects(new ObjectIdCollection { id }, bs.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
            using (var tr = bs.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(bs.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[quelle], OpenMode.ForWrite);
                btr.Name = neu;
                var fehler = Pruefen(tr, bs, btr);
                aus.AddRange(fehler.Select(f => $"AVTBAUSTEIN FEHLER | {f}"));
                var arten = btr.Cast<ObjectId>().GroupBy(k => k.ObjectClass.DxfName).Select(g => $"{g.Count()} {g.Key}");
                aus.Add($"AVTBAUSTEIN Block | {quelle} → {neu} | {string.Join(", ", arten)} | dynamisch {btr.IsDynamicBlock}");
                if (fehler.Count > 0) throw new InvalidOperationException($"{fehler.Count} Fehler – Baustein nicht gespeichert");
                tr.Commit();
            }
            bs.SaveAs(ziel, DwgVersion.Current);
            using (var s = File.OpenRead(ziel))
                aus.Add($"AVTBAUSTEIN gespeichert | {ziel} | SHA256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s))}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Baustein");
    }

    /// <summary>Fremdes im Baustein: andere Layer als 0, Texte/Attribute, verschachtelte Blöcke, RegApps außer ACAD und den AutoCAD-eigenen.</summary>
    static List<string> Pruefen(Transaction tr, Database bs, BlockTableRecord btr)
    {
        var fehler = new List<string>();
        foreach (ObjectId k in btr)
        {
            var e = (Entity)tr.GetObject(k, OpenMode.ForRead);
            if (e.Layer != "0") fehler.Add($"Objekt auf Layer {e.Layer}");
            if (e is DBText or MText or AttributeDefinition) fehler.Add($"Text im Baustein ({e.GetType().Name})");
            if (e is BlockReference) fehler.Add("verschachtelter Block");
        }
        foreach (ObjectId a in (RegAppTable)tr.GetObject(bs.RegAppTableId, OpenMode.ForRead))
        {
            var n = ((RegAppTableRecord)tr.GetObject(a, OpenMode.ForRead)).Name;
            if (!n.StartsWith("ACAD", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Ac", StringComparison.Ordinal) && !n.StartsWith("AEC", StringComparison.OrdinalIgnoreCase))
                fehler.Add($"Fremd-RegApp {n}");
        }
        foreach (ObjectId l in (LayerTable)tr.GetObject(bs.LayerTableId, OpenMode.ForRead))
        {
            var n = ((LayerTableRecord)tr.GetObject(l, OpenMode.ForRead)).Name;
            if (n != "0") fehler.Add($"Layer {n} kam mit");
        }
        return fehler;
    }
}
