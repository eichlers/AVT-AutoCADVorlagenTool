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
}
