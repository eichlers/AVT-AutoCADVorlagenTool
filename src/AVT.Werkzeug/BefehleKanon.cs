using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.BefehleKanon))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTKANON (AVT 0.2.0): kanonische Inventur der offenen Zeichnung in eine Textdatei (nur lesen, nur Kopien unter C:\Temp).
/// Kopf: Gesamtprüfsumme, Zahl der Objekte, Fassungsstempel (oder „fehlt“), DWGPROPS und Kopfwerte. Danach je Objekt Pfad, Klasse,
/// Prüfsumme; Objekte unter dem Detail-Präfix zusätzlich mit ihrem ganzen Inhalt (zur Fehlersuche).
/// </summary>
public sealed class BefehleKanon
{
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
            aus.AddRange(DwgProps(doc.Database).Select(f => $"AVTKANON DWGPROPS | {f}"));
            var vorhanden = DwgProps(doc.Database).Where(f => f.StartsWith('*') && !f.EndsWith(" = ")).Select(f => f[1..].Split(" = ")[0]).ToHashSet();
            var fehlend = Standard.PflichtFelder.Where(f => !vorhanden.Contains(f)).ToList();
            aus.Add($"AVTKANON Pflichtfelder | {(fehlend.Count == 0 ? "alle da" : "fehlt: " + string.Join(", ", fehlend))}");
            aus.Add($"AVTKANON Kopf | INSUNITS {(int)doc.Database.Insunits} | MEASUREMENT {(int)doc.Database.Measurement} | PSTYLEMODE {(doc.Database.PlotStyleMode ? 1 : 0)} | LTSCALE {doc.Database.Ltscale}");
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

    /// <summary>DWGPROPS: Standardfelder und benutzerdefinierte Felder als „Feld = Wert“ (benutzerdefinierte mit Präfix „*“).</summary>
    internal static IEnumerable<string> DwgProps(Database db)
    {
        var s = db.SummaryInfo;
        yield return $"Titel = {s.Title}";
        yield return $"Thema = {s.Subject}";
        yield return $"Autor = {s.Author}";
        yield return $"Schlüsselwörter = {s.Keywords}";
        yield return "Kommentare = " + (s.Comments ?? "").Replace(((char)13).ToString(), "").Replace(((char)10).ToString(), " / ");
        yield return $"Revisionsnummer = {s.RevisionNumber}";
        yield return $"Hyperlink-Basis = {s.HyperlinkBase}";
        var e = s.CustomProperties;
        while (e.MoveNext()) yield return $"*{e.Key} = {e.Value}";
    }

    /// <summary>Fassungsstempel aus dem NOD-Eintrag <see cref="Standard.Stempel"/> (Xrecord, Paare Schlüssel/Wert als Texte).</summary>
    internal static IEnumerable<string> FassungLesen(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Standard.Stempel)) return ["fehlt"];
        if (tr.GetObject(nod.GetAt(Standard.Stempel), OpenMode.ForRead) is not Xrecord x || x.Data is null) return ["unlesbar"];
        var werte = x.Data.AsArray().Select(t => Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList();
        return werte.Chunk(2).Select(p => p.Length == 2 ? $"{p[0]} = {p[1]}" : p[0]);
    }
}
