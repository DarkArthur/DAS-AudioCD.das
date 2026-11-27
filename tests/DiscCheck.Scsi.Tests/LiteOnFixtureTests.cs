using DiscCheck.Core;
using DiscCheck.Scanners;
using DiscCheck.Scsi.Drives;
using FluentAssertions;
using NSubstitute;
using static DiscCheck.Scsi.Tests.FakeScsiTransport;

namespace DiscCheck.Scsi.Tests;

/// <summary>
/// Fixture-driven Lite-On tests (task 13.6; Requirements 8.6, 8.7, 8.13, 26.2, 23.8).
/// The <c>liteon-*.bin</c> fixtures are synthetic (see tests/Fixtures/README.md).
/// </summary>
public class LiteOnFixtureTests
{
    private const char Letter = 'D';

    private static byte[] Cdb(int len, params (int Index, byte Value)[] set)
    {
        var cdb = new byte[len];
        foreach (var (i, v) in set) cdb[i] = v;
        return cdb;
    }

    private static readonly byte[] SetSpeed = Cdb(12, (0, 0xBB), (2, 0x05), (3, 0x80), (4, 0xFF), (5, 0xFF));
    private static readonly byte[] Seek0 = Cdb(10, (0, 0x2B));
    private static readonly byte[] F30E = Cdb(12, (0, 0xF3), (1, 0x0E));
    private static readonly byte[] OldRead = Cdb(12, (0, 0xDF), (1, 0x82), (2, 0x09));
    private static readonly byte[] OldGetData = Cdb(12, (0, 0xDF), (1, 0x82), (2, 0x05));
    private static readonly byte[] OldReset = Cdb(12, (0, 0xDF), (1, 0x97));
    private static readonly byte[] OldEnd = Cdb(10, (0, 0xDF), (1, 0xA3), (2, 0x01));

    private static object[] Case(string n, string f, Func<IScsiTransport, MmcResult<bool>> run, byte[] cdb) => [n, f, run, cdb];

    public static IEnumerable<object[]> CommandFixtures()
    {
        yield return Case("InitOldA", "liteon-init-old-a.bin", t => LiteOnQscanCmd.CdErrcInitOldA(t), Cdb(12, (0, 0xDF), (1, 0xA3)));
        yield return Case("InitOldB", "liteon-init-old-b.bin", t => LiteOnQscanCmd.CdErrcInitOldB(t), Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)));
        yield return Case("InitOldC", "liteon-init-old-c.bin", t => LiteOnQscanCmd.CdErrcInitOldC(t), Cdb(12, (0, 0xDF), (1, 0xA0)));
        yield return Case("InitOldD", "liteon-init-old-d.bin", t => LiteOnQscanCmd.CdErrcInitOldD(t), Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x04)));
        yield return Case("InitOldE", "liteon-init-old-e.bin", t => LiteOnQscanCmd.CdErrcInitOldE(t), Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)));
        yield return Case("ProbeNew", "liteon-probe-new.bin", t => LiteOnQscanCmd.CdErrcProbeNew(t), F30E);
        yield return Case("Read", "liteon-old-read.bin", t => LiteOnQscanCmd.CdErrcRead(t), OldRead);
        yield return Case("IntervalReset", "liteon-old-interval-reset.bin", t => LiteOnQscanCmd.CdErrcIntervalReset(t), OldReset);
        yield return Case("EndOld", "liteon-old-end.bin", t => LiteOnQscanCmd.CdErrcEndOld(t), OldEnd);
    }

    [Theory]
    [MemberData(nameof(CommandFixtures))]
    public void Command_WithFixtureResponse_Succeeds(string name, string fixture, Func<IScsiTransport, MmcResult<bool>> run, byte[] cdb)
    {
        _ = name;
        var t = new FakeScsiTransport().WhenFixture(cdb, fixture);

        run(t).IsSuccess.Should().BeTrue();

        t.Calls.Should().ContainSingle().Which.Cdb.Should().Equal(cdb);
    }

    [Fact]
    public void GetData_Fixture_ParsesBlerE22E32()
    {
        var t = new FakeScsiTransport().WhenFixture(OldGetData, "liteon-old-getdata.bin");

        LiteOnQscanCmd.CdErrcGetData(t).Value.Should().Be(new LiteOnErrcOldData(0x0123, 0x45, 9));
    }

    [Fact]
    public void BlockNew_Fixture_ParsesPositionAndCounters()
    {
        var t = new FakeScsiTransport().WhenFixture(F30E, "liteon-block-new.bin");

        LiteOnQscanCmd.CdErrcBlockNew(t).Value.Should().Be(new LiteOnErrcNewBlock(2 * 75, 0x012C, 7));
    }

    // ---- LiteOnScanner end to end, responses from fixtures ----

    private static LiteOnScanner NewScanner(FakeScsiTransport t)
    {
        var f = Substitute.For<IScsiTransportFactory>();
        f.Open(Letter).Returns(t);
        return new LiteOnScanner(f, TimeSpan.Zero);
    }

    private static ScanOptions Options(int leadOut, LiteOnMethod method) =>
        new(Letter, 1408, method, new Toc([new TocEntry(1, 0, true)], leadOut));

    private static FakeScsiTransport OldTransport()
    {
        var t = new FakeScsiTransport().When(SetSpeed, Response.Good([]))
            .WhenFixture(Cdb(12, (0, 0xDF), (1, 0xA3)), "liteon-init-old-a.bin")
            .WhenFixture(Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)), "liteon-init-old-b.bin")
            .WhenFixture(Cdb(12, (0, 0xDF), (1, 0xA0)), "liteon-init-old-c.bin")
            .WhenFixture(Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x04)), "liteon-init-old-d.bin")
            .WhenFixture(OldRead, "liteon-old-read.bin")
            .WhenFixture(OldGetData, "liteon-old-getdata.bin")
            .WhenFixture(OldReset, "liteon-old-interval-reset.bin")
            .WhenFixture(OldEnd, "liteon-old-end.bin");
        return t;
    }

    private static async Task<(List<ScanSample> Samples, Exception? Error)> Collect(
        LiteOnScanner scanner, ScanOptions options, int cancelAfter = int.MaxValue)
    {
        var samples = new List<ScanSample>();
        using var cts = new CancellationTokenSource();
        try
        {
            await foreach (var s in scanner.ScanAsync(options, new Progress<ScanProgress>(), cts.Token))
            {
                samples.Add(s);
                if (samples.Count >= cancelAfter) cts.Cancel();
            }
            return (samples, null);
        }
        catch (Exception ex)
        {
            return (samples, ex);
        }
    }

    private static string Hex(byte[] cdb) => Convert.ToHexString(cdb);

    [Fact]
    public async Task OldMethod_WithFixtures_ReadsCountersAndEndsWithDfA301()
    {
        var t = OldTransport();

        var (samples, error) = await Collect(NewScanner(t), Options(150, LiteOnMethod.Old));

        error.Should().BeNull();
        samples.Should().HaveCount(2);
        samples.Should().OnlyContain(s => s.C1 == 0x0123 && s.C2 == 0x45 && s.CU == 9);
        t.Calls[^1].Cdb.Should().Equal(OldEnd);
    }

    [Fact]
    public async Task NewMethod_WithFixtures_ConservesCountersAndLeavesCuNull()
    {
        var poll = LoadFixture("liteon-block-new.bin");
        poll[2] = 4; // position 00:04:00 -> LBA 150 after removing the 150-sector lead-in offset
        var t = new FakeScsiTransport().When(SetSpeed, Response.Good([])).When(Seek0, Response.Good([]))
            .WhenFixture(F30E, "liteon-probe-new.bin")
            .When(F30E, Response.Good(poll));

        var (samples, error) = await Collect(NewScanner(t), Options(150, LiteOnMethod.New));

        error.Should().BeNull();
        samples.Should().HaveCount(2);
        samples.Should().OnlyContain(s => s.CU == null);
        samples.Sum(s => s.C1).Should().Be(0x012C);
        samples.Sum(s => s.C2).Should().Be(7);
        t.Calls.Should().NotContain(c => c.Cdb.SequenceEqual(OldEnd));
    }

    [Fact]
    public async Task NewMethod_ProbeFailure_FallsBackToOldWithFixtures()
    {
        var t = OldTransport(); // no SEEK registered: the Nuovo init fails

        var (samples, error) = await Collect(NewScanner(t), Options(75, LiteOnMethod.New));

        error.Should().BeNull();
        samples.Should().ContainSingle().Which.CU.Should().Be(9);
        t.Calls.Count(c => c.Cdb.SequenceEqual(Seek0)).Should().Be(1);
        t.Calls[^1].Cdb.Should().Equal(OldEnd);
    }

    [Fact]
    public async Task OldMethod_EndRunsInFinally_OnErrorAndCancellation()
    {
        var failing = OldTransport().When(OldRead, Response.Check(0x03, 0x11, 0x00));
        // First read succeeds (queued fixture), second fails.
        var (s1, e1) = await Collect(NewScanner(failing), Options(300, LiteOnMethod.Old));
        s1.Should().HaveCount(1);
        e1.Should().BeOfType<ScanFailedException>();
        failing.Calls[^1].Cdb.Should().Equal(OldEnd);

        var cancelled = OldTransport();
        var (s2, e2) = await Collect(NewScanner(cancelled), Options(750, LiteOnMethod.Old), cancelAfter: 1);
        s2.Should().HaveCount(1);
        e2.Should().BeAssignableTo<OperationCanceledException>();
        cancelled.Calls[^1].Cdb.Should().Equal(OldEnd);
    }

    [Theory]
    [InlineData("lite-on")]
    [InlineData("LITE-ON")]
    public void Probe_LowercaseVendor_IsSupported(string vendor)
    {
        var drive = new DriveInfoEx(Letter, vendor, "iHAS124", "WL0L", true, [], 0, null, null, null, null);

        NewScanner(new FakeScsiTransport()).Probe(drive).Supported.Should().BeTrue();
    }
}
