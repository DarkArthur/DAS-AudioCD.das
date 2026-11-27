using System.Globalization;
using DiscCheck.Core;

namespace DiscCheck.Core.Tests;

public class CsvTests
{
    [Fact]
    public void Export_uses_header_crlf_empty_nulls_and_invariant_decimal()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("it-IT");
        try
        {
            var csv = CsvExporter.Export([new ScanSample(0, 1, null, 2, null, 1.5), new ScanSample(5, 2, 3, 4, 7, null)]);
            Assert.Equal("TimeSeconds,C1,C2,CU,SpeedX\r\n1,,2,,1.5\r\n2,3,4,7,\r\n", csv);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Write_emits_utf8_without_bom()
    {
        using var ms = new MemoryStream();
        CsvExporter.Write(ms, []);
        Assert.Equal("TimeSeconds,C1,C2,CU,SpeedX\r\n"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public void Import_inverts_export()
    {
        var samples = new[] { new ScanSample(0, 1, null, 2, null, 1.25), new ScanSample(0, 2, 3, 4, 7, null) };
        Assert.Equal(samples, CsvImporter.Import(CsvExporter.Export(samples)));
    }

    [Fact]
    public void Import_rejects_bad_header()
        => Assert.Throws<FormatException>(() => CsvImporter.Import("a,b\r\n"));
}
