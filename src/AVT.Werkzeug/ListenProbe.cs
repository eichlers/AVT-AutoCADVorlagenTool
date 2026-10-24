using System.Collections.Specialized;
using Avt.Kern;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AecDb = Autodesk.Aec.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.ListenProbe))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTLISTENPROBE (AVT 0.4.0, nur an Kopien): AUDIT meldet an der Vorlage „Eigenschaftssatzdefinition (Liste) eNullObjectId
/// Standard“. Die Probe zeigt die Listen der Zeichnung mit ihren Eigenschaften und kann eine Variante speichern
/// (- = nichts, sonst: „Filter“ = Gültigkeitsbereich der Standardliste setzen, „Weg“ = Standardliste entfernen).
/// </summary>
public sealed class ListenProbe
{
    static string Schluessel(DBDictionary d) { var l = new List<string>(); foreach (DBDictionaryEntry e in d) l.Add(e.Key); return string.Join(", ", l); }

    [CommandMethod("AVTLISTENPROBE", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Probe()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var variante = doc.Editor.GetString(new PromptStringOptions("\nVariante (-, Filter, Weg): ")).StringResult;
        var ziel = variante == "-" ? null : Befehle.Pfad(doc.Editor, "Variante speichern als");
        var db = doc.Database;
        var aus = new List<string>();
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                aus.Add($"AVTLISTEN NOD vor AEC-Zugriff | AEC_LIST_DEFINITIONS {(nod.Contains("AEC_LIST_DEFINITIONS") ? Schluessel((DBDictionary)tr.GetObject(nod.GetAt("AEC_LIST_DEFINITIONS"), OpenMode.ForRead)) : "fehlt")}");
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = new AecDb.DictionaryListDefinition(db);
                foreach (ObjectId id in dict.Records)
                {
                    if (id.IsErased) continue;
                    var l = (AecDb.ListDefinition)tr.GetObject(id, OpenMode.ForRead);
                    var props = string.Join(" ", l.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name is not ("Database" or "UnmanagedObject" or "XData"))
                        .Select(p => { try { var v = p.GetValue(l); return $"{p.Name}={(v is StringCollection s ? string.Join(",", s.Cast<string>()) : v)}"; } catch (System.Exception) { return $"{p.Name}=?"; } }));
                    aus.Add($"AVTLISTEN Liste | {l.Name} | Einträge {l.GetListItems().Count} | {props}");
                }
                if (variante != "-" && dict.Has("Standard", tr))
                {
                    var s = (AecDb.ListDefinition)tr.GetObject(dict.GetAt("Standard"), OpenMode.ForWrite);
                    if (variante == "Filter") { s.AppliesToFilter = new StringCollection { "AecListUserManualPropertyDef" }; aus.Add("AVTLISTEN Variante | Filter gesetzt"); }
                    if (variante == "Weg") { s.Erase(); aus.Add("AVTLISTEN Variante | Standardliste entfernt"); }
                }
                tr.Commit();
            }
            if (ziel is not null) { db.SaveAs(ziel, DwgVersion.Current); aus.Add($"AVTLISTEN gespeichert | {ziel}"); }
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Listenprobe");
    }
}
