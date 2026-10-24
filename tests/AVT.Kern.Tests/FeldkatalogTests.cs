using Avt.Kern.Erzeugen;
using Xunit;

namespace Avt.Kern.Tests;

public class FeldkatalogTests
{
    static Tabelle T(params string[] zeilen) => Tabelle.Lies("test.csv", (IEnumerable<string>)zeilen);

    static readonly Tabelle Saetze = T("ID;Satz;Art;GiltFuer;Beschreibung", "PS-B;EHS_Bauteil;objekt;AecbDbSchematicSymbol;x", "PS-S;EHS_Stil;stil;AecbDbSchematicSymbolStyle;y");
    static readonly Tabelle Listen = T("Liste;Nr;Wert", "Status;2;Bestand", "Status;1;neu");

    static List<string> Fehler(params string[] felder) => Feldkatalog.Fehler(
        Feldkatalog.SaetzeLesen(Saetze),
        Feldkatalog.FelderLesen(T(["Satz;Feld;Anzeigename;Typ;Einheit;Liste;Vorgabe;Quelle;Pflicht", .. felder])),
        Feldkatalog.ListenLesen(Listen));

    [Fact]
    public void Listen_in_Reihenfolge_der_Nummer() => Assert.Equal(["neu", "Bestand"], Feldkatalog.ListenLesen(Listen)["Status"]);

    [Fact]
    public void Stilbasiert_wird_gelesen()
    {
        var s = Feldkatalog.SaetzeLesen(Saetze);
        Assert.False(s[0].Stilbasiert);
        Assert.True(s[1].Stilbasiert);
    }

    [Fact]
    public void Gueltiger_Katalog_ohne_Fehler() => Assert.Empty(Fehler(
        "EHS_Bauteil;Status;Status;Liste;;Status;neu;;nein",
        "EHS_Bauteil;Stilname;Stilname;Auto;;;;Stil;nein",
        "EHS_Bauteil;Benennung;Benennung;Formel;;;;RESULT = \"[Stilname]\";ja",
        "EHS_Stil;Bauteilart;B;Text;;;;;ja",
        "EHS_Bauteil;Art;Art;Formel;;;;RESULT = \"[EHS_Stil:Bauteilart]\";ja"));

    [Theory]
    [InlineData("EHS_Bauteil;Kennung_Alt;K;Text;;;;;ja", "nur Buchstaben")]       // Unterstrich (Feldkatalog 1.3)
    [InlineData("EHS_Bauteil;Größe;G;Text;;;;;ja", "nur Buchstaben")]             // Umlaut
    [InlineData("EHS_Bauteil;X;X;Zahl;;;;;ja", "Typ „Zahl“")]
    [InlineData("EHS_Bauteil;X;X;Liste;;Farbe;;;ja", "Liste „Farbe“ fehlt")]
    [InlineData("EHS_Bauteil;X;X;Liste;;Status;alt;;ja", "Vorgabe „alt“")]
    [InlineData("EHS_Bauteil;X;X;Auto;;;;;ja", "Quelle fehlt")]
    [InlineData("EHS_Bauteil;X;X;Formel;;;;RESULT = [Gibtsnicht];ja", "unbekanntes Feld")]
    [InlineData("TAGBauteil;X;X;Text;;;;;ja", "Satz fehlt")]
    public void Regelverstoss_wird_gemeldet(string zeile, string erwartet) =>
        Assert.Contains(Fehler(zeile), f => f.Contains(erwartet));

    [Fact]
    public void Doppeltes_Feld_wird_gemeldet() =>
        Assert.Contains(Fehler("EHS_Bauteil;A;A;Text;;;;;ja", "EHS_Bauteil;A;A;Text;;;;;ja"), f => f.Contains("doppelt"));

    [Theory]
    [InlineData("AecbDbSchematicStyle", "AecbDbSchematic")]
    [InlineData("AecbDbSchematicSymbolStyle", "AecbDbSchematicSymbol")]
    [InlineData("AecDbSpace", "AecDbSpace")]
    public void Objektklasse_zum_Stil(string klasse, string soll) => Assert.Equal(soll, Feldkatalog.Objektklasse(klasse));

    [Fact]
    public void Ansichtsfenster_werden_gelesen()
    {
        var f = Fenster.Lies("50.1,22.756,956.466,812.726,1:50 | 1066.518,286.683,1184,836,1:50");
        Assert.Equal(2, f.Count);
        Assert.Equal(0.02, f[0].Massstab, 9);
        Assert.Equal(1184, f[1].X2);
    }

    [Theory]
    [InlineData("1,2,3,4")]
    [InlineData("5,5,1,1,1:50")]
    [InlineData("1,1,5,5,50")]
    public void Ansichtsfenster_ungueltig(string text) => Assert.Throws<FormatException>(() => Fenster.Lies(text));

    [Fact]
    public void Vorschlag_der_Stammdaten_ist_gueltig()
    {
        const string ordner = @"C:\Temp\AVT\Stammdaten_Vorschlag";
        if (!File.Exists(Path.Combine(ordner, "Feldkatalog.csv"))) return;   // nur auf dem Bau-Rechner
        var fehler = Feldkatalog.Fehler(
            Feldkatalog.SaetzeLesen(Tabelle.Lies(Path.Combine(ordner, "Saetze.csv"), "ID", "Satz", "Art", "GiltFuer")),
            Feldkatalog.FelderLesen(Tabelle.Lies(Path.Combine(ordner, "Feldkatalog.csv"), "Satz", "Feld", "Typ")),
            Feldkatalog.ListenLesen(Tabelle.Lies(Path.Combine(ordner, "Wertelisten.csv"), "Liste", "Nr", "Wert")));
        Assert.Empty(fehler);
    }
}
