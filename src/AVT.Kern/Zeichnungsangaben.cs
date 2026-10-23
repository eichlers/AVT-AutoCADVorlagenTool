using Autodesk.AutoCAD.DatabaseServices;

namespace Avt.Kern;

/// <summary>Angaben zur Zeichnung: DWGPROPS, Pflichtfelder, Fassungsstempel – für Inventur (AVTKANON), Sonde und Palette.</summary>
internal static class Zeichnungsangaben
{
    /// <summary>Pflichtfelder (benutzerdefinierte DWGPROPS), die fehlen oder leer sind – eine Stelle für Inventur und Sonde.</summary>
    internal static List<string> FehlendePflichtfelder(Database db)
    {
        var gefuellt = new HashSet<string>(StringComparer.Ordinal);
        var e = db.SummaryInfo.CustomProperties;
        while (e.MoveNext())
        {
            if (!string.IsNullOrWhiteSpace(Convert.ToString(e.Value))) gefuellt.Add(Convert.ToString(e.Key) ?? "");
        }
        return Standard.PflichtFelder.Where(f => !gefuellt.Contains(f)).ToList();
    }

    /// <summary>DWGPROPS: Standardfelder und benutzerdefinierte Felder als „Feld = Wert“ (benutzerdefinierte mit Präfix „*“).</summary>
    internal static IEnumerable<string> DwgProps(Database db)
    {
        var s = db.SummaryInfo;
        yield return $"Titel = {s.Title}";
        yield return $"Thema = {s.Subject}";
        yield return $"Autor = {s.Author}";
        yield return $"Schlüsselwörter = {s.Keywords}";
        yield return "Kommentare = " + (s.Comments ?? "").Replace(((char)13).ToString(), "").Replace(((char)10).ToString(), " / ");
        yield return $"Revisionsnummer = {s.RevisionNumber}";
        yield return $"Hyperlink-Basis = {s.HyperlinkBase}";
        var e = s.CustomProperties;
        while (e.MoveNext()) yield return $"*{e.Key} = {e.Value}";
    }

    /// <summary>Fassungsstempel aus dem NOD-Eintrag <see cref="Standard.Stempel"/> (Xrecord, Paare Schlüssel/Wert als Texte).</summary>
    internal static IEnumerable<string> FassungLesen(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Standard.Stempel)) return ["fehlt"];
        if (tr.GetObject(nod.GetAt(Standard.Stempel), OpenMode.ForRead) is not Xrecord x || x.Data is null) return ["unlesbar"];
        var werte = x.Data.AsArray().Select(t => Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList();
        return werte.Chunk(2).Select(p => p.Length == 2 ? $"{p[0]} = {p[1]}" : p[0]);
    }
}
