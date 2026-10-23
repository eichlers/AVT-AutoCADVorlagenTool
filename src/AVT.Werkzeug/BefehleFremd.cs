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
            // Schnelle Vorprüfung wie beim leisen Hinweis (nur Namen) – mit Zeit, zum Beleg „kein Bremsen“.
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            var (anzahl, referenz) = Fremdanalyse.Vorpruefung(doc.Database, tr, null);
            aus.Add($"AVTFREMD Vorprüfung | {anzahl} Einträge nicht in {referenz} | {uhr.ElapsedMilliseconds} ms");
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
        // b-Funde als Ausnahme merken (wie „behalten“ in der Palette) – j/n.
        var antwort = doc.Editor.GetString(new Autodesk.AutoCAD.EditorInput.PromptStringOptions(Zeile("b-Funde als Ausnahme merken (j/n): ")));
        if (antwort.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;
        var merken = antwort.StringResult.Trim().StartsWith("j", StringComparison.OrdinalIgnoreCase);
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
            // Wie die Palette: nur, was dort vorab angehakt ist (a mit Fremdspur), mit Handle aus der Vorschau.
            var alleA = vorher.Funde.Where(f => f.Gruppe == Gruppe.FremdUnbenutzt && f.Vorgabe)
                .ToDictionary(f => f.Kandidat.Pfad, f => vorher.Objekte[f.Kandidat.Pfad].Handle.Value, StringComparer.Ordinal);
            aus.Add($"AVTFREMD gewählt | a mit Fremdspur {alleA.Count} | a ohne Fremdspur (nicht angehakt) {vorher.Funde.Count(f => f.Gruppe == Gruppe.FremdUnbenutzt && !f.Vorgabe)}");
            var behalten = merken
                ? vorher.Funde.Where(f => f.Gruppe == Gruppe.FremdBenutzt).ToDictionary(f => f.Kandidat.Pfad, _ => "behalten (Nachweis ohne Oberfläche)")
                : new Dictionary<string, string>();
            var ergebnis = Fremdbereiniger.Ausfuehren(db, alleA, behalten, null);
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
        aus.Add($"{vorsilbe} Lücke | {a.Luecke}");
        aus.Add($"{vorsilbe} Kopf | Fassung {a.Fassung ?? "keine"} | Referenz {a.Referenz.Fassung} ({a.Referenz.Herkunft}) | Objekte {a.Objektzahl} | Dauer {a.Dauer.TotalMilliseconds:F0} ms");
        aus.Add($"{vorsilbe} Kurz | {Einteilung.Kurzfassung(a.Funde) ?? "keine Fremdinhalte"}");
        foreach (var g in a.Funde.GroupBy(f => f.Gruppe))
            aus.Add($"{vorsilbe} Zahl | {g.Key} | {g.Count()}");
        foreach (var f in a.Funde)
            aus.Add($"{vorsilbe} {Kennung(f.Gruppe)}{(f.Vorgabe ? "+" : "")} | {f.Kandidat.Art} | {Kanon.Esc(f.Kandidat.Pfad)} | {Kanon.Esc(f.Grund)}");
    }

    static string Zeile(string text) => Environment.NewLine + text;

    static string Kennung(Gruppe g) => g switch { Gruppe.FremdUnbenutzt => "a", Gruppe.FremdBenutzt => "b", _ => "c" };
}
