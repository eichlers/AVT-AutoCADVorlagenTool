using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Aec.Building.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;

[assembly: CommandClass(typeof(Avt.Werkzeug.Befehle))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTINVENTUR (nur lesen: Zahlen je Kategorie und der Entfernplan als Trockenlauf) und AVTBEREINIGEN (führt den Plan
/// aus und speichert unter neuem Namen). Nur für Kopien unter C:\Temp, Ausgabe nur nach C:\Temp.
/// </summary>
public sealed class Befehle
{
    const string Erlaubt = @"C:\Temp\";

    static bool UnterTemp(string pfad) => Path.IsPathFullyQualified(pfad)
        && Path.GetFullPath(pfad).StartsWith(Erlaubt, StringComparison.OrdinalIgnoreCase);

    internal static string? Pfad(Editor ed, string frage)
    {
        var p = ed.GetString($"\n{frage}: ").StringResult.Trim().Trim('"');
        if (UnterTemp(p)) return Path.GetFullPath(p);
        ed.WriteMessage($"\nAVT abgelehnt: {p} liegt nicht unter {Erlaubt}");
        return null;
    }

    /// <summary>Vorsilben fremder NOD-Wörterbücher, die mit entfernt werden sollen („-“ = keine), durch Komma getrennt.</summary>
    static string[] Fremd(Editor ed) => ed.GetString("\nFremde Wörterbücher (Vorsilben, - = keine): ").StringResult
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(v => v != "-")
        // AutoCAD-eigene Vorsilben nie (würden AutoCAD- und AEC-Strukturen treffen, Review 23.10.2026).
        .Where(v => !v.StartsWith("AC", StringComparison.OrdinalIgnoreCase) && !v.StartsWith("AEC", StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Genaue Namen von Anzeigethemen, die entfernt werden sollen („-“ = keine), durch Semikolon oder Komma getrennt
    /// (powershell -File übergibt ein Array als Text mit Kommas).</summary>
    static string[] Themen(Editor ed) => ed.GetString(new PromptStringOptions("\nAnzeigethemen entfernen (Namen mit ;, - = keine): ") { AllowSpaces = true })
        .StringResult.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(v => v != "-").ToArray();

    internal static bool Testkopie(Document doc)
    {
        if (UnterTemp(doc.Name)) return true;
        doc.Editor.WriteMessage($"\nAVT abgelehnt: {doc.Name} ist keine Kopie unter {Erlaubt}");
        return false;
    }

    [CommandMethod("AVTINVENTUR", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Inventur()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Testkopie(doc) || Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var fremd = Fremd(doc.Editor);
        var themenWeg = Themen(doc.Editor);
        var aus = new List<string>();
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var b = Bestand.Lies(doc.Database, tr);
            Zahlen(doc.Database, tr, b, aus);
            Schreibe(Plan.Erstelle(b, doc.Database, fremd, themenWeg), b, aus);
            tr.Commit();
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Ablegen(doc.Editor, datei, aus, "Inventur");
    }

    [CommandMethod("AVTBEREINIGEN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Bereinigen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Testkopie(doc) || Pfad(doc.Editor, "Ausgabedatei") is not { } datei || Pfad(doc.Editor, "Ergebniszeichnung") is not { } ziel) return;
        var fremd = Fremd(doc.Editor);
        var themenWeg = Themen(doc.Editor);
        if (string.Equals(ziel, Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase))
        {
            doc.Editor.WriteMessage("\nAVT abgelehnt: Ergebnis muss eine neue Datei sein.");
            return;
        }
        var aus = new List<string>();
        var db = doc.Database;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var b = Bestand.Lies(db, tr);
                var plan = Plan.Erstelle(b, db, fremd, themenWeg);
                Schreibe(plan, b, aus);
                // Lücken im Verweisgraphen: lieber nichts tun als hängende Verweise speichern (Review).
                if (b.Unlesbar > 0 || b.OhneZeiger.Any(i => !plan.Weg(i)))
                    throw new InvalidOperationException($"Verweisgraph unvollständig (unlesbar {b.Unlesbar}, ohne Zeiger {b.OhneZeiger.Count}), nichts gespeichert");
                // Fehler beim Entfernen: Transaktion verwerfen, nicht speichern.
                if (Ausfuehren(db, tr, b, plan, aus) > 0)
                    throw new InvalidOperationException("Fehler beim Entfernen (AVTFEHLER), nichts gespeichert");
                tr.Commit();
                RegAppsBereinigen(db, plan, aus);
            }
            db.SaveAs(ziel, DwgVersion.Current);
            aus.Add($"AVTGESPEICHERT {ziel}");
            aus.Add("AVTENDE ok");
        }
        catch (System.Exception ex) { aus.Add($"AVTENDE FEHLER {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        Ablegen(doc.Editor, datei, aus, "Bereinigen");
    }

    /// <summary>Ausgabe in die Datei; scheitert das, wenigstens das Ende in die Befehlszeile.</summary>
    internal static void Ablegen(Editor ed, string datei, List<string> aus, string was)
    {
        try
        {
            File.WriteAllLines(datei, aus);
            ed.WriteMessage($"\nAVT {was}: {aus.Count} Zeilen in {datei}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nAVT {was}: Ausgabe nicht schreibbar ({ex.Message}). Letzte Zeile: {aus.LastOrDefault()}");
        }
    }

    /// <summary>
    /// RegApps aus dem Plan in eigener Transaktion löschen, nur soweit Database.Purge sie als unbenutzt freigibt (wie PURGE).
    /// </summary>
    static void RegAppsBereinigen(Database db, Plan plan, List<string> aus)
    {
        var ids = new ObjectIdCollection(plan.Wurzeln.Where(w => w.Value is "Modul-Spur RegApp" or "RegApp ohne Benutzer")
            .Select(w => w.Key).Where(i => !i.IsErased).ToArray());
        var gewollt = ids.Cast<ObjectId>().ToList();
        db.Purge(ids);
        var frei = ids.Cast<ObjectId>().ToHashSet();
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var id in frei) tr.GetObject(id, OpenMode.ForWrite).Erase();
        tr.Commit();
        foreach (var id in gewollt.Where(i => !frei.Contains(i))) aus.Add($"AVTRESTREGAPP {id.Handle} | Purge gibt sie nicht frei");
        aus.Add($"AVTERGEBNIS RegApps gelöscht {frei.Count} von {gewollt.Count}");
    }

    /// <summary>Zahlen für den Vorher/Nachher-Vergleich.</summary>
    static void Zahlen(Database db, Transaction tr, Bestand b, List<string> aus)
    {
        aus.Add($"AVTZAHL Objekte gesamt | {b.Alle.Count}");
        aus.Add($"AVTZAHL Objekte unlesbar | {b.Unlesbar}");
        foreach (var g in b.Alle.Values.GroupBy(o => o.Klasse).OrderBy(g => g.Key)) aus.Add($"AVTKLASSE {g.Key} | {g.Count()}");
        // Inhalt je Raum (Modell und Layouts) nach Klasse.
        var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (var e in layouts)
        {
            var l = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
            var inhalt = b.Alle.Values.Where(o => o.Besitzer == l.BlockTableRecordId).ToList();
            aus.Add($"AVTRAUM {l.LayoutName} | {inhalt.Count} | {string.Join(", ", inhalt.GroupBy(o => o.Klasse).Select(g => $"{g.Key}×{g.Count()}"))}");
            foreach (var o in inhalt.Where(o => o.Klasse == "AcDbBlockReference" && !l.ModelType))
            {
                var r = (BlockReference)tr.GetObject(o.Id, OpenMode.ForRead);
                aus.Add($"AVTRAUMBLOCK {l.LayoutName} | {r.Name} | Layer {r.Layer}");
            }
        }
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        foreach (var e in nod)
        {
            var o = tr.GetObject(e.Value, OpenMode.ForRead);
            var anzahl = o is DBDictionary d ? d.Count.ToString() : "-";
            aus.Add($"AVTNOD {e.Key} | {Bestand.Klasse(o)} | {anzahl}");
        }
        aus.Add($"AVTZAHL Symbolstile | {Zaehle(new DictionarySchematicSymbolStyle(db).Records)}");
        var stile = new DictionarySchematicSymbolStyle(db).Records.Cast<ObjectId>().Where(i => !i.IsErased)
            .Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<SchematicSymbolStyle>().ToList();
        aus.Add($"AVTZAHL Symbolstile mit Beschreibung | {stile.Count(s => !string.IsNullOrWhiteSpace(s.Description))}");
        aus.Add($"AVTZAHL Symbolstile mit Ansichtsblock | {stile.Count(s => s.MultiViewPartViewDefinitions.Cast<MultiViewPartViewDefinition>().Any(v => !v.BlockId.IsNull && !v.BlockId.IsErased))}");
        aus.Add($"AVTZAHL Systemdefinitionen | {Zaehle(new DictionarySchematicSystemDefinition(db).Records)}");
        aus.Add($"AVTZAHL Eigenschaftssatzdefinitionen | {Zaehle(new Aecp.DictionaryPropertySetDefinitions(db).Records)}");
        aus.Add($"AVTZAHL Anzeigethemen | {(nod.Contains("AEC_DISPLAYTHEME_STYLES") ? ((DBDictionary)tr.GetObject(nod.GetAt("AEC_DISPLAYTHEME_STYLES"), OpenMode.ForRead)).Count : 0)}");
        foreach (var (name, id) in new[] { ("Layer", db.LayerTableId), ("Linientypen", db.LinetypeTableId), ("Textstile", db.TextStyleTableId),
                     ("Bemassungsstile", db.DimStyleTableId), ("RegApps", db.RegAppTableId), ("Blöcke", db.BlockTableId) })
        {
            var t = (SymbolTable)tr.GetObject(id, OpenMode.ForRead);
            var namen = t.Cast<ObjectId>().Select(i => ((SymbolTableRecord)tr.GetObject(i, OpenMode.ForRead)).Name).ToList();
            aus.Add($"AVTZAHL {name} | {namen.Count}");
            // Anonyme Blöcke (*U…) ohne Namen; die Zahl der gelisteten steht am Ende (AVTZAHL zählt alle).
            var sichtbar = namen.Where(n => !n.StartsWith('*')).OrderBy(n => n).ToList();
            aus.Add($"AVTNAMEN {name} | {string.Join("; ", sichtbar)} | gelistet {sichtbar.Count}");
        }
        aus.Add($"AVTZAHL Layouts | {layouts.Count}");
        // Wer benutzt welche RegApp (XData oder Zeiger)? Zur Prüfung, warum eine RegApp das Speichern übersteht.
        foreach (var r in b.Alle.Values.Where(o => o.Klasse == "AcDbRegAppTableRecord" && !o.Name.StartsWith("AC", StringComparison.OrdinalIgnoreCase)
                     && !o.Name.StartsWith("AEC", StringComparison.OrdinalIgnoreCase)))
        {
            var xdata = b.Alle.Values.Where(o => o.XDataApps.Contains(r.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var zeiger = b.Verwiesen.GetValueOrDefault(r.Id) ?? [];
            aus.Add($"AVTREGAPP {r.Handle} | XData {xdata.Count} | Zeiger {zeiger.Count} | z. B. {(xdata.Count > 0 ? b.Text(xdata[0].Id) : zeiger.Count > 0 ? b.Text(zeiger[0]) : "-")}");
        }
        foreach (var o in b.Alle.Values.Where(o => o.Klasse == Plan.KlasseSatzDef)) aus.Add($"AVTSATZDEF {o.Name} | {o.Handle}");
        var themen = new List<DBDictionaryEntry>();
        if (nod.Contains("AEC_DISPLAYTHEME_STYLES"))
        {
            foreach (var e in (DBDictionary)tr.GetObject(nod.GetAt("AEC_DISPLAYTHEME_STYLES"), OpenMode.ForRead)) themen.Add(e);
        }
        foreach (var e in themen)
        {
            var zeiger = b.Alle.TryGetValue(e.Value, out var t) ? t.Zeiger.Select(b.Text) : [];
            aus.Add($"AVTTHEMA {e.Key} | zeigt auf: {string.Join("; ", zeiger)}");
        }
    }

    static int Zaehle(ObjectIdCollection ids) => ids.Cast<ObjectId>().Count(i => !i.IsErased);

    static void Schreibe(Plan plan, Bestand b, List<string> aus)
    {
        foreach (var g in plan.Wurzeln.GroupBy(w => w.Value).OrderBy(g => g.Key))
        {
            aus.Add($"AVTPLANZAHL {g.Key} | {g.Count()}");
            if (g.Key is "Modellbereich" or "Eigenschaftssatz" or "RegApp ohne Benutzer")
            {
                foreach (var k in g.GroupBy(w => b.Alle[w.Key].Klasse)) aus.Add($"AVTPLAN {g.Key} | {k.Key} | {k.Count()}");
                continue;
            }
            foreach (var w in g.OrderBy(w => b.Alle[w.Key].Name)) aus.Add($"AVTPLAN {g.Key} | {b.Text(w.Key)}");
        }
        foreach (var (ziel, von) in plan.Geschuetzt) aus.Add($"AVTSCHUTZ {b.Text(ziel)} | bleibt wegen {b.Text(von)}");
        foreach (var g in plan.OffeneVerweise.GroupBy(v => (b.Alle[v.Von].Klasse, Ziel: b.Alle[v.Ziel].Klasse)))
            aus.Add($"AVTVERWEIS {g.Key.Klasse} → {g.Key.Ziel} | {g.Count()} | z. B. {b.Text(g.First().Von)} → {b.Text(g.First().Ziel)}");
        foreach (var t in plan.NichtGefundeneThemen) aus.Add($"AVTWARNUNG Thema nicht gefunden: {t}");
        foreach (var id in plan.NurImModell) aus.Add($"AVTNURMODELL {b.Text(id)} | bleibt");
        foreach (var id in plan.SchonUnbenutzt) aus.Add($"AVTUNBENUTZT {b.Text(id)} | bleibt");
        aus.Add($"AVTLUECKE unlesbar {b.Unlesbar} | ohne Zeiger {b.OhneZeiger.Count} | davon bleibend {b.OhneZeiger.Count(i => !plan.Weg(i))}");
        foreach (var id in b.OhneZeiger.Where(i => !plan.Weg(i))) aus.Add($"AVTOHNEZEIGER {b.Text(id)}");
    }

    /// <summary>
    /// Erst Sätze an bleibenden Objekten über die Schnittstelle lösen, dann die Wurzeln löschen (Inhalt vor Behälter).
    /// Gibt die Zahl der Fehler zurück.
    /// </summary>
    static int Ausfuehren(Database db, Transaction tr, Bestand b, Plan plan, List<string> aus)
    {
        int saetze = 0, geloescht = 0, fehler = 0;
        foreach (var (id, grund) in plan.Wurzeln.Where(w => w.Value == "Eigenschaftssatz"))
        {
            var traeger = b.Besitzer(id).FirstOrDefault(x => b.Alle.TryGetValue(x, out var t) && t.Klasse != "AcDbDictionary");
            if (traeger.IsNull || plan.Weg(traeger)) continue;
            try
            {
                var satz = (Aecp.PropertySet)tr.GetObject(id, OpenMode.ForRead);
                var obj = tr.GetObject(traeger, OpenMode.ForWrite);
                Aecp.PropertyDataServices.RemovePropertySet(obj, satz.PropertySetDefinition);
                saetze++;
            }
            catch (System.Exception ex) { fehler++; aus.Add($"AVTFEHLER Satz {b.Text(id)} an {b.Text(traeger)}: {ex.GetBaseException().Message}"); }
        }
        // RegApps getrennt nach dem Commit (RegAppsBereinigen): in derselben Transaktion gelöscht, holte AutoCAD sie beim
        // Speichern zurück (Lauf 23.10.2026, gleiche Handles, keine Benutzer).
        var reihenfolge = new[] { "Modellbereich", "Legendenblock eaklLeg", "Mehransichtsblock TAG", "nur von Entferntem benutzt",
            "Bauteiltabellenstil", "Eigenschaftssatzdefinition", "Modul-Spur Wörterbuch", "Fremd-Spur Wörterbuch", "Anzeigethema auf Auftrag",
            "Modul-Layer" };
        foreach (var grund in reihenfolge)
        {
            foreach (var id in plan.Wurzeln.Where(w => w.Value == grund).Select(w => w.Key).ToList())
            {
                if (id.IsErased) continue;
                try
                {
                    var o = tr.GetObject(id, OpenMode.ForWrite);
                    if (grund is "Modul-Spur Wörterbuch" or "Fremd-Spur Wörterbuch" or "Anzeigethema auf Auftrag")
                    {
                        // Aus dem besitzenden Wörterbuch austragen (NOD oder Erweiterungswörterbuch), dann löschen.
                        if (tr.GetObject(o.OwnerId, OpenMode.ForWrite) is DBDictionary halter) halter.Remove(id);
                    }
                    o.Erase();
                    geloescht++;
                }
                catch (System.Exception ex) { fehler++; aus.Add($"AVTFEHLER {grund} {b.Text(id)}: {ex.GetBaseException().Message}"); }
            }
        }
        aus.Add($"AVTERGEBNIS Sätze gelöst {saetze} | gelöscht {geloescht} | Fehler {fehler}");
        return fehler;
    }
}
