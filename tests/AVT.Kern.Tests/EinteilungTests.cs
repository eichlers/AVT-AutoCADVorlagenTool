using Avt.Kern.Fremd;
using Xunit;

namespace Avt.Kern.Tests;

public class EinteilungTests
{
    static Kandidat K(string pfad, string art, string name, string[]? benutzer = null, string[]? spuren = null) =>
        new(pfad, art, name, benutzer ?? [], spuren ?? []);

    static readonly HashSet<string> Leer = [];

    static Gruppe GruppeVon(List<Einordnung> e, string pfad) => e.Single(x => x.Kandidat.Pfad == pfad).Gruppe;

    [Fact]
    public void Unbenutzter_Block_ohne_Spur_ist_a_aber_nicht_angehakt()
    {
        var e = Einteilung.Einordnen([K("B/Alt", "Block", "Alt")], Leer, Leer);
        Assert.Equal(Gruppe.FremdUnbenutzt, GruppeVon(e, "B/Alt"));
        Assert.False(e.Single().Vorgabe);
    }

    [Fact]
    public void Unbenutzter_Block_mit_Spur_ist_a_und_angehakt()
    {
        var e = Einteilung.Einordnen([K("B/TAG1", "Block", "TAG1", spuren: ["alter Stempelblock"])], Leer, Leer);
        Assert.True(e.Single().Vorgabe);
    }

    [Fact]
    public void Unbenutzte_fremde_Satzdefinition_ist_angehakt()
    {
        var e = Einteilung.Einordnen([K("NOD/AEC_PROPERTY_SET_DEFS/Alt", "Satzdefinition", "Alt")], Leer, Leer);
        Assert.True(e.Single().Vorgabe);
    }

    [Fact]
    public void Benutzter_Block_ohne_Spur_ist_Projektinhalt()
    {
        var e = Einteilung.Einordnen([K("B/Plankopf", "Block", "Plankopf", ["Modell/Ref#0"])], Leer, Leer);
        Assert.Equal(Gruppe.Projektinhalt, GruppeVon(e, "B/Plankopf"));
    }

    [Fact]
    public void Benutzte_fremde_Satzdefinition_ist_b()
    {
        var e = Einteilung.Einordnen([K("NOD/AEC_PROPERTY_SET_DEFS/TAGStrömung", "Satzdefinition", "TAGStrömung", ["Satz#1"])], Leer, Leer);
        Assert.Equal(Gruppe.FremdBenutzt, GruppeVon(e, "NOD/AEC_PROPERTY_SET_DEFS/TAGStrömung"));
    }

    [Fact]
    public void Benutzter_Block_mit_Spur_ist_b()
    {
        var e = Einteilung.Einordnen([K("B/TAG1", "Block", "TAG1", ["Modell/Ref#0"], ["alter Stempelblock"])], Leer, Leer);
        Assert.Equal(Gruppe.FremdBenutzt, GruppeVon(e, "B/TAG1"));
    }

    [Fact]
    public void Was_zur_Referenz_gehoert_wird_nicht_gemeldet()
    {
        var e = Einteilung.Einordnen([K("L/Z_Schema", "Layer", "Z_Schema")], new HashSet<string> { "L/Z_Schema" }, Leer);
        Assert.Empty(e);
    }

    [Fact]
    public void Ausnahme_wird_nicht_gemeldet()
    {
        var e = Einteilung.Einordnen([K("B/Alt", "Block", "Alt")], Leer, new HashSet<string> { "B/Alt" });
        Assert.Empty(e);
    }

    [Theory]
    [InlineData("RegApp", "ACAD")]
    [InlineData("RegApp", "AEC_OBJVER_INFO")]
    [InlineData("Wörterbuch", "ACAD_GROUP")]
    [InlineData("Wörterbuch", "AEC_VARS")]
    [InlineData("Satzdefinition", "ANA_Hydraulik")]
    [InlineData("Layer", "0")]
    [InlineData("Textstil", "Standard")]
    [InlineData("Wörterbuch", "EHS_Zeichnung")]
    [InlineData("Wörterbuch", "AVT_Ausnahmen")]
    [InlineData("Wörterbuch", "AQT_Daten")]
    public void Geschuetzte_Namen_werden_nie_gemeldet(string art, string name) => Assert.True(Einteilung.IstGeschuetzt(art, name));

    [Theory]
    [InlineData("Wörterbuch", "LIN_MEDIEN_DICT")]
    [InlineData("RegApp", "LINEAR_SPEZIALANSCHLUSS")]
    [InlineData("Satzdefinition", "TAGStrömung")]
    [InlineData("Satzdefinition", "TAGBauteil")]
    public void Fremde_Namen_sind_nicht_geschuetzt(string art, string name) => Assert.False(Einteilung.IstGeschuetzt(art, name));

    [Fact]
    public void Nur_von_Unbenutztem_benutzt_ist_auch_unbenutzt()
    {
        // Block „Innen“ steckt nur im unbenutzten Block „Aussen“ (Benutzer ist die Einfügung IN „Aussen“) – beide a.
        var e = Einteilung.Einordnen([K("B/Aussen", "Block", "Aussen"), K("B/Innen", "Block", "Innen", ["B/Aussen/AcDbBlockReference#0"])], Leer, Leer);
        Assert.Equal(Gruppe.FremdUnbenutzt, GruppeVon(e, "B/Innen"));
    }

    [Fact]
    public void Benutzer_im_eigenen_Inhalt_zaehlt_nicht()
    {
        // Ein Wörterbuch, auf das nur eigene Einträge zeigen, ist unbenutzt.
        var e = Einteilung.Einordnen([K("NOD/X_DICT", "Wörterbuch", "X_DICT", ["NOD/X_DICT/Eintrag"])], Leer, Leer);
        Assert.Equal(Gruppe.FremdUnbenutzt, GruppeVon(e, "NOD/X_DICT"));
    }

    [Theory]
    [InlineData("ANA_Hydraulik (2)", "ANA_Hydraulik")]
    [InlineData("Pumpe (10)", "Pumpe")]
    [InlineData("Pumpe", null)]
    [InlineData("Pumpe(2)", null)]
    public void Dubletten_werden_erkannt(string name, string? basis) => Assert.Equal(basis, Einteilung.DublettenBasis(name));

    [Theory]
    [InlineData("linMediumRecord", true)]
    [InlineData("AcDbDictionary", false)]
    [InlineData("AecbDbSchematicSymbolStyle", false)]
    [InlineData("AcAecBinRecord", false)]
    [InlineData("AbsToolsDbHangerStyle", false)]
    [InlineData("vlo_VL", false)]
    public void Fremdklassen_werden_erkannt(string klasse, bool fremd) => Assert.Equal(fremd, Einteilung.IstFremdklasse(klasse));

    [Fact]
    public void Kurzfassung_zaehlt_a_und_b()
    {
        var e = Einteilung.Einordnen([K("B/A", "Block", "A"), K("B/B", "Block", "B"),
            K("NOD/AEC_PROPERTY_SET_DEFS/TAGStrömung", "Satzdefinition", "TAGStrömung", ["S#1"]), K("B/P", "Block", "P", ["M#1"])], Leer, Leer);
        Assert.Equal("3 Fremdinhalte, 1 benutzt", Einteilung.Kurzfassung(e));
        Assert.Null(Einteilung.Kurzfassung([]));
    }

    [Fact]
    public void Altnamen_nur_bei_Bloecken()
    {
        Assert.True(Einteilung.IstAltname("Block", "TAG_Pumpe"));
        Assert.True(Einteilung.IstAltname("Block", "eaklLeg01"));
        Assert.False(Einteilung.IstAltname("Layer", "TAG"));
    }

    [Fact]
    public void Fund_in_einem_Fund_wird_nicht_doppelt_gemeldet()
    {
        var e = Einteilung.Einordnen([K("NOD/X_DICT", "Wörterbuch", "X_DICT"), K("NOD/X_DICT/Eintrag", "Fremdobjekt", "Eintrag")], Leer, Leer);
        Assert.Single(e);
        Assert.Equal("NOD/X_DICT", e[0].Kandidat.Pfad);
    }
}
