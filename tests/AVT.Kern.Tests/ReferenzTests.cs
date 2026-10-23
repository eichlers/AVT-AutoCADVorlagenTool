using Avt.Kern.Fremd;
using Xunit;

namespace Avt.Kern.Tests;

public class ReferenzTests
{
    [Fact]
    public void Eingebettete_Live_Fassung_ist_da_und_enthaelt_Kerneintraege()
    {
        var r = Referenz.Eingebettet(Referenz.LiveFassung);
        Assert.NotNull(r);
        Assert.True(r!.Pfade.Count > 2000);
        Assert.Contains("NOD/AEC_PROPERTY_SET_DEFS/ANA_Hydraulik", r.Pfade);
        Assert.Contains("AcDbLayerTable#0/0", r.Pfade);
        Assert.DoesNotContain(r.Pfade, p => p.StartsWith("#", StringComparison.Ordinal));
    }

    [Fact]
    public void Ohne_Fassung_gilt_die_Live_Fassung() => Assert.Equal(Referenz.LiveFassung, Referenz.Fuer(null, null).Fassung);

    [Fact]
    public void Datei_neben_der_Fassung_geht_vor()
    {
        var ordner = Directory.CreateTempSubdirectory("avt_ref_").FullName;
        try
        {
            File.WriteAllText(Path.Combine(ordner, "EHS_9.9.9.referenz.txt"), "# Kopf\nAcDbLayerTable#0/X\n");
            var r = Referenz.Fuer("EHS_9.9.9", ordner);
            Assert.Equal("EHS_9.9.9", r.Fassung);
            Assert.Single(r.Pfade);
        }
        finally { Directory.Delete(ordner, true); }
    }

    [Fact]
    public void Leere_Referenz_ist_ein_Fehler() =>
        Assert.Throws<InvalidDataException>(() => Referenz.Lies("X", new StringReader("# nur Kopf\n"), "Test"));
}

public class SicherungTests
{
    [Fact]
    public void Sicherungskopie_liegt_daneben_und_ist_gleich()
    {
        var ordner = Directory.CreateTempSubdirectory("avt_sich_").FullName;
        try
        {
            var datei = Path.Combine(ordner, "Plan.dwg");
            File.WriteAllBytes(datei, [1, 2, 3, 4, 5]);
            var zeit = new DateTime(2026, 10, 23, 14, 5, 0);
            var k1 = Fremdbereiniger.Sicherung(datei, zeit);
            var k2 = Fremdbereiniger.Sicherung(datei, zeit);
            Assert.Equal(Path.Combine(ordner, "Plan_vor-Bereinigen_20261023_1405.dwg"), k1);
            Assert.Equal(Path.Combine(ordner, "Plan_vor-Bereinigen_20261023_1405_2.dwg"), k2);
            Assert.Equal(File.ReadAllBytes(datei), File.ReadAllBytes(k1));
        }
        finally { Directory.Delete(ordner, true); }
    }
}
