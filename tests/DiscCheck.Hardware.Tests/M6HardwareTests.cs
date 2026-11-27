using System.Globalization;
using DiscCheck.Core;
using DiscCheck.Scanners;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using FluentAssertions;
using Xunit.Abstractions;

namespace DiscCheck.Hardware.Tests;

/// <summary>
/// <c>[Fact]</c> for the M6 scan comparison: besides the iHAS124 and a disc, it needs the C1/C2 totals measured on the same
/// disc by QPxTool / Opti Drive Control (<see cref="M6Environment.ReferenceC1Variable"/>, <see cref="M6Environment.ReferenceC2Variable"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class M6ScanHardwareFactAttribute : FactAttribute
{
    public M6ScanHardwareFactAttribute()
    {
        Skip = LiteOnDrive.SkipReason(requireDisc: true) ?? M6Environment.SkipReason();
    }
}

/// <summary>Configuration of the M6 hardware tests, read from environment variables (documented in <c>docs/risks.md</c>).</summary>
public static class M6Environment
{
    /// <summary>Total C1 (BLER) of the whole disc measured by QPxTool / Opti Drive Control.</summary>
    public const string ReferenceC1Variable = "DISCCHECK_HW_REF_C1";

    /// <summary>Total C2 (E22) of the whole disc measured by QPxTool / Opti Drive Control.</summary>
    public const string ReferenceC2Variable = "DISCCHECK_HW_REF_C2";

    /// <summary>Optional scan speed in kB/s (1x = 176). Default 704 (4x).</summary>
    public const string SpeedVariable = "DISCCHECK_HW_SCAN_SPEED";

    public const int DefaultSpeedKBps = 704;

    /// <summary>Relative tolerance on the C1 total (Requirement 27.7).</summary>
    public const double C1Tolerance = 0.10;

    public static long? ReferenceC1 => ParseNonNegative(ReferenceC1Variable);

    public static long? ReferenceC2 => ParseNonNegative(ReferenceC2Variable);

    public static int SpeedKBps =>
        int.TryParse(Environment.GetEnvironmentVariable(SpeedVariable), NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v > 0
            ? v : DefaultSpeedKBps;

    private static long? ParseNonNegative(string name) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out long v) ? v : null;

    public static string? SkipReason()
    {
        if (ReferenceC1 is null)
            return $"Hardware test: variabile d'ambiente {ReferenceC1Variable} non impostata o non numerica (totale C1 di QPxTool/Opti Drive Control).";
        if (ReferenceC2 is null)
            return $"Hardware test: variabile d'ambiente {ReferenceC2Variable} non impostata o non numerica (totale C2 di QPxTool/Opti Drive Control).";
        return null;
    }
}

/// <summary>
/// M6 hardware tests on a Lite-On iHAS124 behind an ICY BOX USB bridge: full scan with <see cref="LiteOnScanner"/> compared with
/// the totals of QPxTool / Opti Drive Control, and Test_Mode closed at the end. Excluded from the default run
/// (<c>dotnet test --filter "Category!=Hardware"</c>); skipped when the drive, the disc or the reference totals are missing.
/// Requirements 23.7, 27.7, 27.11.
/// </summary>
[Trait("Category", "Hardware")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class M6HardwareTests(ITestOutputHelper output)
{
    [M6ScanHardwareFact]
    public async Task Full_scan_Metodo_Nuovo_matches_reference_totals_and_closes_test_mode()
        => await RunScanAndCompare(LiteOnMethod.New);

    [M6ScanHardwareFact]
    public async Task Full_scan_Metodo_Vecchio_matches_reference_totals_and_closes_test_mode()
        => await RunScanAndCompare(LiteOnMethod.Old);

    private async Task RunScanAndCompare(LiteOnMethod method)
    {
        var drive = LiteOnDrive.Find()!;
        var toc = ReadToc(drive.Letter);
        var scanner = new LiteOnScanner(new WinScsiTransportFactory());
        var options = new ScanOptions(drive.Letter, M6Environment.SpeedKBps, method, toc);

        scanner.Probe(drive).Supported.Should().BeTrue(because: "the drive is a Lite-On");

        var samples = new List<ScanSample>();
        Exception? failure = null;
        try
        {
            await foreach (var s in scanner.ScanAsync(options,
                               new Progress<ScanProgress>(p => output.WriteLine($"LBA {p.Lba}: {p.Percent:F0}%")),
                               CancellationToken.None))
                samples.Add(s);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        // Test_Mode must be closed whatever happened: the drive answers TEST UNIT READY after the scan.
        using (var transport = new WinScsiPassThrough(drive.Letter))
        {
            var tur = MmcCommands.TestUnitReady(transport);
            output.WriteLine($"TEST UNIT READY dopo la scansione: {(tur.IsSuccess ? "OK" : ScsiErrorMessages.Describe(tur.Error!))}");
            tur.IsSuccess.Should().BeTrue(because: "Test_Mode must be closed at the end of the scan (Requirement 23.7)");
        }

        failure.Should().BeNull(because: "the scan must run to the end: {0}", failure?.Message);
        failure.Should().NotBeOfType<TestModeEndFailedException>();

        long c1 = samples.Sum(s => (long)(s.C1 ?? 0));
        long c2 = samples.Sum(s => (long)s.C2);
        long refC1 = M6Environment.ReferenceC1!.Value;
        long refC2 = M6Environment.ReferenceC2!.Value;
        output.WriteLine($"Metodo {method}: campioni={samples.Count}, C1={c1} (rif. {refC1}), C2={c2} (rif. {refC2})");

        samples.Should().NotBeEmpty();
        samples.Should().OnlyContain(s => s.C2 >= 0 && (s.C1 ?? 0) >= 0);

        double tolerance = Math.Max(1.0, refC1 * M6Environment.C1Tolerance);
        ((double)c1).Should().BeInRange(refC1 - tolerance, refC1 + tolerance,
            because: $"C1 total must be within ±{M6Environment.C1Tolerance:P0} of QPxTool/Opti Drive Control (Requirement 27.7)");
        c2.Should().Be(refC2, because: "C2 total must equal the reference (Requirement 27.7)");
    }

    private static Toc ReadToc(char letter)
    {
        using var transport = new WinScsiPassThrough(letter);
        var raw = MmcCommands.ReadToc(transport, format: 0);
        raw.IsSuccess.Should().BeTrue(because: "READ TOC must succeed: {0}", raw.Error);
        TocParser.TryParse(raw.Value, out var toc, out var error).Should().BeTrue(because: "TOC must parse: {0}", error);
        return toc!;
    }
}
