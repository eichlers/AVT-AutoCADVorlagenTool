using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Avt.Mep.Palette;

/// <summary>
/// Corporate Design aller Modul-Paletten (gemeinsame Regel AMK „Paletten sehen bei allen Modulen gleich aus“, Vorbild ATT):
/// Farben passend zum AutoCAD-Farbschema (COLORTHEME 0 = dunkel, 1 = hell) – dieselben Werte wie ATT.Mep.Palette.Farben
/// (ADM hat sie ebenso 1:1 übernommen).
/// </summary>
internal sealed record Farben(Brush Grund, Brush Text, Brush Leise, Brush Befund, Brush Gut, Brush Null,
                              Brush Zeile, Brush ZeileWechsel, Brush Kopf, Brush Rand, Brush Markiert)
{
    static Brush F(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    public static readonly Farben Dunkel = new(F("#FF3B4453"), F("#FFF0F0F0"), F("#FFB8C0CC"), F("#FFFF8A80"), F("#FF8FD18F"), F("#FF9AA3AE"),
                                               F("#FF333B48"), F("#FF38414F"), F("#FF4A5566"), F("#FF56627A"), F("#FF2F6DB5"));
    public static readonly Farben Hell = new(F("#FFF5F5F5"), F("#FF1E1E1E"), F("#FF606060"), F("#FFB22222"), F("#FF1E7B1E"), F("#FF808080"),
                                             F("#FFFFFFFF"), F("#FFF3F6FA"), F("#FFE4E8EE"), F("#FFC8CED8"), F("#FFCCE0F7"));

    /// <summary>Schema aus AutoCAD (nur im Plugin aufrufen – braucht AutoCAD).</summary>
    public static Farben Aktuell()
    {
        try { return Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("COLORTHEME")) == 0 ? Dunkel : Hell; }
        catch { return Hell; }
    }

    /// <summary>
    /// Grund, Schrift und Stile für ein Element und alles darin: Texte, Kontrollkästchen, Ausklapper und Beschriftungen in
    /// Textfarbe, Eingabe-/Berichtsfelder in Zeilenfarbe mit Rand – wie die ATT-Palette. Knöpfe bleiben in Windows-Optik (wie ATT).
    /// Erneut aufrufen beim Umschalten von COLORTHEME.
    /// </summary>
    public void Anwenden(Control wurzel)
    {
        wurzel.Background = Grund;
        wurzel.Foreground = Text;
        var r = new ResourceDictionary();
        Stil<TextBlock>(r, (TextBlock.ForegroundProperty, Text));
        Stil<CheckBox>(r, (Control.ForegroundProperty, Text));
        Stil<RadioButton>(r, (Control.ForegroundProperty, Text));
        Stil<Expander>(r, (Control.ForegroundProperty, Text));
        Stil<Label>(r, (Control.ForegroundProperty, Text));
        Stil<TextBox>(r, (Control.BackgroundProperty, Zeile), (Control.ForegroundProperty, Text), (Control.BorderBrushProperty, Rand),
            (TextBoxBase.CaretBrushProperty, Text), (TextBoxBase.SelectionBrushProperty, Markiert));
        Stil<ListBox>(r, (Control.BackgroundProperty, Zeile), (Control.ForegroundProperty, Text), (Control.BorderBrushProperty, Rand));
        Stil<ToolTip>(r, (Control.BackgroundProperty, Zeile), (Control.ForegroundProperty, Text), (Control.BorderBrushProperty, Rand));
        wurzel.Resources = r;
    }

    static void Stil<T>(ResourceDictionary r, params (DependencyProperty Eigenschaft, object Wert)[] werte)
    {
        var s = new Style(typeof(T));
        foreach (var (e, w) in werte) s.Setters.Add(new Setter(e, w));
        r[typeof(T)] = s;
    }
}

/// <summary>Kurzname für TextBoxBase (Caret- und Markierfarbe).</summary>
file static class TextBoxBase
{
    public static readonly DependencyProperty CaretBrushProperty = System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty;
    public static readonly DependencyProperty SelectionBrushProperty = System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty;
}
