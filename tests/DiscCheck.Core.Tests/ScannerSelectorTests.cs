using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

public class ScannerSelectorTests
{
    private static readonly DriveInfoEx Drive = new('E', "LITE-ON", "iHAS124", "1", true, [], 0, null, null, null, null);

    private sealed class FakeScanner(string name, Func<ScannerCapabilities> probe) : IQualityScanner
    {
        public int ProbeCalls;
        public string Name => name;
        public ScannerCapabilities Probe(DriveInfoEx drive) { ProbeCalls++; return probe(); }
        public IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions o, IProgress<ScanProgress> p, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static ScannerCapabilities Yes => new(true, true, true, true, null);
    private static ScannerCapabilities No(string r = "no") => new(false, false, false, false, r);

    [Fact]
    public void LiteOn_IsPreferred_AndLaterScannersAreNotProbed()
    {
        var std = new FakeScanner("StandardC2", () => Yes);
        var lite = new FakeScanner("LiteOn", () => Yes);
        var sel = new ScannerSelector([std, lite]).Select(Drive);
        sel.Scanner.Should().BeSameAs(lite);
        std.ProbeCalls.Should().Be(0);
    }

    [Fact]
    public void FallsBackToStandard_WhenLiteOnThrows()
    {
        var logs = new List<string>();
        var lite = new FakeScanner("LiteOn", () => throw new InvalidOperationException("boom"));
        var std = new FakeScanner("StandardC2", () => Yes);
        var sel = new ScannerSelector([lite, std], logs.Add).Select(Drive);
        sel.Scanner.Should().BeSameAs(std);
        logs.Should().ContainSingle(l => l.Contains("LiteOn"));
    }

    [Fact]
    public void ProbeTimeout_IsTreatedAsUnsupported()
    {
        // The slow probe stays blocked until the end of the test, so it can only end by timeout. The timeout (1 s)
        // leaves the fast probe enough time to be scheduled on a busy CI machine (100 ms was not enough).
        using var release = new ManualResetEventSlim(false);
        var lite = new FakeScanner("LiteOn", () => { release.Wait(TimeSpan.FromSeconds(30)); return Yes; });
        var std = new FakeScanner("StandardC2", () => Yes);
        try
        {
            var sel = new ScannerSelector([lite, std], probeTimeout: TimeSpan.FromSeconds(1)).Select(Drive);
            sel.Scanner.Should().BeSameAs(std);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void ForceStandard_IgnoresOtherScanners()
    {
        var lite = new FakeScanner("LiteOn", () => Yes);
        var std = new FakeScanner("StandardC2", () => Yes);
        var sel = new ScannerSelector([lite, std]).Select(Drive, forceStandard: true);
        sel.Scanner.Should().BeSameAs(std);
        lite.ProbeCalls.Should().Be(0);
    }

    [Fact]
    public void ForceStandard_Unsupported_ReturnsProbeReason()
    {
        var lite = new FakeScanner("LiteOn", () => Yes);
        var std = new FakeScanner("StandardC2", () => No("C2 non supportato"));
        var sel = new ScannerSelector([lite, std]).Select(Drive, forceStandard: true);
        sel.CanScan.Should().BeFalse();
        sel.Message.Should().Be("C2 non supportato");
    }

    [Fact]
    public void NoScannerSupportsDrive_ReturnsMessage()
    {
        var sel = new ScannerSelector([new FakeScanner("LiteOn", () => No()), new FakeScanner("StandardC2", () => No())]).Select(Drive);
        sel.CanScan.Should().BeFalse();
        sel.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExtraScanner_RegisteredViaConstructor_IsUsedAfterBuiltIns()
    {
        var dummy = new FakeScanner("Dummy", () => Yes);
        var std = new FakeScanner("StandardC2", () => No());
        var selector = new ScannerSelector([dummy, std]);
        selector.Scanners.Select(s => s.Name).Should().Equal("StandardC2", "Dummy");
        selector.Select(Drive).Scanner.Should().BeSameAs(dummy);
    }
}
