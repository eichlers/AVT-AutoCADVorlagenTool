using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Avt.Kern;
using Avt.Kern.Fremd;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Avt.Mep.Palette;

/// <summary>
/// Inhalt der AVT-Palette (WPF im Code): „Diese Zeichnung“ (nur lesen), leiser Hinweis, Prüfen, Bereinigen mit Vorschau
/// a/b/c → Bestätigung → Sicherungskopie → Bereinigen → Nachprüfung → Bericht. AVT speichert nie.
/// </summary>
public sealed class AvtAnsicht : UserControl
{
    readonly TextBlock zeichnung = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    readonly TextBlock fassung = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock hinweis = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x5B, 0x00)) };
    readonly StackPanel vorschau = new();
    readonly TextBox bericht = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 80, FontFamily = new FontFamily("Consolas"), FontSize = 11,
    };
    readonly Button bereinigenStart = new() { Content = "Ausgewählte bereinigen …", Margin = new Thickness(0, 6, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
    readonly Button abbrechen = new() { Content = "Abbrechen", Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
    readonly StackPanel vorschauLeiste = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };

    Document? dokument;
    Analyse? letzte;
    readonly List<(CheckBox Box, Einordnung Fund)> auswahl = [];

    public AvtAnsicht()
    {
        var kopf = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        kopf.Children.Add(Programminfo.Kennung());
        var zahnrad = new Button { Content = "⚙", ToolTip = "Version, Stand, Versionshistorie, Einstellungen", Width = 26, Margin = new Thickness(0, 2, 4, 0) };
        zahnrad.Click += (_, _) => Zahnrad.Zeigen();
        DockPanel.SetDock(zahnrad, Dock.Right);
        kopf.Children.Add(zahnrad);

        var pruefen = new Button { Content = "Prüfen", ToolTip = "Nur lesen: Fassung, Zeichnungseigenschaften, Fremdinhalte, Inhaltsprüfsumme", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        pruefen.Click += (_, _) => Sicher(Pruefen);
        var bereinigen = new Button { Content = "Bereinigen …", ToolTip = "Vorschau der Fremdinhalte (a/b/c) – noch nichts wird geändert", Padding = new Thickness(10, 2, 10, 2) };
        bereinigen.Click += (_, _) => Sicher(VorschauZeigen);
        var knoepfe = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        knoepfe.Children.Add(pruefen);
        knoepfe.Children.Add(bereinigen);

        bereinigenStart.Click += (_, _) => Sicher(Ausfuehren);
        abbrechen.Click += (_, _) => { vorschau.Children.Clear(); auswahl.Clear(); vorschauLeiste.Visibility = Visibility.Collapsed; };
        vorschauLeiste.Children.Add(bereinigenStart);
        vorschauLeiste.Children.Add(abbrechen);

        var inhalt = new StackPanel { Margin = new Thickness(8) };
        inhalt.Children.Add(kopf);
        inhalt.Children.Add(Ueberschrift("Diese Zeichnung"));
        inhalt.Children.Add(zeichnung);
        inhalt.Children.Add(fassung);
        inhalt.Children.Add(hinweis);
        inhalt.Children.Add(knoepfe);
        inhalt.Children.Add(vorschau);
        inhalt.Children.Add(vorschauLeiste);
        inhalt.Children.Add(Ueberschrift("Bericht"));
        inhalt.Children.Add(bericht);
        Content = new ScrollViewer { Content = inhalt, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    static TextBlock Ueberschrift(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };

    /// <summary>Kopf „Diese Zeichnung“ für die aktive Zeichnung (nur lesen); einen gemerkten Hinweis anzeigen.</summary>
    internal void ZeichnungAnzeigen(Document? doc)
    {
        if (doc != dokument) { vorschau.Children.Clear(); auswahl.Clear(); vorschauLeiste.Visibility = Visibility.Collapsed; letzte = null; }
        dokument = doc;
        if (doc == null) { zeichnung.Text = "Keine Zeichnung offen."; fassung.Text = ""; hinweis.Text = ""; return; }
        zeichnung.Text = Path.GetFileName(doc.Name);
        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var f = Fremdanalyse.Fassung(doc.Database, tr);
            fassung.Text = f is null
                ? $"Keine EHS-Vorlage (kein Fassungsstempel) – Vergleich mit der Live-Fassung {Referenz.LiveFassung.Replace('_', ' ')}."
                : $"Fassung {f.Replace('_', ' ')}";
            tr.Commit();
        }
        hinweis.Text = Hinweis.Gemerkt(doc) is { } kurz ? $"{kurz} – Bereinigen …" : "";
    }

    internal void ZeichnungGeschlossen(Document doc)
    {
        if (doc != dokument) return;
        dokument = null; letzte = null;
        vorschau.Children.Clear(); auswahl.Clear(); vorschauLeiste.Visibility = Visibility.Collapsed;
        zeichnung.Text = "Keine Zeichnung offen."; fassung.Text = ""; hinweis.Text = "";
    }

    /// <summary>Leiser Hinweis (aus <see cref="Hinweis"/>) – nur anzeigen, wenn die Zeichnung gerade gezeigt wird.</summary>
    internal void HinweisSetzen(Document doc, string? kurz)
    {
        if (doc == dokument) hinweis.Text = kurz is null ? "" : $"{kurz} – Bereinigen …";
    }

    void Pruefen()
    {
        var doc = Aktiv();
        if (doc == null) return;
        var zeilen = new List<string> { $"Prüfen {Path.GetFileName(doc.Name)} · {DateTime.Now:dd.MM.yyyy HH:mm}" };
        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var db = doc.Database;
            zeilen.AddRange(Zeichnungsangaben.FassungLesen(db, tr).Select(f => $"Fassungsstempel: {f}"));
            var fehlend = Zeichnungsangaben.FehlendePflichtfelder(db);
            zeilen.Add($"DWGPROPS-Pflichtfelder: {(fehlend.Count == 0 ? "alle da" : "fehlt " + string.Join(", ", fehlend))}");
            var a = Fremdanalyse.Lies(db, tr, null);
            letzte = a;
            zeilen.Add($"Vergleich mit: {a.Referenz.Fassung.Replace('_', ' ')} ({a.Referenz.Herkunft})");
            zeilen.Add($"Fremdinhalte: {Einteilung.Kurzfassung(a.Funde) ?? "keine"} · a {Zahl(a, Gruppe.FremdUnbenutzt)} · b {Zahl(a, Gruppe.FremdBenutzt)} · c {Zahl(a, Gruppe.Projektinhalt)}");
            var b = Bestand.Lies(db, tr);
            var k = Kanon.Lies(db, tr, b, []);
            zeilen.Add($"Inhaltsprüfsumme: {Kanon.Gesamt(k)[..16]} … ({k.Count} Objekte, nicht lesbar {b.Unlesbar}, Lesefehler {k.Count(z => z.Zustand == "fehler")})");
            zeilen.Add($"Dauer: {a.Dauer.TotalSeconds:F1} s (Fremdinhalte)");
            tr.Commit();
            HinweisSetzen(doc, Einteilung.Kurzfassung(a.Funde));
            Hinweis.Merken(doc, Einteilung.Kurzfassung(a.Funde));
            if (a.Luecke > 0) zeilen.Add($"Achtung: {a.Luecke} Objekte nicht lesbar – Bereinigen entfernt in dieser Zeichnung nichts.");
        }
        bericht.Text = string.Join(Environment.NewLine, zeilen);
        Protokoll.Merke("Prüfen", string.Join(" | ", zeilen));
    }

    static int Zahl(Analyse a, Gruppe g) => a.Funde.Count(f => f.Gruppe == g);

    /// <summary>Vorschau: getrennt nach a/b/c, nichts wird geändert. a angehakt, b nicht (b „behalten“ = Ausnahme merken), c nur Info.</summary>
    void VorschauZeigen()
    {
        var doc = Aktiv();
        if (doc == null) return;
        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            letzte = Fremdanalyse.Lies(doc.Database, tr, null);
            tr.Commit();
        }
        vorschau.Children.Clear();
        auswahl.Clear();
        var funde = letzte.Funde;
        HinweisSetzen(doc, Einteilung.Kurzfassung(funde));
        if (funde.Count == 0)
        {
            vorschau.Children.Add(new TextBlock { Text = "Keine Fremdinhalte – nichts zu bereinigen.", Margin = new Thickness(0, 4, 0, 4) });
            vorschauLeiste.Visibility = Visibility.Collapsed;
            return;
        }
        // Unvollständiger Verweisgraph (nicht lesbare Objekte): nichts zum Entfernen anbieten (Review 0.3.0).
        var luecke = letzte.Luecke > 0;
        if (luecke)
            vorschau.Children.Add(new TextBlock
            {
                Text = $"Achtung: {letzte.Luecke} Objekte ließen sich nicht lesen – AVT kann nicht sicher sagen, was unbenutzt ist, und entfernt nichts. Ausnahmen merken geht.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed, Margin = new Thickness(0, 4, 0, 4),
            });
        Gruppenliste(Gruppe.FremdUnbenutzt, "a) Fremd und unbenutzt – angehakt wird entfernt (vorab angehakt: mit Fremdspur)", !luecke);
        Gruppenliste(Gruppe.FremdBenutzt, "b) Fremd und benutzt – bleibt; angehakt = als Ausnahme merken (Hinweis kommt nicht mehr)", true);
        Gruppenliste(Gruppe.Projektinhalt, "c) Projektinhalt – nur Information", false);
        vorschauLeiste.Visibility = funde.Any(f => f.Gruppe != Gruppe.Projektinhalt) ? Visibility.Visible : Visibility.Collapsed;
        bericht.Text = $"Vorschau {Path.GetFileName(doc.Name)}: {Einteilung.Kurzfassung(funde)} · Projektinhalt {Zahl(letzte, Gruppe.Projektinhalt)}. Noch nichts geändert.";
    }

    void Gruppenliste(Gruppe gruppe, string titel, bool mitAuswahl)
    {
        var funde = letzte!.Funde.Where(f => f.Gruppe == gruppe).ToList();
        if (funde.Count == 0) return;
        var liste = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        foreach (var f in funde)
        {
            var text = $"{f.Kandidat.Art}: {f.Kandidat.Name} – {f.Grund}";
            if (mitAuswahl)
            {
                // a: vorab angehakt nur mit Fremdspur (Vorgabe); b: nie vorab.
                var box = new CheckBox { Content = text, IsChecked = f.Gruppe == Gruppe.FremdUnbenutzt && f.Vorgabe, ToolTip = f.Kandidat.Pfad, Margin = new Thickness(0, 1, 0, 1) };
                auswahl.Add((box, f));
                liste.Children.Add(box);
            }
            else liste.Children.Add(new TextBlock { Text = "• " + text, ToolTip = f.Kandidat.Pfad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
        }
        vorschau.Children.Add(new Expander
        {
            Header = $"{titel} ({funde.Count})", Content = liste, IsExpanded = gruppe != Gruppe.Projektinhalt, Margin = new Thickness(0, 2, 0, 2),
        });
    }

    /// <summary>Bestätigung → Sicherungskopie (gespeicherte Datei, Prüfsumme) → Bereinigen → Nachprüfung → Bericht. Speichern macht der Nutzer.</summary>
    void Ausfuehren()
    {
        var doc = Aktiv();
        if (doc == null || letzte == null) return;
        // Pfad UND Handle aus der Vorschau – entfernt wird nur, wenn beide noch passen (Review 0.3.0).
        var entfernen = auswahl.Where(x => x.Box.IsChecked == true && x.Fund.Gruppe == Gruppe.FremdUnbenutzt)
            .ToDictionary(x => x.Fund.Kandidat.Pfad, x => letzte.Objekte[x.Fund.Kandidat.Pfad].Handle.Value, StringComparer.Ordinal);
        var behalten = auswahl.Where(x => x.Box.IsChecked == true && x.Fund.Gruppe == Gruppe.FremdBenutzt)
            .ToDictionary(x => x.Fund.Kandidat.Pfad, _ => $"behalten (Nutzer, {DateTime.Now:dd.MM.yyyy})");
        if (entfernen.Count + behalten.Count == 0) { bericht.Text = "Nichts angehakt – nichts zu tun."; return; }
        // Sicherungskopie aus der gespeicherten Datei: ungespeicherte Änderungen wären nicht darin – darum vorher speichern lassen.
        if (doc.IsReadOnly)
        {
            bericht.Text = "Die Zeichnung ist schreibgeschützt geöffnet – Bereinigen lohnt nicht, das Ergebnis ließe sich nicht speichern.";
            return;
        }
        if (Hinweis.DateiWieGeoeffnet(doc) == false)
        {
            bericht.Text = "Die Datei wurde seit dem Öffnen von außen geändert (z. B. Synchronisierung) – die Sicherungskopie hätte einen "
                + "anderen Stand. Bitte die Zeichnung neu öffnen und dann bereinigen.";
            return;
        }
        if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0 || !File.Exists(doc.Name))
        {
            bericht.Text = "Die Zeichnung hat ungespeicherte Änderungen (oder ist noch nie gespeichert). Bitte zuerst speichern – "
                + "die Sicherungskopie entsteht aus der gespeicherten Datei. AVT speichert nie selbst.";
            return;
        }
        var frage = $"{entfernen.Count} Fremdinhalt(e) entfernen" + (behalten.Count > 0 ? $" und {behalten.Count} als Ausnahme merken" : "")
            + $"?\n\nVorher legt AVT eine Sicherungskopie neben die Zeichnung.\nAVT speichert nicht – das Ergebnis speichern Sie selbst (oder schließen ohne Speichern).";
        if (MessageBox.Show(frage, "AVT – Bereinigen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var zeilen = new List<string> { $"Bereinigen {Path.GetFileName(doc.Name)} · {DateTime.Now:dd.MM.yyyy HH:mm}" };
        string sicherung;
        try { sicherung = Fremdbereiniger.Sicherung(doc.Name, DateTime.Now); }
        catch (System.Exception ex) { bericht.Text = $"Sicherungskopie nicht möglich – nichts geändert. {ex.Message}"; Protokoll.Merke("Fehler", bericht.Text); return; }
        zeilen.Add($"Sicherungskopie (Prüfsumme gleich): {sicherung}");
        Analyse nach;
        bool fehler;
        using (doc.LockDocument())
        {
            var ergebnis = Fremdbereiniger.Ausfuehren(doc.Database, entfernen, behalten, null);
            fehler = ergebnis.Any(z => z.StartsWith("AVTFREMD FEHLER", StringComparison.Ordinal));
            zeilen.AddRange(ergebnis.Select(Lesbar));
            using var tr = doc.Database.TransactionManager.StartTransaction();
            nach = Fremdanalyse.Lies(doc.Database, tr, null);
            tr.Commit();
        }
        letzte = nach;
        if (fehler) zeilen.Insert(1, "NICHTS ENTFERNT – siehe FEHLER unten. Die Zeichnung ist unverändert; die Sicherungskopie kann gelöscht werden.");
        zeilen.Add($"Nachprüfung: {Einteilung.Kurzfassung(nach.Funde) ?? "keine Fremdinhalte"} · Projektinhalt {Zahl(nach, Gruppe.Projektinhalt)}");
        zeilen.Add("Jetzt speichern (Strg+S) – AVT speichert nicht. Ist ein Zusatzprogramm geladen, kann es beim Speichern Einträge neu anlegen: "
            + "dann die Zeichnung im MEP-Profil ohne Zusatzprogramm neu öffnen und erneut bereinigen (der Hinweis zeigt es).");
        vorschau.Children.Clear(); auswahl.Clear(); vorschauLeiste.Visibility = Visibility.Collapsed;
        HinweisSetzen(doc, Einteilung.Kurzfassung(nach.Funde));
        Hinweis.Merken(doc, Einteilung.Kurzfassung(nach.Funde));
        bericht.Text = string.Join(Environment.NewLine, zeilen);
        Protokoll.Merke("Bereinigen", string.Join(" | ", zeilen));
        doc.Editor.WriteMessage(fehler
            ? "\nAVT: Bereinigen abgebrochen – nichts entfernt (Bericht in der Palette).\n"
            : $"\nAVT: Bereinigt – Bericht in der Palette. Sicherung: {sicherung}. Speichern Sie selbst.\n");
    }

    static string Lesbar(string zeile) => zeile.StartsWith("AVTFREMD ", StringComparison.Ordinal) ? zeile[9..] : zeile;

    Document? Aktiv()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) { bericht.Text = "Keine Zeichnung offen."; return null; }
        if (doc != dokument) ZeichnungAnzeigen(doc);
        return doc;
    }

    void Sicher(Action tun)
    {
        try { Cursor = System.Windows.Input.Cursors.Wait; tun(); }
        catch (System.Exception ex)
        {
            bericht.Text = $"Fehler: {ex.GetBaseException().Message}";
            Protokoll.Merke("Fehler", $"Palette: {ex}");
        }
        finally { Cursor = null; }
    }
}
