using DiscCheck.Core;
using DiscCheck.Scanners;
using DiscCheck.Scsi.Drives;
using FluentAssertions;
using NSubstitute;
using static DiscCheck.Scsi.Tests.FakeScsiTransport;

namespace DiscCheck.Scsi.Tests;

/// <summary>LiteOnScanner with a fake transport (Requirements 8.3-8.8, 8.11, 8.12, 9.12, 23.8).</summary>
public class LiteOnScannerTests
{
    private const char Letter = 'D';

    private static DriveInfoEx DriveOf(string vendor) =>
        new(Letter, vendor, "iHAS124", "WL0L", true, [], 0, null, null, null, null);

    private static IScsiTransportFactory FactoryOf(IScsiTransport transport)
    {
        var f = Substitute.For<IScsiTransportFactory>();
        f.Open(Letter).Returns(transport);
        return f;
    }

    private static LiteOnScanner NewScanner(FakeScsiTransport t) =>
        new(FactoryOf(t), TimeSpan.Zero);

    private static ScanOptions Options(int leadOut, LiteOnMethod method) =>
        new(Letter, 1408, method, new Toc([new TocEntry(1, 0, true)], leadOut));

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

    private static readonly byte[][] OldInit =
    [
        Cdb(12, (0, 0xDF), (1, 0xA3)),
        Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)),
        Cdb(12, (0, 0xDF), (1, 0xA0)),
        Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x04)),
    ];

    private static Response Good256() => Response.Good(new byte[256]);

    private static Response OldCounters(int bler, int e22, int e32)
    {
        var d = new byte[256];
        d[0] = (byte)(bler >> 8); d[1] = (byte)bler;
        d[2] = (byte)(e22 >> 8); d[3] = (byte)e22;
        d[4] = (byte)e32;
        return Response.Good(d);
    }

    /// <summary>Metodo_Nuovo poll response: position as binary M:S:F of (lba + 150), BLER, E22.</summary>
    private static Response NewPoll(int lba, int bler, int e22)
    {
        int msf = lba + 150;
        var d = new byte[16];
        d[1] = (byte)(msf / 4500); d[2] = (byte)(msf / 75 % 60); d[3] = (byte)(msf % 75);
        d[4] = (byte)(bler >> 8); d[5] = (byte)bler;
        d[6] = (byte)(e22 >> 8); d[7] = (byte)e22;
        return Response.Good(d);
    }

    private static FakeScsiTransport OldTransport(params Response[] getData)
    {
        var t = new FakeScsiTransport().When(SetSpeed, Response.Good([]));
        foreach (var c in OldInit) t.When(c, Good256());
        t.When(Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)), Good256());
        t.When(OldRead, Good256()).When(OldReset, Good256()).When(OldEnd, Good256());
        foreach (var g in getData) t.When(OldGetData, g);
        return t;
    }

    private static async Task<(List<ScanSample> Samples, Exception? Error)> Collect(
        LiteOnScanner scanner, ScanOptions options, CancellationToken ct = default, int cancelAfter = int.MaxValue)
    {
        var samples = new List<ScanSample>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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

    private static string[] Sent(FakeScsiTransport t) => t.Calls.Select(c => Convert.ToHexString(c.Cdb)).ToArray();

    [Theory]
    [InlineData("LITE-ON", true)]
    [InlineData("lite-on", true)]
    [InlineData("Lite-On ", true)]
    [InlineData("PLEXTOR", false)]
    public void Probe_MatchesVendorCaseInsensitively(string vendor, bool supported)
    {
        var caps = NewScanner(new FakeScsiTransport()).Probe(DriveOf(vendor));

        caps.Supported.Should().Be(supported);
        if (!supported) caps.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OldMethod_RunsFlowInOrder_PublishesSamplePer75Sectors_AndEndsWithDfA301()
    {
        var t = OldTransport(OldCounters(5, 2, 1));

        var (samples, error) = await Collect(NewScanner(t), Options(150, LiteOnMethod.Old));

        error.Should().BeNull();
        samples.Should().HaveCount(2);
        samples[0].Should().Match<ScanSample>(s => s.Lba == 0 && s.TimeSeconds == 0 && s.C1 == 5 && s.C2 == 2 && s.CU == 1);
        samples[1].Lba.Should().Be(75);

        var sent = Sent(t);
        sent[0].Should().Be(Convert.ToHexString(SetSpeed));
        sent[1].Should().Be(Convert.ToHexString(OldInit[0]));
        sent.Skip(6).Take(3).Should().Equal(Convert.ToHexString(OldRead), Convert.ToHexString(OldGetData), Convert.ToHexString(OldReset));
        sent[^1].Should().Be(Convert.ToHexString(OldEnd));
        t.Calls[^1].Timeout.Should().Be(TimeSpan.FromSeconds(30));
        t.Calls.Should().NotContain(c => c.Cdb.SequenceEqual(Seek0));
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task NewMethod_AggregatesPolls_DiscardsStalePolls_ReportsCuAsNull_AndSendsNoEndCommand()
    {
        var t = new FakeScsiTransport()
            .When(SetSpeed, Response.Good([])).When(Seek0, Response.Good([]))
            .When(F30E, Response.Good(new byte[16]))          // init probe
            .When(F30E, NewPoll(30, 1, 0))
            .When(F30E, NewPoll(30, 9, 9))                    // no progress: discarded
            .When(F30E, NewPoll(80, 2, 1))                    // 80 sectors -> first sample
            .When(F30E, NewPoll(150, 4, 0));                  // 150 sectors -> second sample

        var (samples, error) = await Collect(NewScanner(t), Options(150, LiteOnMethod.New));

        error.Should().BeNull();
        samples.Should().HaveCount(2);
        samples[0].Should().Match<ScanSample>(s => s.Lba == 0 && s.C1 == 3 && s.C2 == 1 && s.CU == null);
        samples[1].Should().Match<ScanSample>(s => s.Lba == 75 && s.TimeSeconds == 1 && s.C1 == 4 && s.C2 == 0 && s.CU == null);
        Sent(t).Should().NotContain(Convert.ToHexString(OldEnd), "the Metodo_Nuovo sends nothing at the end");
        Sent(t)[..3].Should().Equal(Convert.ToHexString(SetSpeed), Convert.ToHexString(Seek0), Convert.ToHexString(F30E));
    }

    [Fact]
    public async Task NewMethod_InitFailure_FallsBackOnceToOld_AndFixesEffectiveMethodToOld()
    {
        // SEEK is not registered: CHECK CONDITION, so the Nuovo init fails.
        var t = OldTransport(OldCounters(1, 0, 0));

        var (samples, error) = await Collect(NewScanner(t), Options(75, LiteOnMethod.New));

        error.Should().BeNull();
        samples.Should().ContainSingle().Which.CU.Should().Be(0);
        var sent = Sent(t);
        sent.Count(c => c == Convert.ToHexString(Seek0)).Should().Be(1);
        sent.Should().NotContain(Convert.ToHexString(F30E), "after the fallback no Nuovo poll is sent");
        sent[^1].Should().Be(Convert.ToHexString(OldEnd), "the effective method is Vecchio, so the end command is sent");
    }

    [Fact]
    public async Task InitFailureOfBothMethods_FailsWithoutSamples()
    {
        var t = new FakeScsiTransport().When(SetSpeed, Response.Good([]));

        var (samples, error) = await Collect(NewScanner(t), Options(150, LiteOnMethod.New));

        samples.Should().BeEmpty();
        error.Should().BeOfType<ScanInitFailedException>();
    }

    [Fact]
    public async Task NewMethod_64PollsWithoutProgress_FailsWithScanFailed()
    {
        var t = new FakeScsiTransport()
            .When(SetSpeed, Response.Good([])).When(Seek0, Response.Good([]))
            .When(F30E, Response.Good(new byte[16]))
            .When(F30E, NewPoll(0, 0, 0));

        var (samples, error) = await Collect(NewScanner(t), Options(150, LiteOnMethod.New));

        samples.Should().BeEmpty();
        error.Should().BeOfType<ScanFailedException>();
        Sent(t).Count(c => c == Convert.ToHexString(F30E)).Should().Be(1 + LiteOnScanner.MaxStalePolls);
    }

    [Fact]
    public async Task ReadFailure_StopsKeepingPublishedSamples_AndStillSendsEnd()
    {
        var t = OldTransport(OldCounters(1, 0, 0));
        // The second interval read fails with a sense: ILLEGAL REQUEST.
        t.When(OldRead, Response.Check(0x03, 0x11, 0x00));

        var (samples, error) = await Collect(NewScanner(t), Options(300, LiteOnMethod.Old));

        samples.Should().HaveCount(1);
        var failed = error.Should().BeOfType<ScanFailedException>().Subject;
        failed.Lba.Should().Be(75);
        failed.SenseKey.Should().Be(0x03);
        Sent(t)[^1].Should().Be(Convert.ToHexString(OldEnd));
    }

    [Fact]
    public async Task Cancellation_StillSendsEndCommand()
    {
        var t = OldTransport(OldCounters(0, 0, 0));

        var (samples, error) = await Collect(NewScanner(t), Options(750, LiteOnMethod.Old), cancelAfter: 1);

        samples.Should().HaveCount(1);
        error.Should().BeAssignableTo<OperationCanceledException>();
        Sent(t)[^1].Should().Be(Convert.ToHexString(OldEnd));
    }

    [Fact]
    public async Task EndCommandFailure_ThrowsTestModeEndFailed()
    {
        var failing = new FakeScsiTransport().When(SetSpeed, Response.Good([]));
        foreach (var c in OldInit) failing.When(c, Good256());
        failing.When(Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)), Good256())
               .When(OldRead, Good256()).When(OldReset, Good256())
               .When(OldGetData, OldCounters(0, 0, 0)).When(OldEnd, Response.Check(0x05, 0x20, 0x00));

        var (samples, endError) = await Collect(NewScanner(failing), Options(75, LiteOnMethod.Old));

        samples.Should().HaveCount(1);
        var ex = endError.Should().BeOfType<TestModeEndFailedException>().Subject;
        ex.Cdb.Should().StartWith("DFA301");
    }
}
