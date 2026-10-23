using System.Globalization;
using Avt.Kern;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.LinExport))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTLINEXPORT: schreibt die Definition genannter Linientypen einer Zeichnung im .lin-Format (Striche, Texte, Formen), um
/// Linientypen ohne Quelldatei (z. B. DRUCKLEITUNG aus RI 9.1) in die Stammdaten zu übernehmen. Nur lesen, nur Kopien unter C:\Temp.
/// </summary>
public sealed class LinExport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [CommandMethod("AVTLINEXPORT", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Export()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei (.lin)") is not { } datei) return;
        var namen = doc.Editor.GetString(new Autodesk.AutoCAD.EditorInput.PromptStringOptions("\nLinientypen (| getrennt): ") { AllowSpaces = true })
            .StringResult.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var aus = new List<string> { $";; Aus {Path.GetFileName(doc.Name)} mit AVTLINEXPORT ({DateTime.Now:dd.MM.yyyy})" };
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var t = (LinetypeTable)tr.GetObject(doc.Database.LinetypeTableId, OpenMode.ForRead);
            foreach (var name in namen)
            {
                if (!t.Has(name)) { aus.Add($";; FEHLT: {name}"); continue; }
                var l = (LinetypeTableRecord)tr.GetObject(t[name], OpenMode.ForRead);
                aus.Add($"*{l.Name},{l.AsciiDescription}");
                var teile = new List<string> { "A" };
                for (var i = 0; i < l.NumDashes; i++)
                {
                    teile.Add(l.DashLengthAt(i).ToString("0.######", Inv));
                    var stil = l.ShapeStyleAt(i);
                    if (stil.IsNull) continue;
                    var s = (TextStyleTableRecord)tr.GetObject(stil, OpenMode.ForRead);
                    var text = l.TextAt(i);
                    var o = l.ShapeOffsetAt(i);
                    var zusatz = $"S={l.ShapeScaleAt(i).ToString("0.######", Inv)},R={(l.ShapeRotationAt(i) * 180 / Math.PI).ToString("0.###", Inv)},X={o.X.ToString("0.######", Inv)},Y={o.Y.ToString("0.######", Inv)}";
                    teile.Add(string.IsNullOrEmpty(text)
                        ? $"[{l.ShapeNumberAt(i)},{s.FileName},{zusatz}]"
                        : $"[\"{text}\",{s.Name},{zusatz}]");
                }
                aus.Add(string.Join(",", teile));
            }
            tr.Commit();
            aus.Add(";; AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($";; AVTENDE FEHLER {ex.GetBaseException().Message}"); }
        Befehle.Ablegen(doc.Editor, datei, aus, "Linientypen");
    }
}
