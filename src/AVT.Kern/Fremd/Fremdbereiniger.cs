using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;

namespace Avt.Kern.Fremd;

/// <summary>
/// Bereinigen einer Zeichnung nach der Vorschau (Auftrag AMK 23.10.2026): entfernt nur gewählte Funde der Gruppe a
/// (fremd und unbenutzt) und merkt b-Funde, die behalten werden, als Ausnahme in der Zeichnung. Speichert nie – das macht
/// der Nutzer (Palette) bzw. das Werkzeug ohne Oberfläche unter neuem Namen.
/// </summary>
internal static class Fremdbereiniger
{
    /// <summary>
    /// Führt die Auswahl aus. Vorher wird neu analysiert: nur was jetzt noch a ist, wird entfernt (die Zeichnung kann sich
    /// seit der Vorschau geändert haben). Fehler → nichts wird entfernt. Rückgabe: Berichtszeilen.
    /// </summary>
    public static List<string> Ausfuehren(Database db, IReadOnlyCollection<string> entfernen, IReadOnlyDictionary<string, string> behalten,
        string? referenzOrdner)
    {
        var aus = new List<string>();
        Analyse jetzt;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            jetzt = Fremdanalyse.Lies(db, tr, referenzOrdner);
            tr.Commit();
        }
        var a = jetzt.Funde.Where(f => f.Gruppe == Gruppe.FremdUnbenutzt).ToDictionary(f => f.Kandidat.Pfad, StringComparer.Ordinal);
        var nichtMehr = entfernen.Where(p => !a.ContainsKey(p)).ToList();
        foreach (var p in nichtMehr) aus.Add($"AVTFREMD übersprungen | {p} | ist nicht mehr fremd und unbenutzt");
        var ziele = entfernen.Where(a.ContainsKey).ToList();
        var regApps = new List<ObjectId>();
        int geloescht = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var pfad in ziele)
            {
                var f = a[pfad];
                var id = jetzt.Objekte[pfad];
                if (f.Kandidat.Art == "RegApp") { regApps.Add(id); continue; }
                try
                {
                    var o = tr.GetObject(id, OpenMode.ForRead);
                    Entfernen.Objekt(tr, id, ausWoerterbuch: tr.GetObject(o.OwnerId, OpenMode.ForRead) is DBDictionary);
                    geloescht++;
                    aus.Add($"AVTFREMD entfernt | {f.Kandidat.Art} | {pfad}");
                }
                catch (System.Exception ex)
                {
                    // Lieber nichts als halb: alles verwerfen.
                    tr.Abort();
                    aus.Add($"AVTFREMD FEHLER | {pfad} | {ex.GetBaseException().Message} – nichts entfernt");
                    return aus;
                }
            }
            if (behalten.Count > 0) AusnahmenMerken(db, tr, behalten, aus);
            tr.Commit();
        }
        if (regApps.Count > 0)
        {
            var (frei, nichtFrei) = Entfernen.RegApps(db, regApps);
            geloescht += frei.Count;
            aus.Add($"AVTFREMD RegApps entfernt | {frei.Count} von {regApps.Count}");
            foreach (var id in nichtFrei) aus.Add($"AVTFREMD RegApp bleibt | {id.Handle} | Purge gibt sie nicht frei");
        }
        aus.Add($"AVTFREMD Ergebnis | entfernt {geloescht} | übersprungen {nichtMehr.Count} | Ausnahmen {behalten.Count}");
        return aus;
    }

    /// <summary>b-Funde, die bleiben, mit Grund merken (Wörterbuch AVT_Ausnahmen, Paare Pfad/Grund) – der Hinweis kommt dann nicht mehr.</summary>
    static void AusnahmenMerken(Database db, Transaction tr, IReadOnlyDictionary<string, string> behalten, List<string> aus)
    {
        var alle = Fremdanalyse.Ausnahmen(db, tr);
        foreach (var (pfad, grund) in behalten) alle[pfad] = grund;
        var werte = alle.SelectMany(p => new[] { new TypedValue((int)DxfCode.Text, p.Key), new TypedValue((int)DxfCode.Text, p.Value) }).ToArray();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        if (nod.Contains(Fremdanalyse.AusnahmenSchluessel))
        {
            var x = (Xrecord)tr.GetObject(nod.GetAt(Fremdanalyse.AusnahmenSchluessel), OpenMode.ForWrite);
            x.Data = new ResultBuffer(werte);
        }
        else
        {
            var x = new Xrecord { Data = new ResultBuffer(werte) };
            nod.SetAt(Fremdanalyse.AusnahmenSchluessel, x);
            tr.AddNewlyCreatedDBObject(x, true);
        }
        foreach (var (pfad, grund) in behalten) aus.Add($"AVTFREMD Ausnahme gemerkt | {pfad} | {grund}");
    }

    /// <summary>
    /// Sicherungskopie der gespeicherten Datei daneben: &lt;Name&gt;_vor-Bereinigen_&lt;JJJJMMTT_hhmm&gt;.dwg, Prüfsumme verglichen.
    /// Gibt den Pfad zurück; wirft, wenn Kopie und Quelle nicht gleich sind.
    /// </summary>
    public static string Sicherung(string datei, DateTime jetzt)
    {
        var ziel = Path.Combine(Path.GetDirectoryName(datei)!, $"{Path.GetFileNameWithoutExtension(datei)}_vor-Bereinigen_{jetzt:yyyyMMdd_HHmm}.dwg");
        for (var n = 2; File.Exists(ziel); n++)
            ziel = Path.Combine(Path.GetDirectoryName(datei)!, $"{Path.GetFileNameWithoutExtension(datei)}_vor-Bereinigen_{jetzt:yyyyMMdd_HHmm}_{n}.dwg");
        // Die offene Zeichnung kann gesperrt sein – mit Lesefreigabe kopieren.
        using (var quelle = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var kopie = new FileStream(ziel, FileMode.CreateNew, FileAccess.Write))
            quelle.CopyTo(kopie);
        if (!Pruefsumme(datei).SequenceEqual(Pruefsumme(ziel)))
        {
            File.Delete(ziel);
            throw new IOException($"Sicherungskopie {ziel} stimmt nicht mit {datei} überein");
        }
        return ziel;
    }

    static byte[] Pruefsumme(string datei)
    {
        using var s = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(s);
    }
}
