using System.Globalization;
using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class CsvRoundTripPropertyTests
{
    // Feature: disccheck, Property 14: Round-trip CSV
    // **Validates: Requirements 21.10**
    [Property(MaxTest = 100)]
    public bool CsvExportThenImport_PreservesRowsAndValues(int[] seeds)
    {
        var samples = Build(seeds ?? []);

        var previous = CultureInfo.CurrentCulture;
        try
        {
            // The export must be culture-invariant even when the current culture uses a decimal comma.
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            var imported = CsvImporter.Import(CsvExporter.Export(samples));

            if (imported.Count != samples.Count) return false;
            for (int i = 0; i < samples.Count; i++)
            {
                var a = samples[i];
                var b = imported[i];
                if (a.TimeSeconds != b.TimeSeconds || a.C1 != b.C1 || a.C2 != b.C2
                    || a.CU != b.CU || a.SpeedX != b.SpeedX)
                    return false;
            }
            return true;
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static List<ScanSample> Build(int[] seeds)
    {
        var list = new List<ScanSample>();
        for (int i = 0; i + 4 < seeds.Length; i += 5)
        {
            int s0 = seeds[i], s1 = seeds[i + 1], s2 = seeds[i + 2], s3 = seeds[i + 3], s4 = seeds[i + 4];
            int? c1 = (s0 & 1) == 0 ? null : Math.Abs(s1 % 100000);
            int? cu = (s0 & 2) == 0 ? null : Math.Abs(s3 % 100000);
            double? speed = (s0 & 4) == 0
                ? null
                : s4 / 7.0 * Math.Pow(10, (s2 % 6)); // fractional values, exercises decimal separator
            list.Add(new ScanSample(0, Math.Abs(s0 % 100000), c1, Math.Abs(s2 % 100000), cu, speed));
        }
        return list;
    }
}
