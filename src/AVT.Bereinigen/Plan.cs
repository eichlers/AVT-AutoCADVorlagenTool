using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Avt.Bereinigen;

/// <summary>
/// Was beim Bereinigen der Vorlage wegkommt (Auftrag AMK/Nutzer 23.10.2026, EHS 0.1). Ausdrückliche Regeln, dazu alles,
/// was nur von Entferntem benutzt wurde. Schutz: Zeigt ein bleibendes Objekt (z. B. ein Anzeigethema) auf etwas, das weg
/// soll, bleibt dieses Ziel stehen und wird gemeldet. Kein pauschales PURGE – unbenutzte Symbolstile bleiben.
/// </summary>
internal sealed class Plan
{
    /// <summary>Vorsilben der Modul-Spuren (Wörterbuch der Zeichnung, RegApps).</summary>
    static readonly string[] Module = ["ATT_", "ADM_", "ANA_", "ASG"];
    /// <summary>Layer, die Module bei Bedarf selbst anlegen (ADM-Marken, ANA-Analyse).</summary>
    static readonly string[] ModulLayer = ["ADM_", "NA_"];

    public Dictionary<ObjectId, string> Wurzeln { get; } = [];
    public List<(ObjectId Ziel, ObjectId Von)> Geschuetzt { get; } = [];
    public List<(ObjectId Von, ObjectId Ziel)> OffeneVerweise { get; } = [];
    /// <summary>Blöcke, die schon vorher niemand benutzte (bleiben, zur Entscheidung gemeldet).</summary>
    public List<ObjectId> SchonUnbenutzt { get; } = [];
    /// <summary>Blöcke, die nur im Modellbereich eingesetzt waren und keine Stempel/Legenden sind (bleiben, gemeldet).</summary>
    public List<ObjectId> NurImModell { get; } = [];
    /// <summary>
    /// Vorsilben fremder Wörterbucheinträge, die mit entfernt werden (Schalter; Vorgabe leer). Groß/klein zählt, damit
    /// eine kurze Vorsilbe keine AutoCAD-Schlüssel (z. B. LINETYPE) trifft.
    /// </summary>
    readonly string[] fremd;
    /// <summary>Anzeigethemen, die auf Auftrag entfernt werden (genaue Namen; Vorgabe leer).</summary>
    readonly string[] themen;
    /// <summary>Auftragsthemen, die es in der Zeichnung nicht gibt (Tippfehler werden so gemeldet).</summary>
    public List<string> NichtGefundeneThemen { get; } = [];

    readonly Bestand b;
    readonly Dictionary<ObjectId, bool> wegMerk = [];

    public static readonly string KlasseSatzDef = RXObject.GetClass(typeof(Aecp.PropertySetDefinition)).Name;
    public static readonly string KlasseSatz = RXObject.GetClass(typeof(Aecp.PropertySet)).Name;
    public static readonly string KlasseMvBlock = RXObject.GetClass(typeof(Autodesk.Aec.DatabaseServices.MultiViewBlockDefinition)).Name;
    static readonly string KlasseBlock = RXObject.GetClass(typeof(BlockTableRecord)).Name;
    static readonly string KlasseLayer = RXObject.GetClass(typeof(LayerTableRecord)).Name;
    static readonly string KlasseRegApp = RXObject.GetClass(typeof(RegAppTableRecord)).Name;

    public static bool IstTabellenstil(string klasse) => klasse.Contains("Schedule", StringComparison.OrdinalIgnoreCase)
        && klasse.Contains("Style", StringComparison.OrdinalIgnoreCase);

    static bool Beginnt(string text, IEnumerable<string> vorsilben) =>
        vorsilben.Any(v => text.StartsWith(v, StringComparison.OrdinalIgnoreCase));

    bool IstRaumblock(Objekt o) => o.Klasse == KlasseBlock && o.Name.StartsWith('*')
        && (o.Name.StartsWith("*Model_Space", StringComparison.OrdinalIgnoreCase) || o.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase));

    Plan(Bestand bestand, string[] fremdeWoerterbuecher, string[] themenWeg) =>
        (b, fremd, themen) = (bestand, fremdeWoerterbuecher, themenWeg);

    public static Plan Erstelle(Bestand b, Database db, string[] fremdeWoerterbuecher, string[] themenWeg)
    {
        var p = new Plan(b, fremdeWoerterbuecher, themenWeg);
        p.Ausdruecklich(db);
        p.Erweitern(SymbolUtilityServices.GetBlockModelSpaceId(db));
        p.Schuetzen();
        // RegApps erst nach dem Schutz: geschützte Objekte können XData tragen (Review). Danach erneut schützen.
        p.RegAppsOhneBenutzer();
        p.Schuetzen();
        return p;
    }

    void Ausdruecklich(Database db)
    {
        var modell = SymbolUtilityServices.GetBlockModelSpaceId(db);
        foreach (var o in b.Alle.Values)
        {
            string? grund =
                o.Besitzer == modell && o.Element ? "Modellbereich"
                : o.Klasse == KlasseSatzDef ? "Eigenschaftssatzdefinition"
                : o.Klasse == KlasseSatz ? "Eigenschaftssatz"
                : IstTabellenstil(o.Klasse) ? "Bauteiltabellenstil"
                : o.Klasse == KlasseBlock && o.Name.StartsWith("eaklLeg", StringComparison.OrdinalIgnoreCase) ? "Legendenblock eaklLeg"
                : o.Klasse == KlasseMvBlock && o.Name.StartsWith("TAG", StringComparison.OrdinalIgnoreCase) ? "Mehransichtsblock TAG"
                : o.Besitzer == db.NamedObjectsDictionaryId && Beginnt(o.Name, Module) ? "Modul-Spur Wörterbuch"
                : o.Klasse == "AecDbDisplayThemeStyle" && themen.Contains(o.Name, StringComparer.OrdinalIgnoreCase) ? "Anzeigethema auf Auftrag"
                // Fremde Einträge in jedem Wörterbuch (NOD und Erweiterungswörterbücher), z. B. LIN_* (Nutzer 23.10.2026).
                // Layouts nie (dafür wäre der LayoutManager zuständig); AutoCAD-Vorsilben weist Befehle.Fremd ab.
                : o.Klasse != "AcDbLayout" && fremd.Any(v => o.Name.StartsWith(v, StringComparison.Ordinal))
                  && b.Alle.TryGetValue(o.Besitzer, out var halter) && halter.Klasse.Contains("Dictionary") ? "Fremd-Spur Wörterbuch"
                : o.Klasse == KlasseRegApp && Beginnt(o.Name, Module) ? "Modul-Spur RegApp"
                // Der aktuelle Layer steht im Kopf (CLAYER), den sieht der Graph nicht – er bleibt.
                : o.Klasse == KlasseLayer && Beginnt(o.Name, ModulLayer) && o.Id != db.Clayer ? "Modul-Layer"
                : null;
            if (grund is not null) Wurzeln[o.Id] = grund;
        }
        var gefunden = Wurzeln.Where(w => w.Value == "Anzeigethema auf Auftrag").Select(w => b.Alle[w.Key].Name).ToList();
        NichtGefundeneThemen.AddRange(themen.Where(t => !gefunden.Contains(t, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>Ist das Objekt nach dem Bereinigen weg (selbst Wurzel oder von einer Wurzel besessen)?</summary>
    public bool Weg(ObjectId id)
    {
        if (wegMerk.TryGetValue(id, out var w)) return w;
        w = Wurzeln.ContainsKey(id) || b.Besitzer(id).Any(Wurzeln.ContainsKey);
        wegMerk[id] = w;
        return w;
    }

    ObjectId Wurzel(ObjectId id) => Wurzeln.ContainsKey(id) ? id : b.Besitzer(id).First(Wurzeln.ContainsKey);

    /// <summary>Wer benutzt das Objekt, ohne es zu besitzen oder ihm zu gehören?</summary>
    List<ObjectId> Benutzer(ObjectId id)
    {
        if (!b.Verwiesen.TryGetValue(id, out var von)) return [];
        var eigene = b.Besitzer(id).ToHashSet();
        return von.Where(v => !eigene.Contains(v) && !b.Besitzer(v).Contains(id)).ToList();
    }

    /// <summary>
    /// Blöcke und Mehransichtsblöcke, die nur von Entferntem benutzt wurden, kommen mit weg – wenn sie anonym sind, Stempel
    /// oder Legenden (TAG*, Legende*) oder nur in entfernten Definitionen steckten. Blöcke, die direkt im Modellbereich
    /// eingesetzt waren (z. B. Höhenkote, Zeichnungsverweis), bleiben als Definition (Entscheidung AMK 23.10.2026).
    /// </summary>
    void Erweitern(ObjectId modell)
    {
        var kandidaten = b.Alle.Values.Where(o => (o.Klasse == KlasseBlock && !IstRaumblock(o)) || o.Klasse == KlasseMvBlock).ToList();
        foreach (var o in kandidaten.Where(o => Benutzer(o.Id).Count == 0 && !Wurzeln.ContainsKey(o.Id))) SchonUnbenutzt.Add(o.Id);
        bool weiter;
        do
        {
            weiter = false;
            foreach (var o in kandidaten.Where(o => !Wurzeln.ContainsKey(o.Id)))
            {
                var benutzer = Benutzer(o.Id);
                if (benutzer.Count == 0 || !benutzer.All(Weg)) continue;
                var stempel = o.Name.StartsWith('*') || Beginnt(o.Name, ["TAG", "Legende", "eaklLeg"]);
                if (!stempel && benutzer.Any(u => b.Alle.TryGetValue(u, out var x) && x.Besitzer == modell))
                {
                    if (!NurImModell.Contains(o.Id)) NurImModell.Add(o.Id);
                    continue;
                }
                Wurzeln[o.Id] = "nur von Entferntem benutzt";
                wegMerk.Clear();
                weiter = true;
            }
        } while (weiter);
    }

    /// <summary>
    /// RegApps, die kein bleibendes Objekt in seinen XData nutzt (wie PURGE, nur für RegApps). AutoCAD-eigene (AC*, AEC*)
    /// bleiben (Entscheidung AMK 23.10.2026).
    /// </summary>
    void RegAppsOhneBenutzer()
    {
        var genutzt = b.Alle.Values.Where(o => !Weg(o.Id)).SelectMany(o => o.XDataApps).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var o in b.Alle.Values.Where(o => o.Klasse == KlasseRegApp && !Wurzeln.ContainsKey(o.Id)))
        {
            if (!Beginnt(o.Name, ["AC", "AEC"]) && !genutzt.Contains(o.Name)) Wurzeln[o.Id] = "RegApp ohne Benutzer";
        }
        wegMerk.Clear();
    }

    /// <summary>
    /// Bleibende Objekte, die auf Entferntes zeigen: Definitionen, Blöcke, Stile, Layer und RegApps bleiben dann stehen.
    /// Verweise auf Objekte im Modellbereich (z. B. aus Gruppen) werden nur gemeldet – AutoCAD löst sie beim Löschen.
    /// Eigenschaftssätze an bleibenden Objekten werden über die Schnittstelle entfernt und zählen nicht als Benutzer.
    /// </summary>
    void Schuetzen()
    {
        bool weiter;
        do
        {
            weiter = false;
            OffeneVerweise.Clear();
            foreach (var o in b.Alle.Values.Where(o => !Weg(o.Id)))
            {
                foreach (var z in o.Zeiger.Where(z => b.Alle.ContainsKey(z) && Weg(z)))
                {
                    var w = Wurzel(z);
                    var grund = Wurzeln[w];
                    if (grund is "Modellbereich" or "Eigenschaftssatz")
                    {
                        OffeneVerweise.Add((o.Id, z));
                        continue;
                    }
                    Wurzeln.Remove(w);
                    wegMerk.Clear();
                    Geschuetzt.Add((w, o.Id));
                    weiter = true;
                }
            }
        } while (weiter);
    }
}
