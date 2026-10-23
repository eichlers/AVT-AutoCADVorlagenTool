using Avt.Kern;
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
            // Objekte, deren Inhalt nicht zählt (Fehler beim Lesen, instabile Klassen) – Prüfen warnt bei Fehlern.
            aus.Add($"AVTKANON Fehler | {zeilen.Count(z => z.Zustand == "fehler")}");
            aus.Add($"AVTKANON Instabil | {zeilen.Count(z => z.Zustand == "instabil")}");
            foreach (var z in zeilen.Where(z => z.Zustand == "fehler")) aus.Add($"AVTKANON FehlerObjekt | {z.Pfad} | {z.Klasse}");
            aus.AddRange(Zeichnungsangaben.FassungLesen(doc.Database, tr).Select(f => $"AVTKANON Fassung | {Kanon.Esc(f)}"));
            aus.Add($"AVTKANON Feldpraefix | {Standard.Feld}");
            aus.AddRange(Zeichnungsangaben.DwgProps(doc.Database).Select(f => $"AVTKANON DWGPROPS | {Kanon.Esc(f)}"));
            var fehlend = Zeichnungsangaben.FehlendePflichtfelder(doc.Database);
            aus.Add($"AVTKANON Pflichtfelder | {(fehlend.Count == 0 ? "alle da" : "fehlt: " + string.Join(", ", fehlend))}");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            aus.Add($"AVTKANON Kopf | INSUNITS {(int)doc.Database.Insunits} | MEASUREMENT {(int)doc.Database.Measurement} | PSTYLEMODE {(doc.Database.PlotStyleMode ? 1 : 0)} | LTSCALE {doc.Database.Ltscale.ToString("G12", inv)}");
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
}
