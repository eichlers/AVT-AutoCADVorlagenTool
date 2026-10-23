// Leiser Hinweis beim Öffnen/Aktivieren einer Zeichnung (Auftrag AMK/Nutzer 23.10.2026): AVT prüft im Leerlauf, ob die
// Zeichnung Einträge hat, die nicht zur Fassung gehören, und meldet einmal je Zeichnung eine Zeile in der Befehlszeile und
// in der Palette. Kein Dialog, nichts wird geändert, nicht während eines Befehls. Abschaltbar im Zahnrad.
// Nur eine schnelle Vorprüfung über Namen (Review 0.3.0: die volle Analyse kann bei großen Zeichnungen spürbar dauern);
// was davon benutzt ist, zeigt „Bereinigen …“ in der Palette.
// Außerdem: Dateistand beim Öffnen merken – vor der Sicherungskopie prüft die Palette, ob die Datei seitdem von außen
// geändert wurde (z. B. Synchronisierung).

using Avt.Kern.Fremd;
using Avt.Mep.Palette;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Avt.Mep;

internal static class Hinweis
{
    static readonly List<Document> offen = [];
    static readonly Dictionary<IntPtr, string?> ergebnis = [];
    static readonly Dictionary<string, (DateTime Zeit, long Laenge)> dateistand = new(StringComparer.OrdinalIgnoreCase);
    static bool eingerichtet;

    public static void Einrichten()
    {
        if (eingerichtet) return;
        eingerichtet = true;
        var dm = AcApp.DocumentManager;
        dm.DocumentActivated += (_, e) => Vormerken(e.Document);
        dm.DocumentCreated += (_, e) => { StandMerken(e.Document); Vormerken(e.Document); };
        dm.DocumentToBeDestroyed += (_, e) =>
        {
            try { ergebnis.Remove(e.Document.Database.UnmanagedObject); offen.Remove(e.Document); dateistand.Remove(e.Document.Name); }
            catch (System.Exception) { /* nur Merklisten */ }
        };
        AcApp.Idle += ImLeerlauf;
        foreach (Document doc in dm) { StandMerken(doc); Vormerken(doc); }
    }

    /// <summary>Gemerkte Kurzfassung der Zeichnung (null = nichts Fremdes oder noch nicht geprüft).</summary>
    public static string? Gemerkt(Document doc) => ergebnis.GetValueOrDefault(doc.Database.UnmanagedObject);

    /// <summary>Nach Prüfen/Bereinigen den Stand merken (kein erneuter Hinweis für dieselbe Zeichnung).</summary>
    public static void Merken(Document doc, string? kurz) => ergebnis[doc.Database.UnmanagedObject] = kurz;

    /// <summary>
    /// Ist die Datei noch so wie beim Öffnen bzw. letzten Speichern in dieser Sitzung? null = unbekannt.
    /// Speichert der Nutzer, ändert sich der Stand – darum nach dem Speichern neu merken (BeginSave/SaveComplete).
    /// </summary>
    public static bool? DateiWieGeoeffnet(Document doc)
    {
        if (!dateistand.TryGetValue(doc.Name, out var alt) || !File.Exists(doc.Name)) return null;
        var i = new FileInfo(doc.Name);
        return i.LastWriteTimeUtc == alt.Zeit && i.Length == alt.Laenge;
    }

    static void StandMerken(Document? doc)
    {
        try
        {
            if (doc == null || !File.Exists(doc.Name)) return;
            var i = new FileInfo(doc.Name);
            dateistand[doc.Name] = (i.LastWriteTimeUtc, i.Length);
            // Nach dem Speichern durch den Nutzer gilt der neue Stand.
            doc.Database.SaveComplete -= NachSpeichern;
            doc.Database.SaveComplete += NachSpeichern;
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Dateistand merken: {ex.Message}"); }
    }

    static void NachSpeichern(object? sender, Autodesk.AutoCAD.DatabaseServices.DatabaseIOEventArgs e)
    {
        try
        {
            foreach (Document d in AcApp.DocumentManager)
                if (d.Database == sender && File.Exists(e.FileName))
                {
                    var i = new FileInfo(e.FileName);
                    dateistand[e.FileName] = (i.LastWriteTimeUtc, i.Length);
                }
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Dateistand nach dem Speichern: {ex.Message}"); }
    }

    static void Vormerken(Document? doc)
    {
        try
        {
            if (doc == null || ergebnis.ContainsKey(doc.Database.UnmanagedObject) || offen.Contains(doc)) return;
            offen.Add(doc);
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Hinweis vormerken: {ex.Message}"); }
    }

    // Je Leerlauf höchstens eine Zeichnung: die aktive, wenn sie wartet und kein Befehl läuft (Review 0.3.0: nicht am Kopf
    // einer Warteschlange hängen bleiben).
    static void ImLeerlauf(object? sender, EventArgs e)
    {
        if (offen.Count == 0 || !AcApp.IsQuiescent) return;
        try
        {
            offen.RemoveAll(d => d.IsDisposed);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null || !offen.Contains(doc) || doc.CommandInProgress.Length > 0) return;
            offen.Remove(doc);
            if (!Einstellungen.Lies().HinweisBeimOeffnen) return;
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            int anzahl;
            string referenz;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                (anzahl, referenz) = Fremdanalyse.Vorpruefung(doc.Database, tr, null);
                tr.Commit();
            }
            var kurz = anzahl == 0 ? null : $"{anzahl} Einträge nicht in der Fassung";
            ergebnis[doc.Database.UnmanagedObject] = kurz;
            Protokoll.Merke("Hinweis", $"{Path.GetFileName(doc.Name)}: {kurz ?? "alles in der Fassung"} (Referenz {referenz}, {uhr.ElapsedMilliseconds} ms)");
            if (kurz == null) return;
            doc.Editor.WriteMessage($"\nAVT: {kurz} ({referenz.Replace('_', ' ')}) – Palette AVT → „Bereinigen …“ zeigt, was davon fremd ist.\n");
            PalettenBefehl.Ansicht?.HinweisSetzen(doc, kurz);
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Hinweis: {ex.GetBaseException().Message}"); }
    }
}
