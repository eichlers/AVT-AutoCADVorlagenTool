using Avt.Kern;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AecDb = Autodesk.Aec.DatabaseServices;
using Aecp = Autodesk.Aec.PropertyData.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Avt.Werkzeug.VorlageLesen))]
namespace Avt.Werkzeug;

/// <summary>
/// AVTVORLAGELESEN (AVT 0.4.0, Auftrag AMK 24.10.2026): liest aus einer Vorlage (Kopie) nur lesend, was der EHS-Bau
/// übernimmt oder nachbaut: Layouts mit Papierformat/Plotbereich und den Objekten im Papierbereich (Rahmen-Blöcke mit
/// Größe und Inhalt), Satzdefinitionen mit Feldern (Typ, automatische Quelle, Formel, Liste), Listen und Anzeigethemen mit
/// Regeln und Darstellung. Ausgabe als Textdatei, eine Angabe je Zeile.
/// </summary>
public sealed class VorlageLesen
{
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    static string Z(double d) => d.ToString("0.###", Inv);
    static string Pt(Autodesk.AutoCAD.Geometry.Point3d p) => $"{Z(p.X)},{Z(p.Y)}";

    [CommandMethod("AVTVORLAGELESEN", CommandFlags.Modal | CommandFlags.NoHistory)]
    public void Lesen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (!Befehle.Testkopie(doc) || Befehle.Pfad(doc.Editor, "Ausgabedatei") is not { } datei) return;
        var db = doc.Database;
        var aus = new List<string>();
        void Teil(string was, Action<Transaction> tun)
        {
            using var tr = db.TransactionManager.StartTransaction();
            try { tun(tr); tr.Commit(); }
            catch (System.Exception ex) { tr.Abort(); aus.Add($"FEHLER {was} | {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        }
        Teil("Layouts", tr => Layouts(db, tr, aus));
        Teil("Saetze", tr => Saetze(db, tr, aus));
        Teil("Listen", tr => Listen(db, tr, aus));
        Teil("Themen", tr => Themen(db, tr, aus));
        aus.Add("AVTENDE ok");
        Befehle.Ablegen(doc.Editor, datei, aus, "Vorlage");
    }

    static void Layouts(Database db, Transaction tr, List<string> aus)
    {
        var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (var e in dict)
        {
            var l = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
            aus.Add($"LAYOUT | {l.LayoutName} | Reihenfolge {l.TabOrder} | Plotter {l.PlotConfigurationName} | Papier {l.CanonicalMediaName} | Größe {Z(l.PlotPaperSize.X)}x{Z(l.PlotPaperSize.Y)} | Einheit {l.PlotPaperUnits} | Drehung {l.PlotRotation} | Plotbereich {l.PlotType} | Fenster {Z(l.PlotWindowArea.MinPoint.X)},{Z(l.PlotWindowArea.MinPoint.Y)}-{Z(l.PlotWindowArea.MaxPoint.X)},{Z(l.PlotWindowArea.MaxPoint.Y)} | Ursprung {Z(l.PlotOrigin.X)},{Z(l.PlotOrigin.Y)} | Maßstab {Z(l.CustomPrintScale.Numerator)}:{Z(l.CustomPrintScale.Denominator)} | Plotstil {l.CurrentStyleSheet} | Ränder {Z(l.PlotPaperMargins.MinPoint.X)},{Z(l.PlotPaperMargins.MinPoint.Y)},{Z(l.PlotPaperMargins.MaxPoint.X)},{Z(l.PlotPaperMargins.MaxPoint.Y)} | Limiten {Z(l.Limits.MinPoint.X)},{Z(l.Limits.MinPoint.Y)}-{Z(l.Limits.MaxPoint.X)},{Z(l.Limits.MaxPoint.Y)}");
            if (l.ModelType) continue;
            var raum = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
            foreach (ObjectId id in raum)
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var ausdehnung = "";
                try { var x = ent.GeometricExtents; ausdehnung = $"{Pt(x.MinPoint)}-{Pt(x.MaxPoint)}"; } catch (System.Exception) { ausdehnung = "?"; }
                var text = ent switch
                {
                    BlockReference br => $"Block {((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name} bei {Pt(br.Position)} Maßstab {Z(br.ScaleFactors.X)} Drehung {Z(br.Rotation)}",
                    Viewport vp => $"Ansichtsfenster Mitte {Pt(vp.CenterPoint)} {Z(vp.Width)}x{Z(vp.Height)} Maßstab {Z(vp.CustomScale)} Nr {vp.Number}",
                    DBText t => $"Text „{t.TextString}“ Stil {t.TextStyleName} Höhe {Z(t.Height)}",
                    MText m => $"MText „{m.Contents}“ Höhe {Z(m.TextHeight)}",
                    _ => ent.GetType().Name,
                };
                aus.Add($"PAPIER | {l.LayoutName} | {text} | Layer {ent.Layer} | Ausdehnung {ausdehnung}");
                if (ent is BlockReference b)
                {
                    Block(tr, b.DynamicBlockTableRecord, aus, l.LayoutName, new HashSet<ObjectId>());
                    if (b.IsDynamicBlock) Formate(tr, b, aus, l.LayoutName);
                }
            }
        }
    }

    /// <summary>
    /// Dynamischer Block: Eigenschaften mit erlaubten Werten; für jeden Sichtbarkeitszustand die sichtbaren Rechtecke
    /// (Außenmaß und Innenrahmen) und die Zahl der Linien. Nur im Speicher der Kopie (Transaktion wird verworfen).
    /// </summary>
    static void Formate(Transaction aussen, BlockReference b, List<string> aus, string wo)
    {
        foreach (DynamicBlockReferenceProperty p in b.DynamicBlockReferencePropertyCollection)
            aus.Add($"DYNEIGENSCHAFT | {wo} | {p.PropertyName} | Wert {p.Value} | Typ {p.PropertyTypeCode} | erlaubt {string.Join(" ; ", p.GetAllowedValues().Select(v => Convert.ToString(v, Inv)))} | nur lesen {p.ReadOnly}");
        var db = b.Database;
        foreach (DynamicBlockReferenceProperty p in b.DynamicBlockReferencePropertyCollection)
        {
            if (p.ReadOnly || p.GetAllowedValues().Length == 0) continue;
            foreach (var wert in p.GetAllowedValues())
            {
                using var tr = db.TransactionManager.StartTransaction();
                try
                {
                    var r = (BlockReference)tr.GetObject(b.ObjectId, OpenMode.ForWrite);
                    r.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().First(x => x.PropertyName == p.PropertyName).Value = wert;
                    var btr = (BlockTableRecord)tr.GetObject(r.BlockTableRecord, OpenMode.ForRead);
                    var rechtecke = new List<string>();
                    var linien = 0;
                    foreach (ObjectId k in btr)
                    {
                        var e = (Entity)tr.GetObject(k, OpenMode.ForRead);
                        if (!e.Visible) continue;
                        if (e is Polyline pl && pl.Closed) rechtecke.Add($"{Pt(pl.GeometricExtents.MinPoint)}-{Pt(pl.GeometricExtents.MaxPoint)}");
                        else if (e is Line) linien++;
                    }
                    aus.Add($"FORMAT | {wo} | {p.PropertyName} = {Convert.ToString(wert, Inv)} | Rechtecke {string.Join(" ; ", rechtecke)} | Linien {linien}");
                }
                catch (System.Exception ex) { aus.Add($"FORMAT | {wo} | {p.PropertyName} = {wert} | FEHLER {ex.GetBaseException().Message}"); }
                finally { tr.Abort(); }
            }
        }
    }

    static void Block(Transaction tr, ObjectId id, List<string> aus, string wo, HashSet<ObjectId> gesehen)
    {
        if (!gesehen.Add(id)) return;
        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
        var arten = new Dictionary<string, int>();
        foreach (ObjectId k in btr)
        {
            var ent = (Entity)tr.GetObject(k, OpenMode.ForRead);
            var art = ent.GetType().Name;
            arten[art] = arten.GetValueOrDefault(art) + 1;
            switch (ent)
            {
                case AttributeDefinition a:
                    aus.Add($"BLOCKATTRIBUT | {wo} | {btr.Name} | Tag {a.Tag} | Frage „{a.Prompt}“ | Wert „{a.TextString}“ | Stil {a.TextStyleName} | Höhe {Z(a.Height)} | bei {Pt(a.Position)} | Layer {a.Layer}");
                    break;
                case DBText t:
                    aus.Add($"BLOCKTEXT | {wo} | {btr.Name} | „{t.TextString}“ | Stil {t.TextStyleName} | Höhe {Z(t.Height)} | bei {Pt(t.Position)} | Layer {t.Layer}");
                    break;
                case MText m:
                    aus.Add($"BLOCKTEXT | {wo} | {btr.Name} | „{m.Contents}“ | Stil {m.TextStyleName} | Höhe {Z(m.TextHeight)} | bei {Pt(m.Location)} | Layer {m.Layer}");
                    break;
                case Polyline p:
                    aus.Add($"BLOCKLINIE | {wo} | {btr.Name} | Polylinie {p.NumberOfVertices} Punkte, geschlossen {p.Closed} | {Pt(p.GeometricExtents.MinPoint)}-{Pt(p.GeometricExtents.MaxPoint)} | Layer {p.Layer} | Breite {Z(p.ConstantWidth)} | Linienstärke {p.LineWeight}");
                    break;
                case BlockReference br:
                    aus.Add($"BLOCKBLOCK | {wo} | {btr.Name} | {((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name} bei {Pt(br.Position)}");
                    Block(tr, br.DynamicBlockTableRecord, aus, wo, gesehen);
                    break;
            }
        }
        var layer = btr.Cast<ObjectId>().Select(k => ((Entity)tr.GetObject(k, OpenMode.ForRead)).Layer).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        aus.Add($"BLOCK | {wo} | {btr.Name} | {string.Join(", ", arten.OrderBy(a => a.Key).Select(a => $"{a.Value} {a.Key}"))} | Layer {string.Join(", ", layer)} | Einfügepunkt {Pt(btr.Origin)} | dynamisch {btr.IsDynamicBlock}");
    }

    static void Saetze(Database db, Transaction tr, List<string> aus)
    {
        foreach (ObjectId id in new Aecp.DictionaryPropertySetDefinitions(db).Records)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Aecp.PropertySetDefinition d) continue;
            aus.Add($"SATZ | {d.Name} | stilbasiert {d.IsStyleBased} | alle {d.AppliesToAll} | gilt für {string.Join(",", d.AppliesToFilter.Cast<string>())} | Beschreibung {d.Description}");
            foreach (Aecp.PropertyDefinition f in d.Definitions)
            {
                var quelle = "";
                if (f.Automatic && f is not Aecp.PropertyDefinitionFormula)
                    quelle = string.Join(" + ", f.GetAutomaticData().Select(a => string.Join("/", a.GetType().GetProperties().Select(p => $"{p.Name}={Convert.ToString(p.GetValue(a))}"))));
                var formel = f is Aecp.PropertyDefinitionFormula fo ? fo.GetFormulaString().Replace("\r", " ").Replace("\n", " ") : "";
                var liste = f.ListDefinitionId.IsNull ? "" : ((AecDb.DictionaryRecord)tr.GetObject(f.ListDefinitionId, OpenMode.ForRead)).Name;
                aus.Add($"FELD | {d.Name} | {f.Name} | Typ {f.DataType} | automatisch {f.Automatic} | Quelle {quelle} | Formel {formel} | Liste {liste} | Vorgabe {Convert.ToString(f.DefaultData, Inv)} | sichtbar {f.IsVisible} | Beschreibung {f.Description}");
            }
        }
    }

    static void Listen(Database db, Transaction tr, List<string> aus)
    {
        foreach (ObjectId id in new AecDb.DictionaryListDefinition(db).Records)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not AecDb.ListDefinition l) continue;
            aus.Add($"LISTE | {l.Name} | {string.Join(" ; ", Eintraege(l))}");
        }
    }

    // Einträge einer Liste per Reflexion (der Name der Sammlung unterscheidet sich je Fassung der API).
    static IEnumerable<string> Eintraege(object l)
    {
        foreach (var p in l.GetType().GetProperties())
        {
            if (p.PropertyType == typeof(string) || !typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)) continue;
            if (p.GetValue(l) is not System.Collections.IEnumerable e) continue;
            var liste = e.Cast<object>().Select(i => Convert.ToString(i.GetType().GetProperty("Name")?.GetValue(i) ?? i) ?? "").ToList();
            if (liste.Count > 0) return liste.Prepend($"[{p.Name}]");
        }
        return ["(keine Einträge gefunden)"];
    }

    static void Themen(Database db, Transaction tr, List<string> aus)
    {
        foreach (ObjectId id in new AecDb.DictionaryDisplayThemeStyle(db).Records)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not AecDb.DisplayThemeStyle t) continue;
            aus.Add($"THEMA | {t.Name} | Titel {t.Title} | Beschreibung {t.Description} | Komponenten {t.DisplayThemeComponents.Count}");
            var nr = 0;
            foreach (AecDb.DisplayThemeComponentBase k in t.DisplayThemeComponents)
            {
                nr++;
                var dp = k.DisplayProperties;
                var darst = dp is null ? "keine" : string.Join(" ", dp.GetType().GetProperties().Where(p => p.Name is "Color" or "LinetypeId" or "LineWeight" or "LinetypeScale" or "Visible").Select(p => { try { var v = p.GetValue(dp); return $"{p.Name}={(v is ObjectId o && !o.IsNull ? ((SymbolTableRecord)tr.GetObject(o, OpenMode.ForRead)).Name : Convert.ToString(v))}"; } catch (System.Exception) { return $"{p.Name}=?"; } }));
                aus.Add($"KOMPONENTE | {t.Name} | {nr} | {k.Name} | Regel {k.Rules?.DisplayString} | {RegelZiel(tr, k.Rules)} | {darst}");
            }
        }
    }

    static string RegelZiel(Transaction tr, object? r)
    {
        if (r is null) return "";
        var teile = new List<string> { r.GetType().Name };
        foreach (var p in r.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0 || p.Name is "DisplayString") continue;
            object? v;
            try { v = p.GetValue(r); } catch (System.Exception) { continue; }
            var text = v switch
            {
                ObjectId o when !o.IsNull => Name(tr, o),
                System.Collections.IEnumerable e and not string => "[" + string.Join(", ", e.Cast<object>().Take(8).Select(x => RegelZiel(tr, x))) + "]",
                _ => Convert.ToString(v, Inv),
            };
            teile.Add($"{p.Name}={text}");
        }
        return string.Join(" ", teile);
    }

    static string Name(Transaction tr, ObjectId o)
    {
        try
        {
            var x = tr.GetObject(o, OpenMode.ForRead);
            return x switch { AecDb.DictionaryRecord d => d.Name, SymbolTableRecord s => s.Name, _ => x.GetType().Name };
        }
        catch (System.Exception) { return o.ToString(); }
    }
}
