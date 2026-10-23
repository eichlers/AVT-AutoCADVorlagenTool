using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;

namespace Avt.Kern.Fremd;

/// <summary>
/// Bereinigen einer Zeichnung nach der Vorschau (Auftrag AMK 23.10.2026): entfernt nur gewählte Funde der Gruppe a
/// (fremd und unbenutzt) und merkt b-Funde, die behalten werden, als Ausnahme in der Zeichnung. Speichert nie – das macht
/// der Nutzer (Palette) bzw. das Werkzeug ohne Oberfläche unter neuem Namen.
/// Sicherungen (Review 0.3.0): neu analysieren; bei unvollständigem Verweisgraph nichts entfernen; Pfad UND Handle müssen
/// zur Vorschau passen; was ein bleibendes Objekt noch benutzt (auch über eine Teilauswahl), bleibt; AutoCAD-Purge als
/// zweite Prüfung für Tabelleneinträge und Stile; ein Fehler → nichts entfernt.
/// </summary>
internal static class Fremdbereiniger
{
    /// <summary>Arten, bei denen AutoCADs Purge zusätzlich bestätigen muss, dass nichts sie benutzt (auch Kopfvariablen).</summary>
    static readonly HashSet<string> MitPurge = ["Block", "Layer", "Linientyp", "Textstil", "Bemaßungsstil", "Mehransichtsblock", "Stil/Definition", "Satzdefinition"];

    /// <summary>
    /// Führt die Auswahl aus. <paramref name="entfernen"/>: Pfad → Handle (aus der Vorschau). Rückgabe: Berichtszeilen;
    /// eine Zeile „AVTFREMD FEHLER“ heißt: nichts wurde entfernt.
    /// </summary>
    public static List<string> Ausfuehren(Database db, IReadOnlyDictionary<string, long> entfernen, IReadOnlyDictionary<string, string> behalten,
        string? referenzOrdner)
    {
        var aus = new List<string>();
        Analyse jetzt;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            jetzt = Fremdanalyse.Lies(db, tr, referenzOrdner);
            tr.Commit();
        }
        if (jetzt.Luecke > 0)
        {
            aus.Add($"AVTFREMD FEHLER | Verweisgraph unvollständig ({jetzt.Luecke} Objekte nicht lesbar) – nichts entfernt");
            return aus;
        }
        var a = jetzt.Funde.Where(f => f.Gruppe == Gruppe.FremdUnbenutzt).ToDictionary(f => f.Kandidat.Pfad, StringComparer.Ordinal);
        var ziele = new Dictionary<string, Einordnung>(StringComparer.Ordinal);
        foreach (var (pfad, handle) in entfernen)
        {
            if (!a.TryGetValue(pfad, out var f)) aus.Add($"AVTFREMD übersprungen | {pfad} | ist nicht mehr fremd und unbenutzt");
            else if (jetzt.Objekte[pfad].Handle.Value != handle) aus.Add($"AVTFREMD übersprungen | {pfad} | anderes Objekt als in der Vorschau");
            else ziele[pfad] = f;
        }
        // Fixpunkt: ein Ziel bleibt nur, wenn alle seine Benutzer selbst wegkommen (Teilauswahl darf keine Verweise kappen).
        bool weiter;
        do
        {
            weiter = false;
            foreach (var (pfad, f) in ziele.ToList())
            {
                var bleibend = f.Kandidat.Benutzer.FirstOrDefault(u => !Weg(u, pfad, ziele));
                if (bleibend is null) continue;
                ziele.Remove(pfad);
                aus.Add($"AVTFREMD übersprungen | {pfad} | benutzt von {bleibend}, das bleibt");
                weiter = true;
            }
        } while (weiter);

        // Löschen in Runden, alles in EINER Transaktion (ein Fehler verwirft alles): je Runde gibt AutoCADs Purge frei, was
        // nichts mehr benutzt – auch über Kopfvariablen und Verweise, die der Graph nicht kennt. Was erst frei wird, wenn
        // ein anderes Ziel weg ist (z. B. ein Block in einem Mehransichtsblock), kommt in der nächsten Runde.
        var regApps = ziele.Where(z => z.Value.Kandidat.Art == "RegApp").Select(z => jetzt.Objekte[z.Key]).ToList();
        var offen = ziele.Where(z => z.Value.Kandidat.Art != "RegApp").ToDictionary(z => z.Key, z => z.Value, StringComparer.Ordinal);
        var entfernt = new List<string>();
        var runden = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                while (offen.Count > 0)
                {
                    runden++;
                    var pruefen = offen.Where(z => MitPurge.Contains(z.Value.Kandidat.Art)).Select(z => jetzt.Objekte[z.Key]).ToArray();
                    var frei = new ObjectIdCollection(pruefen);
                    if (pruefen.Length > 0) db.Purge(frei);
                    var freiSet = frei.Cast<ObjectId>().ToHashSet();
                    var jetztWeg = offen.Where(z => !MitPurge.Contains(z.Value.Kandidat.Art) || freiSet.Contains(jetzt.Objekte[z.Key])).ToList();
                    if (jetztWeg.Count == 0) break;
                    foreach (var (pfad, f) in jetztWeg)
                    {
                        var id = jetzt.Objekte[pfad];
                        var o = tr.GetObject(id, OpenMode.ForRead);
                        Entfernen.Objekt(tr, id, ausWoerterbuch: tr.GetObject(o.OwnerId, OpenMode.ForRead) is DBDictionary);
                        entfernt.Add($"AVTFREMD entfernt | {f.Kandidat.Art} | {pfad}");
                        offen.Remove(pfad);
                    }
                }
                if (behalten.Count > 0) AusnahmenMerken(db, tr, behalten, entfernt);
            }
            catch (System.Exception ex)
            {
                // Lieber nichts als halb: alles verwerfen, „entfernt“-Zeilen gelten nicht.
                tr.Abort();
                aus.Add($"AVTFREMD FEHLER | {ex.GetBaseException().Message} – nichts entfernt");
                return aus;
            }
            tr.Commit();
        }
        foreach (var pfad in offen.Keys) aus.Add($"AVTFREMD übersprungen | {pfad} | AutoCAD hält es für benutzt (Purge gibt es nicht frei)");
        aus.Add($"AVTFREMD Runden | {runden}");
        aus.AddRange(entfernt);
        var geloescht = entfernt.Count(z => z.StartsWith("AVTFREMD entfernt", StringComparison.Ordinal));
        if (regApps.Count > 0)
        {
            try
            {
                var (frei, nichtFrei) = Entfernen.RegApps(db, regApps);
                geloescht += frei.Count;
                aus.Add($"AVTFREMD RegApps entfernt | {frei.Count} von {regApps.Count}");
                foreach (var id in nichtFrei) aus.Add($"AVTFREMD RegApp bleibt | {id.Handle} | Purge gibt sie nicht frei");
            }
            catch (System.Exception ex) { aus.Add($"AVTFREMD RegApps nicht entfernt | {ex.GetBaseException().Message} (das Übrige ist entfernt)"); }
        }
        aus.Add($"AVTFREMD Ergebnis | entfernt {geloescht} | übersprungen {aus.Count(z => z.StartsWith("AVTFREMD übersprungen", StringComparison.Ordinal))} | Ausnahmen {behalten.Count}");
        return aus;
    }

    /// <summary>Kommt der Benutzer mit weg? (er ist selbst Ziel, liegt in einem Ziel oder im eigenen Fund)</summary>
    static bool Weg(string benutzer, string eigenerPfad, Dictionary<string, Einordnung> ziele)
    {
        if (benutzer.StartsWith(eigenerPfad + "/", StringComparison.Ordinal) || ziele.ContainsKey(benutzer)) return true;
        for (var i = benutzer.LastIndexOf('/'); i > 0; i = benutzer.LastIndexOf('/', i - 1))
            if (ziele.ContainsKey(benutzer[..i])) return true;
        return false;
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
