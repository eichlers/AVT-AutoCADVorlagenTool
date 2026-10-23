using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace Avt.Kern;

/// <summary>Eine Zeile der kanonischen Inventur: fester Pfad, Klasse, Prüfsumme des Inhalts (und auf Wunsch der Inhalt).</summary>
internal sealed record KanonZeile(string Pfad, string Klasse, string Pruefsumme, string? Inhalt, string Zustand);

/// <summary>
/// Kanonische Inventur (AVT 0.2.0): jedes Objekt der Zeichnung mit einem Pfad aus Namen statt Handles
/// (z. B. „AcDbLayerTable/Z_Schema“, „NOD/AEC_DISPLAYTHEME_STYLES/DN Rohrleitung“) und einer Prüfsumme über alles, was das
/// Objekt beim DwgOut schreibt. Verweise auf andere Objekte werden als deren Pfad geschrieben, Handles nie. Benannte Objekte
/// haben so feste Pfade; namenlose und anonyme (Klasse#n, *U#n) werden nach Handle nummeriert – gleich gebaute Zeichnungen
/// vergleichen sich darum sicher, anders gebaute können namenlose Objekte verschoben zeigen (Grenze 0.2.0, Review 23.10.2026;
/// Abhilfe ab 0.4.0: Nummer nach Inhalt). Gesamtprüfsumme = SHA-256 über die sortierten Zeilen.
/// </summary>
internal static class Kanon
{
    public static List<KanonZeile> Lies(Database db, Transaction tr, Bestand b, IReadOnlyList<string> detailPraefixe)
    {
        var pfade = Pfade(db, b);
        var zeilen = new List<KanonZeile>();
        foreach (var (id, o) in b.Alle)
        {
            var fang = new Kanonfang(db, pfade);
            var zustand = "ok";
            if (Instabil.Contains(o.Klasse)) { fang.Tokens.Add("INSTABIL"); zustand = "instabil"; }
            else
            {
                try { tr.GetObject(id, OpenMode.ForRead, false).DwgOut(fang); }
                catch (System.Exception ex) { fang.Tokens.Add($"FEHLER {ex.GetBaseException().GetType().Name}"); zustand = "fehler"; }
            }
            var inhalt = string.Join("|", Normiere(o.Klasse, fang.Tokens));
            var pfad = pfade[id];
            zeilen.Add(new KanonZeile(pfad, o.Klasse, Sha(inhalt)[..16],
                detailPraefixe.Any(d => pfad.StartsWith(d, StringComparison.Ordinal)) ? inhalt : null, zustand));
        }
        zeilen.Sort((x, y) => string.CompareOrdinal(x.Pfad, y.Pfad));
        return zeilen;
    }

    /// <summary>
    /// Klassen, deren Inhalt nicht stabil ist und darum nur mit Pfad zählt (Befund 23.10.2026, gleiche Datei zweimal gelesen):
    /// räumliche Indizes (abgeleitet, Zeitstempel) und die DACH-Sanierungsstile (wechselnde Werte bei jedem Lesen).
    /// </summary>
    public static readonly HashSet<string> Instabil = ["AcDbSpatialIndex", "AcDbLayerIndex", "AecDACHDbRefurbishStyle"];

    /// <summary>XData-Anwendungen, deren Binärwerte je Bau neu entstehen (GUID, Objektversion) – nur ihr Vorkommen zählt.</summary>
    static readonly string[] FluechtigeXData = ["/AcDbDynamicBlockGUID", "/AEC_OBJVER_INFO"];

    /// <summary>
    /// Mengen ohne feste Reihenfolge sortieren und flüchtige Werte ausblenden: Verweise einer Blockdefinition auf ihre
    /// Einfügungen, Rechenverfahren einer Zonendefinition, Binärwerte flüchtiger XData.
    /// </summary>
    static List<string> Normiere(string klasse, List<string> t)
    {
        var n = new List<string>(t);
        void SortiereStellen(Func<string, bool> teil)
        {
            var stellen = Enumerable.Range(0, n.Count).Where(i => teil(n[i])).ToList();
            var werte = stellen.Select(i => n[i]).OrderBy(v => v, StringComparer.Ordinal).ToList();
            for (var i = 0; i < stellen.Count; i++) n[stellen[i]] = werte[i];
        }
        if (klasse == "AcDbBlockTableRecord") SortiereStellen(v => v.StartsWith("SP", StringComparison.Ordinal) && (v.Contains("/AcDbBlockReference#") || v.Contains("/AecDbMvBlockRef#")));
        SortiereStellen(v => v.StartsWith("SAecSpaceCalculation", StringComparison.Ordinal));
        // Nur Binärwerte bis zum nächsten Wert, der kein Binärwert ist, gehören zur flüchtigen XData (Review 23.10.2026).
        var fluechtig = false;
        for (var i = 0; i < n.Count; i++)
        {
            if (n[i].StartsWith("HP", StringComparison.Ordinal)) fluechtig = FluechtigeXData.Any(a => n[i].EndsWith(a, StringComparison.Ordinal));
            else if (fluechtig && n[i].StartsWith("BY", StringComparison.Ordinal)) n[i] = "BY*";
            else if (!n[i].StartsWith("I", StringComparison.Ordinal)) fluechtig = false;
        }
        return n;
    }

    public static string Gesamt(IEnumerable<KanonZeile> zeilen) =>
        Sha(string.Join("\n", zeilen.Select(z => $"{z.Pfad}\t{z.Klasse}\t{z.Pruefsumme}")));

    /// <summary>Trenn- und Steuerzeichen maskieren, damit Werte, Pfade und Kopfzeilen das Zeilenformat nicht brechen.</summary>
    public static string Esc(string text) => text.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    public static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// Pfad je Objekt: Pfad des Besitzers + Name. Ohne Namen: Klasse#n (n = Rang unter den namenlosen Geschwistern gleicher
    /// Klasse nach Handle). Anonyme Blöcke (*U12, *D3 …) heißen *U#n, damit ihr wechselnder Name nicht zählt.
    /// Gleiche Pfade bekommen ~2, ~3 … angehängt.
    /// </summary>
    internal static Dictionary<ObjectId, string> Pfade(Database db, Bestand b)
    {
        var segment = new Dictionary<ObjectId, string>();
        foreach (var gruppe in b.Alle.Values.GroupBy(o => (o.Besitzer, o.Klasse, Anonym: AnonymeArt(o))))
        {
            var namenlos = 0;
            foreach (var o in gruppe.OrderBy(o => o.Id.Handle.Value))
            {
                segment[o.Id] = o.Id == db.NamedObjectsDictionaryId ? "NOD"
                    : gruppe.Key.Anonym is { } art ? $"*{art}#{namenlos++}"
                    : o.Name != "" ? Esc(o.Name).Replace('/', '∕')
                    : $"{o.Klasse}#{namenlos++}";
            }
        }
        var pfade = new Dictionary<ObjectId, string>();
        string Pfad(ObjectId id, int tiefe)
        {
            if (pfade.TryGetValue(id, out var p)) return p;
            if (!b.Alle.TryGetValue(id, out var o)) return "?";
            var vorn = tiefe < 64 && b.Alle.ContainsKey(o.Besitzer) && o.Besitzer != id ? Pfad(o.Besitzer, tiefe + 1) + "/" : "";
            return pfade[id] = vorn + segment[id];
        }
        foreach (var id in b.Alle.Keys.OrderBy(i => i.Handle.Value)) Pfad(id, 0);
        // Doppelte Pfade eindeutig machen (Reihenfolge nach Handle).
        foreach (var g in pfade.GroupBy(p => p.Value).Where(g => g.Count() > 1).ToList())
        {
            var n = 1;
            foreach (var (id, _) in g.OrderBy(p => p.Key.Handle.Value).Skip(1)) pfade[id] = $"{g.Key}~{++n}";
        }
        // Eindeutigkeit sicherstellen (ein „X~2“ könnte mit einem echten Namen zusammenfallen) – sonst lieber abbrechen.
        if (pfade.Values.Distinct(StringComparer.Ordinal).Count() != pfade.Count)
            throw new InvalidOperationException("Inventur: Pfade nicht eindeutig – Inventur abgebrochen");
        return pfade;
    }

    /// <summary>
    /// Buchstabe eines anonymen Namens oder null: anonyme Blöcke (*U12, *D3 …) und anonyme AEC-Einträge in Wörterbüchern
    /// (*A113 in AEC_DISPLAY_PROPS_DEFAULTS) – ihre Nummern vergibt AutoCAD je Datei neu.
    /// </summary>
    static string? AnonymeArt(Objekt o)
    {
        var n = o.Name;
        if (n.Length < 2 || n[0] != '*') return null;
        if (o.Klasse == "AcDbBlockTableRecord")
            return n.StartsWith("*Model_Space", StringComparison.OrdinalIgnoreCase) || n.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase)
                ? null : n[1].ToString();
        return char.IsLetter(n[1]) && n.Length > 2 && n[2..].All(char.IsDigit) ? n[1].ToString() : null;
    }
}

/// <summary>
/// Schreibt beim DwgOut alle Werte als Text: Verweise als Pfad (nie Handle), Zahlen mit 12 gültigen Stellen,
/// Binärdaten als Prüfsumme. Position bleibt 0 (Falle aus ASG: sonst liefern Anzeigethemen keine Regeln).
/// </summary>
internal sealed class Kanonfang(Database db, IReadOnlyDictionary<ObjectId, string> pfade) : DwgFiler
{
    public List<string> Tokens { get; } = [];
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    void W(string art, string wert) => Tokens.Add(art + Kanon.Esc(wert));
    void Id(string art, ObjectId id) => W(art, id.IsNull ? "0" : pfade.TryGetValue(id, out var p) ? p : "?" + id.ObjectClass?.Name);
    static string D(double d) => d.ToString("G12", Inv);
    static string Bin(byte[]? b) => b is null ? "-" : Kanon.Sha(Convert.ToBase64String(b))[..16];

    public override ErrorStatus FilerStatus { get => ErrorStatus.OK; set { } }
    public override FilerType FilerType => FilerType.CopyFiler;
    public override long Position => 0;
    public override void ResetFilerStatus() { }
    public override void Seek(long offset, int method) { }

    public override void WriteHardOwnershipId(ObjectId value) => Id("HO", value);
    public override void WriteSoftOwnershipId(ObjectId value) => Id("SO", value);
    public override void WriteHardPointerId(ObjectId value) => Id("HP", value);
    public override void WriteSoftPointerId(ObjectId value) => Id("SP", value);
    public override void WriteString(string value) => W("S", value ?? "");
    public override void WriteBoolean(bool value) => W("B", value ? "1" : "0");
    public override void WriteByte(byte value) => W("Y", value.ToString(Inv));
    public override void WriteBytes(byte[] value) => W("BY", Bin(value));
    public override void WriteBinaryChunk(byte[] chunk) => W("BC", Bin(chunk));
    public override void WriteDouble(double value) => W("D", D(value));
    public override void WriteInt16(short value) => W("I", value.ToString(Inv));
    public override void WriteInt32(int value) => W("I", value.ToString(Inv));
    public override void WriteInt64(long value) => W("I", value.ToString(Inv));
    public override void WriteUInt16(ushort value) => W("I", value.ToString(Inv));
    public override void WriteUInt32(uint value) => W("I", value.ToString(Inv));
    public override void WriteUInt64(ulong value) => W("I", value.ToString(Inv));
    public override void WritePoint2d(Point2d v) => W("P", $"{D(v.X)},{D(v.Y)}");
    public override void WritePoint3d(Point3d v) => W("P", $"{D(v.X)},{D(v.Y)},{D(v.Z)}");
    public override void WriteVector2d(Vector2d v) => W("V", $"{D(v.X)},{D(v.Y)}");
    public override void WriteVector3d(Vector3d v) => W("V", $"{D(v.X)},{D(v.Y)},{D(v.Z)}");
    public override void WriteScale3d(Scale3d v) => W("SC", $"{D(v.X)},{D(v.Y)},{D(v.Z)}");
    // Handles zählen mit dem Pfad ihres Ziels (z. B. XData 1005); unbekannte nur mit ihrem Vorkommen. Adressen nie.
    public override void WriteHandle(Handle handle) =>
        W("H", db.TryGetObjectId(handle, out var id) && pfade.TryGetValue(id, out var p) ? p : "?");
    public override void WriteAddress(IntPtr value) => W("A", "");

    public override IntPtr ReadAddress() => throw new NotSupportedException();
    public override byte[] ReadBinaryChunk() => throw new NotSupportedException();
    public override bool ReadBoolean() => throw new NotSupportedException();
    public override byte ReadByte() => throw new NotSupportedException();
    public override void ReadBytes(byte[] value) => throw new NotSupportedException();
    public override double ReadDouble() => throw new NotSupportedException();
    public override Handle ReadHandle() => throw new NotSupportedException();
    public override ObjectId ReadHardOwnershipId() => throw new NotSupportedException();
    public override ObjectId ReadHardPointerId() => throw new NotSupportedException();
    public override short ReadInt16() => throw new NotSupportedException();
    public override int ReadInt32() => throw new NotSupportedException();
    public override long ReadInt64() => throw new NotSupportedException();
    public override Point2d ReadPoint2d() => throw new NotSupportedException();
    public override Point3d ReadPoint3d() => throw new NotSupportedException();
    public override Scale3d ReadScale3d() => throw new NotSupportedException();
    public override ObjectId ReadSoftOwnershipId() => throw new NotSupportedException();
    public override ObjectId ReadSoftPointerId() => throw new NotSupportedException();
    public override string ReadString() => throw new NotSupportedException();
    public override ushort ReadUInt16() => throw new NotSupportedException();
    public override uint ReadUInt32() => throw new NotSupportedException();
    public override ulong ReadUInt64() => throw new NotSupportedException();
    public override Vector2d ReadVector2d() => throw new NotSupportedException();
    public override Vector3d ReadVector3d() => throw new NotSupportedException();
}
