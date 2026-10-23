using Avt.Kern;
using Avt.Kern.Fremd;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.BefehleFremd))]
namespace Avt.Werkzeug;

/// <summary>
/// Fremdinhalte ohne Oberfläche (AVT 0.3.0) – derselbe Kern wie „Bereinigen“ in der Palette, zum Nachweis an Kopien:
/// AVTFREMD (Vorschau a/b/c, nur lesen) und AVTFREMDBEREINIGEN (alle a entfernen, unter neuem Namen speichern, Nachprüfung).
/// </summary>
public sealed class BefehleFremd
{
    [CommandMethod("AVTFREMD", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Vorschau()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var aus = new List<string>();
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            Schreibe(Fremdanalyse.Lies(doc.Database, tr, null), aus, "AVTFREMD");
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Fremd");
    }

    [CommandMethod("AVTFREMDBEREINIGEN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Bereinigen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei
            || Befehle.Pfad(doc.Editor, "Ergebniszeichnung") is not { } ziel) return;
        if (string.Equals(ziel, Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase) || string.Equals(ziel, datei, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(ziel), ".dwg", StringComparison.OrdinalIgnoreCase))
        {
            doc.Editor.WriteMessage("\nAVT abgelehnt: Ergebniszeichnung muss eine neue .dwg sein.");
            return;
        }
        var db = doc.Database;
        var aus = new List<string>();
        try
        {
            Analyse vorher;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                vorher = Fremdanalyse.Lies(db, tr, null);
                tr.Commit();
            }
            Schreibe(vorher, aus, "AVTFREMD vorher");
            var alleA = vorher.Funde.Where(f => f.Gruppe == Gruppe.FremdUnbenutzt).Select(f => f.Kandidat.Pfad).ToList();
            var ergebnis = Fremdbereiniger.Ausfuehren(db, alleA, new Dictionary<string, string>(), null);
            aus.AddRange(ergebnis);
            if (ergebnis.Any(z => z.StartsWith("AVTFREMD FEHLER", StringComparison.Ordinal)))
                throw new InvalidOperationException("Fehler beim Entfernen, nichts gespeichert");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Schreibe(Fremdanalyse.Lies(db, tr, null), aus, "AVTFREMD nachher");
                tr.Commit();
            }
            db.SaveAs(ziel, DwgVersion.Current);
            aus.Add($"AVTFREMD gespeichert | {ziel}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Fremd bereinigen");
    }

    static void Schreibe(Analyse a, List<string> aus, string vorsilbe)
    {
        aus.Add($"{vorsilbe} Kopf | Fassung {a.Fassung ?? "keine"} | Referenz {a.Referenz.Fassung} ({a.Referenz.Herkunft}) | Objekte {a.Objektzahl} | Dauer {a.Dauer.TotalMilliseconds:F0} ms");
        aus.Add($"{vorsilbe} Kurz | {Einteilung.Kurzfassung(a.Funde) ?? "keine Fremdinhalte"}");
        foreach (var g in a.Funde.GroupBy(f => f.Gruppe))
            aus.Add($"{vorsilbe} Zahl | {g.Key} | {g.Count()}");
        foreach (var f in a.Funde)
            aus.Add($"{vorsilbe} {Kennung(f.Gruppe)} | {f.Kandidat.Art} | {Kanon.Esc(f.Kandidat.Pfad)} | {Kanon.Esc(f.Grund)}");
    }

    static string Kennung(Gruppe g) => g switch { Gruppe.FremdUnbenutzt => "a", Gruppe.FremdBenutzt => "b", _ => "c" };
}
