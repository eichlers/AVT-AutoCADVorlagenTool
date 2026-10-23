using Avt.Kern;
using Avt.Kern.Erzeugen;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.BefehleErzeugen))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTERZEUGEN (AVT 0.4.0 Teil 1): baut auf der offenen Kopie der Leervorlage aus den Stammdaten und speichert den
/// Entwurf unter neuem Namen (nur C:\Temp). AVTSTAMMPRUEFEN: prüft eine Zeichnung gegen die Stammdaten (nur lesen).
/// </summary>
public sealed class BefehleErzeugen
{
    static string Text(Editor ed, string frage)
    {
        var r = ed.GetString(new PromptStringOptions(Environment.NewLine + frage + ": ") { AllowSpaces = true });
        if (r.Status != PromptStatus.OK) throw new OperationCanceledException(frage);
        return r.StringResult.Trim().Trim('"');
    }

    [CommandMethod("AVTERZEUGEN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Erzeugen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei
            || Befehle.Pfad(doc.Editor, "Ergebniszeichnung") is not { } ziel) return;
        var aus = new List<string>();
        try
        {
            var stammdaten = Text(doc.Editor, "Stammdaten-Ordner");
            var systeme = Text(doc.Editor, "Systeme-Tabelle");
            var auftrag = new Bauauftrag(Text(doc.Editor, "Fassung"), Text(doc.Editor, "Vorgänger"), Text(doc.Editor, "Stand der Stammdaten"),
                Text(doc.Editor, "Basis"), Text(doc.Editor, "AVT-Version"));
            if (string.Equals(ziel, Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase) || string.Equals(ziel, datei, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ergebniszeichnung muss eine neue Datei sein");
            aus.AddRange(Erzeuger.Bauen(doc.Database, stammdaten, systeme, auftrag));
            doc.Database.SaveAs(ziel, DwgVersion.Current);
            aus.Add($"AVTERZEUGEN gespeichert | {ziel}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Erzeugen");
    }

    [CommandMethod("AVTSTAMMPRUEFEN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Pruefen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var aus = new List<string>();
        try
        {
            var stammdaten = Text(doc.Editor, "Stammdaten-Ordner");
            var systeme = Text(doc.Editor, "Systeme-Tabelle");
            using var tr = doc.Database.TransactionManager.StartTransaction();
            aus.AddRange(Stammpruefung.Pruefen(doc.Database, tr, stammdaten, systeme));
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Stammdaten prüfen");
    }
}
