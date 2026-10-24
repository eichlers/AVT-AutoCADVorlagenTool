using System.Collections.Specialized;
using Avt.Kern;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.ThemenBaustein))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTTHEMENBAUSTEIN (AVT 0.4.0): Die API kann die Darstellung einer neuen Themen-Komponente nicht anlegen, nur eine
/// vorhandene klonen (Sonde 23.10., Themenprobe 24.10.). Darum ein Baustein: ein Thema mit genau einer Komponente aus der
/// Vorlage (Kopie), Regel umgehängt auf den EHS-Leitungsstilsatz (nur dessen Felder, sonst nichts). Der Erzeuger klont daraus
/// alle Komponenten nach Themen.csv. In der offenen Kopie wird nichts gespeichert.
/// </summary>
public sealed class ThemenBaustein
{
    public const string Thema = Standard.Kuerzel + "_Thema_Baustein";
    public const string Satz = Standard.Kuerzel + "_Leitungsstil";

    [CommandMethod("AVTTHEMENBAUSTEIN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Anlegen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei || Befehle.Pfad(doc.Editor, "Baustein-Zeichnung") is not { } ziel) return;
        var vorlage = doc.Editor.GetString(new PromptStringOptions("\nThema als Vorlage: ") { AllowSpaces = true }).StringResult;
        var aus = new List<string>();
        var db = doc.Database;
        try
        {
            if (!string.Equals(Path.GetExtension(ziel), ".dwg", StringComparison.OrdinalIgnoreCase) || File.Exists(ziel))
                throw new InvalidOperationException("Baustein-Zeichnung muss eine neue .dwg sein");
            ObjectId thema;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var satz = SatzAnlegen(db, tr);
                var feld = satz.Definitions.Cast<Aecp.PropertyDefinition>().First(f => f.Name == "Kuerzel");
                var dict = new AecDb.DictionaryDisplayThemeStyle(db);
                var quelle = (AecDb.DisplayThemeStyle)tr.GetObject(dict.GetAt(vorlage), OpenMode.ForRead);
                var t = (AecDb.DisplayThemeStyle)quelle.Clone();
                dict.AddNewRecord(Thema, t);
                tr.AddNewlyCreatedDBObject(t, true);
                while (t.DisplayThemeComponents.Count > 1) t.DisplayThemeComponents.RemoveAt(t.DisplayThemeComponents.Count - 1);
                var k = t.DisplayThemeComponents[0];
                k.Name = "Baustein";
                Avt.Kern.Erzeugen.Themen.Umhaengen(k, satz.ObjectId, feld.Id, "");
                t.Title = "Baustein";
                t.Description = "";
                thema = t.ObjectId;
                aus.Add($"AVTTHEMENBAUSTEIN Thema | aus {vorlage} | 1 Komponente | Regel {Regeltext(k.Rules)} | Darstellung {(k.DisplayProperties is null ? "LEER" : "da")}");
                tr.Commit();
            }
            using var bs = new Database(true, true);
            var zielDict = new AecDb.DictionaryDisplayThemeStyle(bs);
            if (zielDict.DictionaryId.IsNull) throw new InvalidOperationException("Themen-Wörterbuch im Baustein nicht vorhanden");
            db.WblockCloneObjects(new ObjectIdCollection { thema }, zielDict.DictionaryId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
            using (var tr = bs.TransactionManager.StartTransaction())
            {
                var saetze = new Aecp.DictionaryPropertySetDefinitions(bs).Records.Cast<ObjectId>().Where(i => !i.IsErased)
                    .Select(i => ((AecDb.DictionaryRecord)tr.GetObject(i, OpenMode.ForRead)).Name).ToList();
                var themen = new AecDb.DictionaryDisplayThemeStyle(bs).Records.Cast<ObjectId>().Where(i => !i.IsErased)
                    .Select(i => (AecDb.DisplayThemeStyle)tr.GetObject(i, OpenMode.ForRead)).ToList();
                aus.Add($"AVTTHEMENBAUSTEIN Inhalt | Sätze {string.Join(", ", saetze)} | Themen {string.Join(", ", themen.Select(x => $"{x.Name} ({x.DisplayThemeComponents.Count})"))}");
                var fremd = saetze.Where(s => s != Satz).ToList();
                if (fremd.Count > 0) throw new InvalidOperationException($"Fremde Sätze im Baustein: {string.Join(", ", fremd)}");
                var b = themen.FirstOrDefault(x => x.Name == Thema) ?? throw new InvalidOperationException("Baustein-Thema fehlt nach dem Klonen");
                if (b.DisplayThemeComponents[0].DisplayProperties is null) throw new InvalidOperationException("Darstellung der Baustein-Komponente leer");
                tr.Commit();
            }
            bs.SaveAs(ziel, DwgVersion.Current);
            using (var s = File.OpenRead(ziel))
                aus.Add($"AVTTHEMENBAUSTEIN gespeichert | {ziel} | SHA256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s))}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Themenbaustein");
    }

    /// <summary>Minimaler Leitungsstilsatz in der Kopie (nur zum Umhängen der Regel; Felder wie im Feldkatalog).</summary>
    static Aecp.PropertySetDefinition SatzAnlegen(Database db, Transaction tr)
    {
        var dict = new Aecp.DictionaryPropertySetDefinitions(db);
        var d = new Aecp.PropertySetDefinition();
        d.SetToStandard(db);
        d.SubSetDatabaseDefaults(db);
        d.SetAppliesToFilter(new StringCollection { "AecbDbSchematicStyle" }, true);
        dict.AddNewRecord(Satz, d);
        tr.AddNewlyCreatedDBObject(d, true);
        foreach (var (name, quelle) in new[] { ("Kuerzel", "Abkürzung"), ("Rohrklasse", "Stil") })
        {
            var f = new Aecp.PropertyDefinition();
            f.SetToStandard(db);
            f.SubSetDatabaseDefaults(db);
            f.Name = name;
            f.Automatic = true;
            f.SetAutomaticData("AecbDbSchematic", quelle);
            d.Definitions.Add(f);
        }
        return d;
    }

    /// <summary>Kopfregel und alle Unterregeln auf Satz/Feld umhängen und den Wert setzen (Kopf zeigt in RI 9.1 auf einen alten Satz).</summary>
    internal static void Umhaengen(AecDb.DisplayThemeComponentBase k, ObjectId satz, int feld, string wert)
    {
        var kopf = k.Rules as Aecp.DisplayThemeScheduleDataRule ?? throw new InvalidOperationException($"Regeltyp {k.Rules?.GetType().Name} nicht umhängbar");
        kopf.SetPropertySetDefinitionId(satz, false);
        kopf.SetPropertyDefinitionId(feld, false);
        // Unterregeln nur über den Enumerator (der Index liefert ImpObject – Befund 24.10.2026), Änderung am Element selbst.
        var unter = ((System.Collections.IEnumerable)kopf.DisplayThemeRules).Cast<object>().ToList();
        if (unter.Count != 1) throw new InvalidOperationException($"Komponente {k.Name}: {unter.Count} Unterregeln – erwartet 1");
        dynamic u = unter[0];
        u.SetPropertySetDefinitionId(satz, false);
        u.SetPropertyDefinitionId(feld, false);
        u.ComparisonOperator = AecDb.DisplayThemeRuleComparisonOperator.RuleComparisonOperatorEqualTo;
        u.Value = wert;
        k.Rules = kopf;
    }

    internal static string Regeltext(object? kopf) =>
        kopf is Aecp.DisplayThemeScheduleDataRule r && ((System.Collections.IEnumerable)r.DisplayThemeRules).Cast<object>().FirstOrDefault() is { } u
            ? Text((dynamic)u) : kopf?.GetType().Name ?? "keine";

    static string Text(dynamic u) => $"{u.PropertyOwnerName}.{u.PropertyName} = „{u.ValueAsString}“";
}
