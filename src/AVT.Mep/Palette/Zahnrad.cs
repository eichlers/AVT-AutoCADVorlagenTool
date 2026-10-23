using System.Windows;
using System.Windows.Controls;

namespace Avt.Mep.Palette;

/// <summary>Zahnrad der Palette (gemeinsame Regel): Version, Stand, Copyright, Eichler-Logo, Versionshistorie, Einstellungen.</summary>
internal static class Zahnrad
{
    public static void Zeigen()
    {
        var e = Einstellungen.Lies();
        var hinweis = new CheckBox
        {
            Content = "Beim Öffnen einer Zeichnung leise auf Fremdinhalte hinweisen", IsChecked = e.HinweisBeimOeffnen, Margin = new Thickness(0, 8, 0, 0),
        };
        hinweis.Checked += (_, _) => (Einstellungen.Lies() with { HinweisBeimOeffnen = true }).Schreibe();
        hinweis.Unchecked += (_, _) => (Einstellungen.Lies() with { HinweisBeimOeffnen = false }).Schreibe();

        var inhalt = new StackPanel { Margin = new Thickness(12) };
        if (Programminfo.Logo(36) is { } logo) inhalt.Children.Add(logo);
        inhalt.Children.Add(new TextBlock { Text = "AVT – Vorlagen-Tool", FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
        inhalt.Children.Add(new TextBlock { Text = $"Version {Programminfo.Version} · Stand {Programminfo.Datum}" });
        inhalt.Children.Add(new TextBlock { Text = Programminfo.Copyright });
        inhalt.Children.Add(new TextBlock { Text = "Einstellungen", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) });
        inhalt.Children.Add(hinweis);
        inhalt.Children.Add(new TextBlock { Text = "Versionshistorie", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });
        foreach (var (version, datum, text) in Programminfo.Historie)
            inhalt.Children.Add(new TextBlock { Text = $"{version} ({datum}): {text}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
        var fenster = new Window
        {
            Title = "AVT – Einstellungen und Version", Content = new ScrollViewer { Content = inhalt }, Width = 460, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.CanResizeWithGrip, ShowInTaskbar = false,
        };
        // Gleiches Farbschema wie die Palette.
        var farben = Farben.Aktuell();
        fenster.Background = farben.Grund;
        var huelle = new UserControl { Content = fenster.Content };
        fenster.Content = huelle;
        farben.Anwenden(huelle);
        try { fenster.Icon = Werkzeugkasten.WerkzeugkastenBefehl.SymbolBild(32); }
        catch (Exception) { /* Symbol ist Beiwerk */ }
        Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(fenster);
    }
}
