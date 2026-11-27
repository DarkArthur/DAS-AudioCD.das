using System.Globalization;
using DiscCheck.Codecs.Flake;
using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using DiscCheck.Verify;
using FluentAssertions;
using Xunit.Abstractions;

namespace DiscCheck.Hardware.Tests;

/// <summary>
/// <c>[Fact]</c> for the M5 bit-perfect test: besides the iHAS124 and a disc, it needs the path of the Sorgente of a known disc
/// (<see cref="M5Environment.SourceVariable"/>) and the offset to use (<see cref="M5Environment.OffsetVariable"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class M5BitPerfectHardwareFactAttribute : FactAttribute
{
    public M5BitPerfectHardwareFactAttribute()
    {
        Skip = LiteOnDrive.SkipReason(requireDisc: true) ?? M5Environment.BitPerfectSkipReason();
    }
}

/// <summary>Configuration of the M5 hardware tests, read from environment variables (documented in <c>docs/risks.md</c>).</summary>
public static class M5Environment
{
    /// <summary>File or folder of the Sorgente (FLAC/WAV, one file per track) of the disc inserted in the drive.</summary>
    public const string SourceVariable = "DISCCHECK_HW_SOURCE";

    /// <summary>Offset in samples (-5000..5000) to apply: the Combined_Offset of the drive/disc pair.</summary>
    public const string OffsetVariable = "DISCCHECK_HW_OFFSET";

    public static string? SourcePath => Environment.GetEnvironmentVariable(SourceVariable);

    public static int? Offset =>
        int.TryParse(Environment.GetEnvironmentVariable(OffsetVariable), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out int v) && Math.Abs(v) <= OffsetDetector.MaxOffset ? v : null;

    public static string? BitPerfectSkipReason()
    {
        var path = SourcePath;
        if (string.IsNullOrWhiteSpace(path))
            return $"Hardware test: variabile d'ambiente {SourceVariable} non impostata (percorso della Sorgente del disco noto).";
        if (!File.Exists(path) && !Directory.Exists(path))
            return $"Hardware test: la Sorgente indicata da {SourceVariable} non esiste: {path}";
        if (Offset is null)
            return $"Hardware test: variabile d'ambiente {OffsetVariable} non impostata o fuori da ±{OffsetDetector.MaxOffset} (offset in campioni).";
        return null;
    }
}

/// <summary>
/// M5 hardware tests on a Lite-On iHAS124 behind an ICY BOX USB bridge: cache detection and bit-perfect verification of a
/// known disc. Excluded from the default run (<c>dotnet test --filter "Category!=Hardware"</c>); skipped when the drive,
/// the disc or the Sorgente is missing. Requirements 12.10, 27.6, 27.11.
/// </summary>
[Trait("Category", "Hardware")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class M5HardwareTests(ITestOutputHelper output)
{
    /// <summary>Needs an audio CD of at least 28.500 sectors (Requirement 12.1).</summary>
    [LiteOnHardwareFact(requireDisc: true)]
    public void Cache_detection_on_iHAS124_matches_2048_KB_reference()
    {
        var drive = LiteOnDrive.Find()!;
        var toc = ReadToc(drive.Letter);
        var repo = new CapturingDriveRepository();
        var runner = new CacheDetectorRunner(new WinScsiTransportFactory(), repo);

        var result = runner.Run(drive.Letter, ToRecord(drive), toc);

        output.WriteLine($"Cache: completed={result.Completed}, caches={result.CachesAudio}, cache_kb={result.CacheKb}, " +
                         $"T1={result.MedianT1Ms:F2} ms, T2={result.MedianT2Ms:F2} ms (riferimento {CacheDetector.Ihas124ReferenceKb} KB)");
        foreach (var w in result.Warnings) output.WriteLine($"Avviso: {w}");

        result.Completed.Should().BeTrue(because: "detection must not abort: {0}", result.AbortReason);
        result.CachesAudio.Should().BeTrue(because: "the iHAS124 caches audio reads");
        result.CacheKb.Should().BeInRange(CacheDetector.Ihas124MinKb, CacheDetector.Ihas124MaxKb,
            because: $"the estimate must be close to the {CacheDetector.Ihas124ReferenceKb} KB reference (Requirement 12.10)");
        result.Warnings.Should().BeEmpty();
        repo.Saved.Should().ContainSingle(because: "a completed detection is saved");
    }

    /// <summary>
    /// Verifies the disc in the drive against its Sorgente. The disc must be a known one whose Sorgente is a bit-perfect rip
    /// (path in <c>DISCCHECK_HW_SOURCE</c>, offset in <c>DISCCHECK_HW_OFFSET</c>).
    /// </summary>
    [M5BitPerfectHardwareFact]
    public void Bit_perfect_verification_of_known_disc_is_identical()
    {
        var drive = LiteOnDrive.Find()!;
        var toc = ReadToc(drive.Letter);
        var factory = new WinScsiTransportFactory();

        // Same flow as the app: cache detection first, so that the Cache_Defeat uses the real cache size.
        var record = ToRecord(drive);
        var detection = new CacheDetectorRunner(factory, new CapturingDriveRepository()).Run(drive.Letter, record, toc);
        output.WriteLine($"Cache: completed={detection.Completed}, cache_kb={detection.CacheKb}");
        if (detection.Completed)
            record = record with { CachesAudio = detection.CachesAudio, CacheKb = detection.CacheKb };

        var verifier = new Verifier(factory, new SourceReader(new FlakeDecoder()));
        var options = new VerifyOptions(drive.Letter, record, toc, M5Environment.SourcePath!, M5Environment.Offset!.Value);

        var report = verifier.Run(options, new Progress<VerifyProgress>(p =>
            output.WriteLine($"Traccia {p.TrackNo} ({p.TrackIndex + 1}/{p.TrackCount}): {p.Percent:F0}%")), CancellationToken.None);

        output.WriteLine($"Esito: status={report.Status}, result={report.Result}, offset={report.OffsetUsed}, " +
                         $"cache_defeat_limited={report.CacheDefeatLimited}, messaggio={report.Message}");
        foreach (var t in report.Tracks)
            output.WriteLine($"  traccia {t.TrackNo}: identica={t.Identical}, campioni diversi={t.DifferentSamples}, " +
                             $"esclusi={t.ExcludedEdgeSamples}, CRC32={t.CrcRead:X8}/{t.CrcSource:X8}");

        report.Status.Should().Be(VerifyStatus.Completed, because: "verification must run to the end: {0}", report.Message);
        report.IsDiscIdentical.Should().BeTrue(because: "the known disc must be bit-perfect against its Sorgente");
        report.Tracks.Should().OnlyContain(t => t.Identical);
    }

    private static Toc ReadToc(char letter)
    {
        using var transport = new WinScsiPassThrough(letter);
        var raw = MmcCommands.ReadToc(transport, format: 0);
        raw.IsSuccess.Should().BeTrue(because: "READ TOC must succeed: {0}", raw.Error);
        TocParser.TryParse(raw.Value, out var toc, out var error).Should().BeTrue(because: "TOC must parse: {0}", error);
        return toc!;
    }

    private static DriveRecord ToRecord(DriveInfoEx d) =>
        new(0, d.Vendor.Trim(), d.Model.Trim(), d.Firmware.Trim(), d.C2Supported, d.ReadOffset, d.CachesAudio, d.CacheKb, d.C2Reliable);

    /// <summary>In-memory repository that records what the runner saves, so the tests never touch the real database.</summary>
    private sealed class CapturingDriveRepository : IDriveRepository
    {
        public List<DriveRecord> Saved { get; } = [];

        public long AddIfMissing(DriveRecord drive)
        {
            Saved.Add(drive);
            return Saved.Count;
        }

        public DriveRecord? Get(long id) => null;
        public IReadOnlyList<DriveRecord> List() => Saved;
    }
}
