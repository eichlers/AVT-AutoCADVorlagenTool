using Autodesk.AutoCAD.DatabaseServices;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Avt.Kern.Erzeugen;

/// <summary>
/// Bausteine (Bauplan „Bausteine“): kleine DWGs unter Stammdaten\Bausteine mit Inhalt, den die API nicht erzeugen kann
/// (dynamischer Rahmen-Block, Themen-Komponente mit Darstellung). Bausteine.csv nennt Art, Name, Datei und SHA256 – eine
/// geänderte Datei wird abgelehnt.
/// </summary>
internal static class Bausteine
{
    /// <summary>Satzkopien, die ein Themen-Baustein mitbrachte – nach Themen.Bauen zu entfernen.</summary>
    internal static readonly List<string> Mitgebracht = [];

    public static List<string> Bauen(Database db, string stammdaten, Action<DBObject, string> id)
    {
        Mitgebracht.Clear();
        var aus = new List<string>();
        foreach (var (_, w) in Tabelle.Lies(Path.Combine(stammdaten, "Bausteine.csv"), "ID", "Art", "Name", "Datei", "SHA256").Zeilen)
            aus.Add(w["Art"] switch
            {
                "Block" => Layouts.BlockAusBaustein(db, stammdaten, w, id),
                "Thema" => ThemaAusBaustein(db, stammdaten, w),
                var a => throw new InvalidDataException($"Bausteine.csv: Art „{a}“ – erwartet Block oder Thema"),
            });
        return aus;
    }

    /// <summary>Pfad und geprüfte Prüfsumme eines Bausteins.</summary>
    internal static (string Datei, string Sha) Datei(string stammdaten, IReadOnlyDictionary<string, string> w)
    {
        var datei = Path.Combine(stammdaten, w["Datei"]);
        string ist;
        using (var s = File.OpenRead(datei)) ist = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
        if (!ist.Equals(w["SHA256"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Baustein {w["Datei"]}: Prüfsumme {ist[..16]} … ≠ Bausteine.csv {w["SHA256"][..Math.Min(16, w["SHA256"].Length)]} …");
        return (datei, ist);
    }

    /// <summary>
    /// Thema aus dem Baustein in die Vorlage klonen. Der Baustein bringt den Leitungsstilsatz mit; die Vorlage hat ihn schon
    /// (Saetze.csv) – bei „Ignorieren“ zeigt die Regel auf den vorhandenen. Kommt trotzdem eine zweite Satzdefinition mit, wird
    /// sie abgelehnt (die Themen hingen sonst an der falschen).
    /// </summary>
    static string ThemaAusBaustein(Database db, string stammdaten, IReadOnlyDictionary<string, string> w)
    {
        var (datei, ist) = Datei(stammdaten, w);
        using var bs = new Database(false, true);
        bs.ReadDwgFile(datei, FileOpenMode.OpenForReadAndAllShare, true, "");
        ObjectId quelle;
        using (var tr = bs.TransactionManager.StartTransaction())
        {
            var d = new AecDb.DictionaryDisplayThemeStyle(bs);
            if (!d.Has(w["Name"], tr)) throw new InvalidDataException($"Baustein {w["Datei"]}: Thema {w["Name"]} fehlt");
            quelle = d.GetAt(w["Name"]);
            tr.Commit();
        }
        var vorher = Satznamen(db);
        var ziel = new AecDb.DictionaryDisplayThemeStyle(db).DictionaryId;
        bs.WblockCloneObjects(new ObjectIdCollection { quelle }, ziel, new IdMapping(), DuplicateRecordCloning.Ignore, false);
        // AutoCAD legt den gleichnamigen Satz trotz „Ignorieren“ als „<Name> (2)“ an (Befund 24.10.2026). Erlaubt ist nur diese
        // Kopie des Leitungsstilsatzes; Themen.Bauen hängt alle Regeln auf den Satz der Vorlage um, danach wird sie entfernt.
        var neu = Satznamen(db).Except(vorher).ToList();
        var fremd = neu.Where(n => !n.StartsWith(Erzeugen.Themen.Leitungsstilsatz, StringComparison.Ordinal)).ToList();
        if (fremd.Count > 0) throw new InvalidDataException($"Baustein {w["Datei"]} brachte Satzdefinitionen mit: {string.Join(", ", fremd)}");
        Mitgebracht.AddRange(neu);
        return $"AVTERZEUGEN Baustein | Thema {w["Name"]} | aus {w["Datei"]} (SHA256 {ist[..16]} …)";
    }

    /// <summary>Mitgebrachte Satzkopien entfernen (nach dem Umhängen der Themen).</summary>
    public static List<string> Aufraeumen(Database db, Transaction tr)
    {
        var dict = new Aecp.DictionaryPropertySetDefinitions(db);
        var aus = new List<string>();
        foreach (var n in Mitgebracht)
        {
            tr.GetObject(dict.GetAt(n), OpenMode.ForWrite).Erase();
            aus.Add($"AVTERZEUGEN Baustein | mitgebrachte Satzkopie {n} entfernt");
        }
        Mitgebracht.Clear();
        return aus;
    }

    static HashSet<string> Satznamen(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var namen = new Aecp.DictionaryPropertySetDefinitions(db).Records.Cast<ObjectId>().Where(i => !i.IsErased)
            .Select(i => ((AecDb.DictionaryRecord)tr.GetObject(i, OpenMode.ForRead)).Name).ToHashSet(StringComparer.Ordinal);
        tr.Commit();
        return namen;
    }
}
