using Avt.Kern.Erzeugen;
using Xunit;

namespace Avt.Kern.Tests;

public class StammdatenTests
{
    [Theory]
    [InlineData("ACI 40", false, 40)]
    [InlineData("aci 7", false, 7)]
    public void Aci_wird_gelesen(string text, bool rgb, short index)
    {
        var f = Farbe.Lies(text);
        Assert.Equal(rgb, f.Rgb);
        Assert.Equal(index, f.Index);
    }

    [Fact]
    public void Rgb_wird_gelesen()
    {
        var f = Farbe.Lies("RGB 181,203,11");
        Assert.True(f.Rgb);
        Assert.Equal((181, 203, 11), (f.R, f.G, f.B));
        Assert.Equal("RGB 181,203,11", f.ToString());
    }

    [Theory]
    [InlineData("rot")]
    [InlineData("ACI 300")]
    [InlineData("RGB 1,2")]
    public void Falsche_Farbe_ist_Fehler(string text) => Assert.Throws<FormatException>(() => Farbe.Lies(text));

    [Theory]
    [InlineData("0.30 mm", 30)]
    [InlineData("0,15 mm", 15)]
    [InlineData("0.25mm", 25)]
    public void Linienstaerke_wird_gelesen(string text, int wert) => Assert.Equal(wert, Stammwerte.Linienstaerke(text));

    [Theory]
    [InlineData("0.31 mm")]
    [InlineData("dick")]
    public void Falsche_Linienstaerke_ist_Fehler(string text) => Assert.Throws<FormatException>(() => Stammwerte.Linienstaerke(text));

    [Theory]
    [InlineData("NOD/AEC_PROPERTY_SET_DEFS/*", "NOD/AEC_PROPERTY_SET_DEFS/Raumobjekte", true)]
    [InlineData("NOD/AEC_PROPERTY_SET_DEFS/*", "NOD/AEC_PROPERTY_SET_DEFS/Raumobjekte/AcDbDictionary#0", false)]
    [InlineData("NOD/AEC_PROPERTY_SET_DEFS/*", "NOD/AEC_LIST_DEFINITIONS/Standard", false)]
    [InlineData("NOD/AEC_LIST_DEFINITIONS/Standard", "NOD/AEC_LIST_DEFINITIONS/Standard", true)]
    public void Muster_passen(string muster, string pfad, bool passt) => Assert.Equal(passt, Stammwerte.Passt(muster, pfad));

    [Fact]
    public void Platzhalter_werden_gefuellt()
    {
        Assert.Equal("EHS_S_ETA", Stammwerte.Fuelle("EHS_S_{Kuerzel}", new Dictionary<string, string> { ["Kuerzel"] = "ETA" }));
        Assert.Throws<FormatException>(() => Stammwerte.Fuelle("{X}", new Dictionary<string, string>()));
    }

    [Fact]
    public void Tabelle_liest_Kommentare_Kopf_und_Zeilen()
    {
        var t = Tabelle.Lies("T.csv", ["﻿# Kommentar", "ID;Name;Wert", "A;Eins;1", "", "# noch einer", "B;Zwei;"], "ID", "Name");
        Assert.Equal(2, t.Zeilen.Count);
        Assert.Equal("Zwei", t.Zeilen[1].Werte["Name"]);
        Assert.Equal("", t.Zeilen[1].Werte["Wert"]);
        Assert.Equal(6, t.Zeilen[1].Zeile);
    }

    [Fact]
    public void Tabelle_meldet_fehlende_Spalte_und_leere_Pflicht()
    {
        Assert.Throws<FormatException>(() => Tabelle.Lies("T.csv", ["ID;Name", "A;B"], "ID", "Farbe"));
        var e = Assert.Throws<FormatException>(() => Tabelle.Lies("T.csv", ["ID;Name", "A;"], "ID", "Name"));
        Assert.Contains("Zeile 2", e.Message);
    }

    /// <summary>Die Systeme im Schema-Ordner (Eigentümer AMK) sind lesbar: Farben und Linienstärken gültig, Kürzel eindeutig.</summary>
    [Fact]
    public void Systeme_im_Schema_sind_lesbar()
    {
        var datei = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"Meine Ablage\Vorlagen\CAD\CADStandardEichlerGmbH\Schema\Stammdaten\Systeme.csv");
        if (!File.Exists(datei)) return;   // nur auf dem Arbeitsrechner vorhanden
        var sys = Tabelle.Lies(datei, "Kuerzel", "Benennung", "Gruppe", "Farbe", "Linientyp", "Linienstaerke");
        Assert.True(sys.Zeilen.Count >= 90);
        foreach (var (_, w) in sys.Zeilen)
        {
            Farbe.Lies(w["Farbe"]);
            Stammwerte.Linienstaerke(w["Linienstaerke"]);
        }
        Assert.Equal(sys.Zeilen.Count, sys.Zeilen.Select(z => z.Werte["Kuerzel"]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
