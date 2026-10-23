using Autodesk.AutoCAD.DatabaseServices;

namespace Avt.Kern;

/// <summary>
/// Das eigentliche Löschen – eine Stelle für das Bereinigen der Vorlage (AVTBEREINIGEN) und das Bereinigen einer offenen
/// Zeichnung (Palette, AVTFREMDBEREINIGEN). Erfahrungen aus ASG/AVT 0.1.0:
/// Wörterbucheinträge erst aus dem besitzenden Wörterbuch austragen, dann löschen; RegApps erst nach dem Commit in
/// eigener Transaktion und nur, soweit Database.Purge sie freigibt (in derselben Transaktion holte AutoCAD sie beim
/// Speichern zurück).
/// </summary>
internal static class Entfernen
{
    /// <summary>Ein Objekt löschen; <paramref name="ausWoerterbuch"/>: vorher aus dem besitzenden Wörterbuch austragen.</summary>
    public static void Objekt(Transaction tr, ObjectId id, bool ausWoerterbuch)
    {
        if (id.IsErased) return;
        var o = tr.GetObject(id, OpenMode.ForWrite);
        if (ausWoerterbuch && tr.GetObject(o.OwnerId, OpenMode.ForWrite) is DBDictionary halter) halter.Remove(id);
        o.Erase();
    }

    /// <summary>
    /// RegApps löschen, soweit Purge sie freigibt (eigene Transaktion, nach dem Commit der übrigen Löschungen).
    /// Liefert die gelöschten und die nicht freigegebenen.
    /// </summary>
    public static (List<ObjectId> Geloescht, List<ObjectId> NichtFrei) RegApps(Database db, IEnumerable<ObjectId> regApps)
    {
        var ids = new ObjectIdCollection(regApps.Where(i => !i.IsNull && !i.IsErased).ToArray());
        var gewollt = ids.Cast<ObjectId>().ToList();
        db.Purge(ids);
        var frei = ids.Cast<ObjectId>().ToHashSet();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var id in frei) tr.GetObject(id, OpenMode.ForWrite).Erase();
            tr.Commit();
        }
        return (frei.ToList(), gewollt.Where(i => !frei.Contains(i)).ToList());
    }

    /// <summary>
    /// Löschen in Runden innerhalb der offenen Transaktion: je Runde gibt AutoCADs Purge frei, was nichts mehr benutzt
    /// (auch über Kopfvariablen und Verweise, die der Graph nicht kennt); was erst frei wird, wenn ein anderes Ziel weg ist
    /// (z. B. ein Block in einem Mehransichtsblock), kommt in der nächsten Runde. Ziele ohne Purge-Prüfung werden direkt gelöscht.
    /// Wirft bei einem Fehler – der Aufrufer verwirft dann die Transaktion. Liefert gelöschte und übrige Ziele.
    /// </summary>
    public static (List<string> Geloescht, List<string> Uebrig, int Runden) InRunden(Database db, Transaction tr,
        IReadOnlyDictionary<string, (ObjectId Id, bool MitPurge)> ziele)
    {
        var offen = ziele.ToDictionary(z => z.Key, z => z.Value, StringComparer.Ordinal);
        var weg = new List<string>();
        var runden = 0;
        while (offen.Count > 0)
        {
            runden++;
            var pruefen = offen.Where(z => z.Value.MitPurge).Select(z => z.Value.Id).ToArray();
            var frei = new ObjectIdCollection(pruefen);
            if (pruefen.Length > 0) db.Purge(frei);
            var freiSet = frei.Cast<ObjectId>().ToHashSet();
            var jetzt = offen.Where(z => !z.Value.MitPurge || freiSet.Contains(z.Value.Id)).ToList();
            if (jetzt.Count == 0) break;
            foreach (var (pfad, (id, _)) in jetzt)
            {
                var o = tr.GetObject(id, OpenMode.ForRead);
                Objekt(tr, id, ausWoerterbuch: tr.GetObject(o.OwnerId, OpenMode.ForRead) is DBDictionary);
                weg.Add(pfad);
                offen.Remove(pfad);
            }
        }
        return (weg, offen.Keys.ToList(), runden);
    }
}
