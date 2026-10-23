using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.BefehleKanon))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTKANON (AVT 0.2.0): kanonische Inventur der offenen Zeichnung in eine Textdatei (nur lesen, nur Kopien unter C:\Temp).
/// Kopf: Gesamtprüfsumme, Zahl der Objekte, Fassungsstempel EHS_Zeichnung (oder „fehlt“). Danach je Objekt Pfad, Klasse,
/// Prüfsumme; Objekte unter dem Detail-Präfix zusätzlich mit ihrem ganzen Inhalt (zur Fehlersuche).
/// </summary>
public sealed class BefehleKanon
{
    public const string Stempel = "EHS_Zeichnung";

    [CommandMethod("AVTKANON", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Inventur()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        // Mehrere Präfixe durch | getrennt („-“ = keins).
        var praefix = doc.Editor.GetString(new Autodesk.AutoCAD.EditorInput.PromptStringOptions("\nDetail-Präfixe (| getrennt, - = keins): ") { AllowSpaces = true })
            .StringResult.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(v => v != "-").ToList();
        var aus = new List<string>();
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var b = Bestand.Lies(doc.Database, tr);
            var zeilen = Kanon.Lies(doc.Database, tr, b, praefix);
            aus.Add($"AVTKANON Gesamt | {Kanon.Gesamt(zeilen)}");
            aus.Add($"AVTKANON Objekte | {zeilen.Count}");
            aus.Add($"AVTKANON Unlesbar | {b.Unlesbar}");
            aus.AddRange(FassungLesen(doc.Database, tr).Select(f => $"AVTKANON Fassung | {f}"));
            foreach (var z in zeilen)
            {
                aus.Add($"{z.Pfad}\t{z.Klasse}\t{z.Pruefsumme}");
                if (z.Inhalt is not null) aus.Add($"#INHALT {z.Inhalt}");
            }
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Kanon");
    }

    /// <summary>Fassungsstempel aus dem NOD-Eintrag EHS_Zeichnung (Xrecord, Paare Schlüssel/Wert als Texte).</summary>
    internal static IEnumerable<string> FassungLesen(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Stempel)) return ["fehlt"];
        if (tr.GetObject(nod.GetAt(Stempel), OpenMode.ForRead) is not Xrecord x || x.Data is null) return ["unlesbar"];
        var werte = x.Data.AsArray().Select(t => Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList();
        return werte.Chunk(2).Select(p => p.Length == 2 ? $"{p[0]} = {p[1]}" : p[0]);
    }
}
