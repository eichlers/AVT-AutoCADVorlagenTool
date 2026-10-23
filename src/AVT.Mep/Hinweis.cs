// Leiser Hinweis beim Öffnen/Aktivieren einer Zeichnung (Auftrag AMK/Nutzer 23.10.2026): AVT prüft im Leerlauf, ob die
// Zeichnung Inhalte hat, die nicht zur Fassung gehören, und meldet einmal je Zeichnung eine Zeile in der Befehlszeile und in
// der Palette („3 Fremdinhalte, 1 benutzt – Bereinigen?“). Kein Dialog, nichts wird geändert. Läuft ein Befehl, wird gewartet.
// Abschaltbar im Zahnrad.

using Avt.Kern.Fremd;
using Avt.Mep.Palette;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Avt.Mep;

internal static class Hinweis
{
    static readonly Queue<Document> warteschlange = new();
    static readonly Dictionary<IntPtr, string?> ergebnis = [];
    static bool eingerichtet;

    public static void Einrichten()
    {
        if (eingerichtet) return;
        eingerichtet = true;
        var dm = AcApp.DocumentManager;
        dm.DocumentActivated += (_, e) => Vormerken(e.Document);
        dm.DocumentCreated += (_, e) => Vormerken(e.Document);
        dm.DocumentToBeDestroyed += (_, e) => { try { ergebnis.Remove(e.Document.Database.UnmanagedObject); } catch (System.Exception) { /* nur Merkliste */ } };
        AcApp.Idle += ImLeerlauf;
        if (dm.MdiActiveDocument is { } doc) Vormerken(doc);
    }

    /// <summary>Gemerkte Kurzfassung der Zeichnung (null = nichts Fremdes oder noch nicht geprüft).</summary>
    public static string? Gemerkt(Document doc) => ergebnis.GetValueOrDefault(doc.Database.UnmanagedObject);

    /// <summary>Nach dem Bereinigen den neuen Stand merken (kein erneuter Hinweis für dieselbe Zeichnung).</summary>
    public static void Merken(Document doc, string? kurz) => ergebnis[doc.Database.UnmanagedObject] = kurz;

    static void Vormerken(Document? doc)
    {
        try
        {
            if (doc == null || ergebnis.ContainsKey(doc.Database.UnmanagedObject) || warteschlange.Contains(doc)) return;
            warteschlange.Enqueue(doc);
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Hinweis vormerken: {ex.Message}"); }
    }

    // Je Leerlauf höchstens eine Zeichnung; nur die aktive, ruhige (kein laufender Befehl).
    static void ImLeerlauf(object? sender, EventArgs e)
    {
        if (warteschlange.Count == 0 || !AcApp.IsQuiescent) return;
        var doc = warteschlange.Peek();
        try
        {
            if (doc.IsDisposed) { warteschlange.Dequeue(); return; }
            if (doc != AcApp.DocumentManager.MdiActiveDocument || doc.CommandInProgress.Length > 0) return;
            warteschlange.Dequeue();
            if (!Einstellungen.Lies().HinweisBeimOeffnen) return;
            Analyse a;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                a = Fremdanalyse.Lies(doc.Database, tr, null);
                tr.Commit();
            }
            var kurz = Einteilung.Kurzfassung(a.Funde);
            ergebnis[doc.Database.UnmanagedObject] = kurz;
            Protokoll.Merke("Hinweis", $"{Path.GetFileName(doc.Name)}: {kurz ?? "keine Fremdinhalte"} ({a.Dauer.TotalMilliseconds:F0} ms, {a.Objektzahl} Objekte)");
            if (kurz == null) return;
            doc.Editor.WriteMessage($"\nAVT: {kurz} (nicht in der Fassung) – Palette AVT → „Bereinigen …“ zeigt sie.\n");
            PalettenBefehl.Ansicht?.HinweisSetzen(doc, kurz);
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Hinweis: {ex.GetBaseException().Message}"); }
    }
}
