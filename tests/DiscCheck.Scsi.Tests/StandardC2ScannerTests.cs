using DiscCheck.Core;
using FluentAssertions;
using DiscCheck.Scanners;
using DiscCheck.Scsi.Drives;
using NSubstitute;

namespace DiscCheck.Scsi.Tests;

/// <summary>StandardC2Scanner with a fake transport (Requirements 7.1, 7.2, 7.7, 7.10).</summary>
public class StandardC2ScannerTests
{
    private const char Letter = 'D';

    private static readonly DriveInfoEx Drive =
        new(Letter, "LITE-ON", "iHAS124", "WL0L", true, [], 0, null, null, null, null);

    private static IScsiTransportFactory FactoryOf(IScsiTransport transport)
    {
        var f = Substitute.For<IScsiTransportFactory>();
        f.Open(Letter).Returns(transport);
        return f;
    }

    private static ScanOptions Options(int leadOut) =>
        new(Letter, 1408, LiteOnMethod.New, new Toc([new TocEntry(1, 0, true)], leadOut));

    /// <summary>MODE SENSE(10) 0x2A response: 8-byte header, no block descriptors, C2 flag in page byte 5.</summary>
    private static byte[] ModePage(bool c2, int length = 32)
    {
        var d = new byte[length];
        if (length > 13 && c2) d[8 + 5] = 0x10;
        return d;
    }

    /// <summary>READ CD response with the given sector size; <paramref name="c2BitsPerSector"/> bits are set in each sector's pointers.</summary>
    private static byte[] Block(int sectors, int sectorSize, Func<int, int> c2BitsPerSector, int c2Offset = 0)
    {
        var data = new byte[sectors * sectorSize];
        for (int s = 0; s < sectors; s++)
        {
            int bits = c2BitsPerSector(s);
            int start = s * sectorSize + MmcCommands.CdDaSectorSize + c2Offset;
            for (int b = 0; b < bits; b++)
                data[start + b / 8] |= (byte)(1 << (b % 8));
        }
        return data;
    }

    private static async Task<(List<ScanSample> Samples, Exception? Error)> Collect(StandardC2Scanner scanner, ScanOptions options)
    {
        var samples = new List<ScanSample>();
        try
        {
            await foreach (var s in scanner.ScanAsync(options, new Progress<ScanProgress>(), CancellationToken.None))
                samples.Add(s);
            return (samples, null);
        }
        catch (Exception ex)
        {
            return (samples, ex);
        }
    }

    [Fact]
    public void Probe_CheckConditionOnModeSense_IsNotSupportedWithReason()
    {
        var t = new FakeScsiTransport().Then(FakeScsiTransport.Response.Check(0x05, 0x24, 0x00));
        var caps = new StandardC2Scanner(FactoryOf(t)).Probe(Drive);

        caps.Supported.Should().BeFalse();
        caps.Reason.Should().Contain("MODE SENSE");
    }

    [Fact]
    public void Probe_ShortModeSenseResponse_IsNotSupported()
    {
        var t = new FakeScsiTransport().Then(FakeScsiTransport.Response.Good(ModePage(true, 10)));
        var caps = new StandardC2Scanner(FactoryOf(t)).Probe(Drive);

        caps.Supported.Should().BeFalse();
        caps.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Probe_C2FlagClear_IsNotSupported()
    {
        var t = new FakeScsiTransport().Then(FakeScsiTransport.Response.Good(ModePage(false)));
        new StandardC2Scanner(FactoryOf(t)).Probe(Drive).Supported.Should().BeFalse();
    }

    [Fact]
    public void Probe_C2FlagSet_ReportsC2OnlyAndDisposesTransport()
    {
        var t = new FakeScsiTransport().Then(FakeScsiTransport.Response.Good(ModePage(true)));
        var caps = new StandardC2Scanner(FactoryOf(t)).Probe(Drive);

        caps.Should().Be(new ScannerCapabilities(true, false, true, false, null));
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_LastBlockIsResidual_ReadsOnlyRemainingSectorsAndFlushesResidualSample()
    {
        // 100 sectors with 26-sector blocks: 26 + 26 + 26 + 22. Samples: 75 sectors + residual of 25.
        const int sectorSize = 2646;
        var t = new FakeScsiTransport()
            .Then(FakeScsiTransport.Response.Good(Block(26, sectorSize, s => s == 0 ? 3 : 0)))
            .Then(FakeScsiTransport.Response.Good(Block(26, sectorSize, _ => 0)))
            .Then(FakeScsiTransport.Response.Good(Block(26, sectorSize, _ => 0)))
            .Then(FakeScsiTransport.Response.Good(Block(22, sectorSize, s => s == 21 ? 5 : 0)));

        var (samples, error) = await Collect(new StandardC2Scanner(FactoryOf(t)), Options(100));

        error.Should().BeNull();
        t.Calls.Should().HaveCount(4);
        var last = t.Calls[3].Cdb;
        last[0].Should().Be(0xBE);
        last[1].Should().Be(0x04);
        ((last[2] << 24) | (last[3] << 16) | (last[4] << 8) | last[5]).Should().Be(78);
        ((last[6] << 16) | (last[7] << 8) | last[8]).Should().Be(22);
        last[9].Should().Be(StandardC2Scanner.FlagsC2Only);

        samples.Should().HaveCount(2);
        samples[0].Should().Match<ScanSample>(s => s.Lba == 0 && s.TimeSeconds == 0 && s.C1 == null && s.C2 == 3 && s.CU == 0);
        samples[1].Should().Match<ScanSample>(s => s.Lba == 75 && s.TimeSeconds == 1 && s.C1 == null && s.C2 == 5 && s.CU == 0);
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_ExactMultipleOfBlockAndSample_EmitsNoResidualSample()
    {
        // 150 sectors = 2 samples; blocks of 27: 27*5 + 15.
        var t = new FakeScsiTransport();
        for (int i = 0; i < 5; i++) t.Then(FakeScsiTransport.Response.Good(Block(27, 2646, _ => 0)));
        t.Then(FakeScsiTransport.Response.Good(Block(15, 2646, _ => 0)));

        var (samples, error) = await Collect(new StandardC2Scanner(FactoryOf(t), blockSectors: 27), Options(150));

        error.Should().BeNull();
        samples.Should().HaveCount(2);
    }

    [Fact]
    public async Task Scan_ReadCdError_StopsWithoutRetryAndKeepsPublishedSamples()
    {
        // Blocks of 26: the first three (78 sectors) publish sample 0; the fourth read (LBA 78) fails.
        var t = new FakeScsiTransport();
        for (int i = 0; i < 3; i++) t.Then(FakeScsiTransport.Response.Good(Block(26, 2646, s => s == 0 ? 1 : 0)));
        t.Then(FakeScsiTransport.Response.Check(0x03, 0x11, 0x05));

        var (samples, error) = await Collect(new StandardC2Scanner(FactoryOf(t)), Options(300));

        var failure = error.Should().BeOfType<ScanFailedException>().Subject;
        failure.Lba.Should().Be(78);
        failure.SenseKey.Should().Be(0x03);
        failure.Asc.Should().Be(0x11);
        failure.Ascq.Should().Be(0x05);
        failure.Message.Should().Contain("78");

        t.Calls.Should().HaveCount(4, "a failed READ CD is not retried");
        samples.Should().ContainSingle().Which.Should().Match<ScanSample>(s => s.Lba == 0 && s.C2 == 3);
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_TransportFailure_ThrowsWithoutSenseData()
    {
        var t = new FakeScsiTransport().Then(FakeScsiTransport.Response.Failure(new TransportError.Timeout()));

        var (samples, error) = await Collect(new StandardC2Scanner(FactoryOf(t)), Options(100));

        var failure = error.Should().BeOfType<ScanFailedException>().Subject;
        failure.Lba.Should().Be(0);
        failure.SenseKey.Should().BeNull();
        samples.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_Format0x14_ExcludesBlockErrorAndPaddingBytesFromCount()
    {
        // 2648 bytes/sector: block error byte and padding byte are 0xFF but must not be counted; 1 pointer bit is set.
        const int sectorSize = 2648;
        var data = Block(75, sectorSize, s => s == 0 ? 1 : 0, c2Offset: 2);
        for (int s = 0; s < 75; s++)
        {
            data[s * sectorSize + MmcCommands.CdDaSectorSize] = 0xFF;
            data[s * sectorSize + MmcCommands.CdDaSectorSize + 1] = 0xFF;
        }

        var t = new FakeScsiTransport();
        // Blocks of 26: 26 + 26 + 23, sliced from the same data.
        t.Then(FakeScsiTransport.Response.Good(data[..(26 * sectorSize)]));
        t.Then(FakeScsiTransport.Response.Good(data[(26 * sectorSize)..(52 * sectorSize)]));
        t.Then(FakeScsiTransport.Response.Good(data[(52 * sectorSize)..]));

        var scanner = new StandardC2Scanner(FactoryOf(t), StandardC2Scanner.FlagsC2WithBlockError);
        var (samples, error) = await Collect(scanner, Options(75));

        error.Should().BeNull();
        t.Calls.Should().OnlyContain(c => c.Cdb[9] == 0x14);
        samples.Should().ContainSingle().Which.C2.Should().Be(1);
    }
}
