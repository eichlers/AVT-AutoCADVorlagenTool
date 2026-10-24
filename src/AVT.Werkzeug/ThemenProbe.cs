using Avt.Kern;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.ThemenProbe))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTTHEMENPROBE (AVT 0.4.0, nur an Kopien): Wie lassen sich die Themen der Vorlage für EHS umbauen? Je Versuch eine eigene
/// Transaktion, am Ende verworfen (nichts wird gespeichert). Versuche: Komponente klonen (bleibt die Darstellung?), Regel auf
/// einen anderen Satz/Feld umhängen, Wert/Name/Farbe setzen.
/// </summary>
public sealed class ThemenProbe
{
    [CommandMethod("AVTTHEMENPROBE", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Probe()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var db = doc.Database;
        var aus = new List<string>();
        void Versuch(string was, Func<Transaction, string> tun)
        {
            using var tr = db.TransactionManager.StartTransaction();
            try { aus.Add($"AVTTHEMEN {was} | {tun(tr)}"); }
            catch (System.Exception ex) { aus.Add($"AVTTHEMEN {was} | FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
            finally { tr.Abort(); }
        }
        AecDb.DisplayThemeStyle Thema(Transaction tr, string name, OpenMode m) =>
            (AecDb.DisplayThemeStyle)tr.GetObject(new AecDb.DictionaryDisplayThemeStyle(db).GetAt(name), m);

        Versuch("Regel Medien 1-3", tr =>
        {
            var t = Thema(tr, "Medien", OpenMode.ForRead);
            return string.Join(" || ", t.DisplayThemeComponents.Cast<AecDb.DisplayThemeComponentBase>().Take(3).Select(k => Regel(tr, k.Rules)));
        });
        Versuch("Regel RohrKanalKlassen 1-3", tr =>
        {
            var t = Thema(tr, "RohrKanalKlassen", OpenMode.ForRead);
            return string.Join(" || ", t.DisplayThemeComponents.Cast<AecDb.DisplayThemeComponentBase>().Take(3).Select(k => Regel(tr, k.Rules)));
        });
        Versuch("Komponente klonen", tr =>
        {
            var t = Thema(tr, "Medien", OpenMode.ForWrite);
            var vorher = t.DisplayThemeComponents.Count;
            var k = (AecDb.DisplayThemeComponentBase)t.DisplayThemeComponents[0].Clone();
            k.Name = "AVT Probe";
            t.DisplayThemeComponents.Add(k);
            var neu = t.DisplayThemeComponents[t.DisplayThemeComponents.Count - 1];
            var dp = neu.DisplayProperties;
            if (dp is not null) dp.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            return $"{vorher} → {t.DisplayThemeComponents.Count}, Darstellung {(dp is null ? "LEER" : "da, Farbe gesetzt " + neu.DisplayProperties?.Color)}, Regel {Regel(tr, neu.Rules)}";
        });
        Versuch("Komponente entfernen", tr =>
        {
            var t = Thema(tr, "Medien", OpenMode.ForWrite);
            var vorher = t.DisplayThemeComponents.Count;
            t.DisplayThemeComponents.RemoveAt(vorher - 1);
            return $"{vorher} → {t.DisplayThemeComponents.Count}";
        });
        Versuch("Regel umhängen und Wert setzen", tr =>
        {
            var t = Thema(tr, "Medien", OpenMode.ForWrite);
            var k = t.DisplayThemeComponents[0];
            if (k.Rules is not Aecp.DisplayThemeScheduleDataRule r) return $"Regeltyp {k.Rules?.GetType().Name} – nicht umhängbar";
            var ziel = new Aecp.DictionaryPropertySetDefinitions(db);
            var satz = (Aecp.PropertySetDefinition)tr.GetObject(ziel.GetAt("TAGBauteil"), OpenMode.ForRead);
            var feld = satz.Definitions.Cast<Aecp.PropertyDefinition>().First();
            r.SetPropertySetDefinitionId(satz.ObjectId, false);
            r.SetPropertyDefinitionId(feld.Id, false);
            r.Value = "AVT-PROBE";
            k.Rules = r;
            return $"jetzt {Regel(tr, t.DisplayThemeComponents[0].Rules)}";
        });
        Versuch("Thema klonen", tr =>
        {
            var t = Thema(tr, "Medien", OpenMode.ForRead);
            var dict = new AecDb.DictionaryDisplayThemeStyle(db);
            var kopie = (AecDb.DisplayThemeStyle)t.Clone();
            dict.AddNewRecord("AVT_Probe_Thema", kopie);
            tr.AddNewlyCreatedDBObject(kopie, true);
            return $"{kopie.DisplayThemeComponents.Count} Komponenten, 1. Darstellung {(kopie.DisplayThemeComponents[0].DisplayProperties is null ? "LEER" : "da")}";
        });
        aus.Add("AVTENDE ok");
        Befehle.Ablegen(doc.Editor, datei, aus, "Themenprobe");
    }

    static string Regel(Transaction tr, object? r)
    {
        if (r is null) return "keine";
        var teile = new List<string> { r.GetType().Name };
        foreach (var p in r.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0) continue;
            object? v;
            try { v = p.GetValue(r); } catch (System.Exception) { continue; }
            if (v is ObjectId o && !o.IsNull)
            {
                try { var x = tr.GetObject(o, OpenMode.ForRead); v = x is AecDb.DictionaryRecord d ? d.Name : x.GetType().Name; } catch (System.Exception) { }
            }
            teile.Add($"{p.Name}={v}");
        }
        foreach (var m in new[] { "GetPropertySetDefinitionId", "GetPropertyDefinitionId" })
            try { teile.Add($"{m}()={r.GetType().GetMethod(m, Type.EmptyTypes)?.Invoke(r, null)}"); } catch (System.Exception) { }
        return string.Join(" ", teile);
    }
}
