using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Avt.Kern.Fremd;

namespace Avt.Mep.Palette;

/// <summary>
/// Aufbau der AVT-Palette ohne AutoCAD (WPF im Code) – damit lässt sie sich auch außerhalb von AutoCAD als Bild prüfen
/// (tools\vorschau). Die AutoCAD-Logik steckt in <see cref="AvtAnsicht"/>. Farben nach dem gemeinsamen Schema (<see cref="Farben"/>).
/// </summary>
public class AvtOberflaeche : UserControl
{
    internal Farben Schema { get; private set; } = Farben.Hell;

    protected readonly TextBlock zeichnung = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    protected readonly TextBlock fassung = new() { TextWrapping = TextWrapping.Wrap };
    protected readonly TextBlock hinweis = new() { TextWrapping = TextWrapping.Wrap };
    protected readonly StackPanel vorschau = new();
    protected readonly TextBox bericht = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 80, FontFamily = new FontFamily("Consolas"), FontSize = 11, Padding = new Thickness(3),
    };
    protected readonly Button zahnradKnopf = new() { Content = "⚙", ToolTip = "Version, Stand, Versionshistorie, Einstellungen", Width = 26, Margin = new Thickness(0, 2, 4, 0) };
    protected readonly Button pruefenKnopf = new() { Content = "Prüfen", ToolTip = "Nur lesen: Fassung, Zeichnungseigenschaften, Fremdinhalte, Inhaltsprüfsumme", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
    protected readonly Button bereinigenKnopf = new() { Content = "Bereinigen …", ToolTip = "Vorschau der Fremdinhalte (a/b/c) – noch nichts wird geändert", Padding = new Thickness(10, 2, 10, 2) };
    protected readonly Button bereinigenStart = new() { Content = "Ausgewählte bereinigen …", Margin = new Thickness(0, 6, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
    protected readonly Button abbrechen = new() { Content = "Abbrechen", Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
    protected readonly StackPanel vorschauLeiste = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    protected readonly List<(CheckBox Box, Einordnung Fund)> auswahl = [];

    public AvtOberflaeche()
    {
        var kopf = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        kopf.Children.Add(Programminfo.Kennung());
        DockPanel.SetDock(zahnradKnopf, Dock.Right);
        kopf.Children.Add(zahnradKnopf);
        var knoepfe = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        knoepfe.Children.Add(pruefenKnopf);
        knoepfe.Children.Add(bereinigenKnopf);
        vorschauLeiste.Children.Add(bereinigenStart);
        vorschauLeiste.Children.Add(abbrechen);
        abbrechen.Click += (_, _) => VorschauLeeren();

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
        Farbschema(Farben.Hell);
    }

    static TextBlock Ueberschrift(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };

    /// <summary>
    /// Farbschema anwenden (beim Öffnen, nach dem Einfügen in die Palette und bei jedem Umschalten von COLORTHEME) – auch
    /// Hinweis- und Warnfarben folgen dem Schema.
    /// </summary>
    internal void Farbschema(Farben f)
    {
        Schema = f;
        f.Anwenden(this);
        hinweis.Foreground = f.Befund;
        foreach (var w in vorschau.Children.OfType<TextBlock>().Where(t => t.Tag as string == "Warnung")) w.Foreground = f.Befund;
    }

    protected void VorschauLeeren()
    {
        vorschau.Children.Clear();
        auswahl.Clear();
        vorschauLeiste.Visibility = Visibility.Collapsed;
    }

    /// <summary>Vorschau a/b/c füllen: a mit Haken nach Vorgabe (nur mit Fremdspur), b ohne Haken (= Ausnahme merken), c nur Info.</summary>
    protected void VorschauFuellen(IReadOnlyList<Einordnung> funde, int luecke)
    {
        VorschauLeeren();
        if (funde.Count == 0)
        {
            vorschau.Children.Add(new TextBlock { Text = "Keine Fremdinhalte – nichts zu bereinigen.", Margin = new Thickness(0, 4, 0, 4) });
            return;
        }
        // Unvollständiger Verweisgraph (nicht lesbare Objekte): nichts zum Entfernen anbieten (Review 0.3.0).
        if (luecke > 0)
            vorschau.Children.Add(new TextBlock
            {
                Text = $"Achtung: {luecke} Objekte ließen sich nicht lesen – AVT kann nicht sicher sagen, was unbenutzt ist, und entfernt nichts. Ausnahmen merken geht.",
                TextWrapping = TextWrapping.Wrap, Foreground = Schema.Befund, Margin = new Thickness(0, 4, 0, 4), Tag = "Warnung",
            });
        Gruppenliste(funde, Gruppe.FremdUnbenutzt, "a) Fremd und unbenutzt – angehakt wird entfernt (vorab angehakt: mit Fremdspur)", luecke == 0);
        Gruppenliste(funde, Gruppe.FremdBenutzt, "b) Fremd und benutzt – bleibt; angehakt = als Ausnahme merken (Hinweis kommt nicht mehr)", true);
        Gruppenliste(funde, Gruppe.Projektinhalt, "c) Projektinhalt – nur Information", false);
        vorschauLeiste.Visibility = funde.Any(f => f.Gruppe != Gruppe.Projektinhalt) ? Visibility.Visible : Visibility.Collapsed;
    }

    void Gruppenliste(IReadOnlyList<Einordnung> alle, Gruppe gruppe, string titel, bool mitAuswahl)
    {
        var funde = alle.Where(f => f.Gruppe == gruppe).ToList();
        if (funde.Count == 0) return;
        var liste = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        foreach (var f in funde)
        {
            var text = $"{f.Kandidat.Art}: {f.Kandidat.Name} – {f.Grund}";
            if (mitAuswahl)
            {
                // Text als TextBlock: umbricht und zeigt Unterstriche (als Content-Text wäre „_“ ein Tastenkürzel).
                var box = new CheckBox
                {
                    Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = f.Gruppe == Gruppe.FremdUnbenutzt && f.Vorgabe,
                    ToolTip = f.Kandidat.Pfad, Margin = new Thickness(0, 1, 0, 1),
                };
                auswahl.Add((box, f));
                liste.Children.Add(box);
            }
            else liste.Children.Add(new TextBlock { Text = "• " + text, ToolTip = f.Kandidat.Pfad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
        }
        vorschau.Children.Add(new Expander
        {
            Header = new TextBlock { Text = $"{titel} ({funde.Count})", TextWrapping = TextWrapping.Wrap }, Content = liste, IsExpanded = gruppe != Gruppe.Projektinhalt, Margin = new Thickness(0, 2, 0, 2),
        });
    }
}
