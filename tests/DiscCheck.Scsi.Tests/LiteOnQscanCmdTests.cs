using DiscCheck.Scanners;
using FluentAssertions;

namespace DiscCheck.Scsi.Tests;

/// <summary>Minimal checks of the ported Lite-On CDBs (docs/liteon-cdb.md). Fixture-based tests are task 13.6.</summary>
public class LiteOnQscanCmdTests
{
    private static byte[] Cdb(int len, params (int Index, byte Value)[] set)
    {
        var cdb = new byte[len];
        foreach (var (i, v) in set) cdb[i] = v;
        return cdb;
    }

    public static IEnumerable<object[]> Commands()
    {
        yield return Case("InitOldA", t => LiteOnQscanCmd.CdErrcInitOldA(t), Cdb(12, (0, 0xDF), (1, 0xA3)), DataDirection.In);
        yield return Case("InitOldB", t => LiteOnQscanCmd.CdErrcInitOldB(t), Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)), DataDirection.In);
        yield return Case("InitOldC", t => LiteOnQscanCmd.CdErrcInitOldC(t), Cdb(12, (0, 0xDF), (1, 0xA0)), DataDirection.In);
        yield return Case("InitOldD", t => LiteOnQscanCmd.CdErrcInitOldD(t), Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x04)), DataDirection.In);
        yield return Case("InitOldE", t => LiteOnQscanCmd.CdErrcInitOldE(t), Cdb(12, (0, 0xDF), (1, 0xA0), (4, 0x02)), DataDirection.In);
        yield return Case("ProbeNew", t => LiteOnQscanCmd.CdErrcProbeNew(t), Cdb(12, (0, 0xF3), (1, 0x0E)), DataDirection.In);
        yield return Case("Read", t => LiteOnQscanCmd.CdErrcRead(t), Cdb(12, (0, 0xDF), (1, 0x82), (2, 0x09)), DataDirection.In);
        yield return Case("IntervalReset", t => LiteOnQscanCmd.CdErrcIntervalReset(t), Cdb(12, (0, 0xDF), (1, 0x97)), DataDirection.In);
        yield return Case("EndOld", t => LiteOnQscanCmd.CdErrcEndOld(t), Cdb(10, (0, 0xDF), (1, 0xA3), (2, 0x01)), DataDirection.In);
        yield return Case("Seek0", t => MmcSeekPort.Seek(t), Cdb(10, (0, 0x2B)), DataDirection.None);
    }

    private static object[] Case(string name, Func<IScsiTransport, MmcResult<bool>> run, byte[] cdb, DataDirection dir)
        => [name, run, cdb, dir];

    [Theory]
    [MemberData(nameof(Commands))]
    public void SendsDocumentedCdb(string name, Func<IScsiTransport, MmcResult<bool>> run, byte[] cdb, DataDirection dir)
    {
        _ = name;
        var fake = new FakeScsiTransport().When(cdb, FakeScsiTransport.Response.Good(new byte[16]));

        run(fake).IsSuccess.Should().BeTrue();

        fake.Calls.Should().ContainSingle();
        fake.Calls[0].Cdb.Should().Equal(cdb);
        fake.Calls[0].Direction.Should().Be(dir);
    }

    [Fact]
    public void GetData_SendsCdbAndParsesCounters()
    {
        var cdb = Cdb(12, (0, 0xDF), (1, 0x82), (2, 0x05));
        var data = new byte[] { 0x01, 0x02, 0x00, 0x07, 0x09 };
        var fake = new FakeScsiTransport().When(cdb, FakeScsiTransport.Response.Good(data));

        var r = LiteOnQscanCmd.CdErrcGetData(fake);

        fake.Calls[0].Cdb.Should().Equal(cdb);
        r.Value.Should().Be(new LiteOnErrcOldData(0x0102, 7, 9));
    }

    [Fact]
    public void BlockNew_SendsCdbAndParsesPositionAndCounters()
    {
        var cdb = Cdb(12, (0, 0xF3), (1, 0x0E));
        var data = new byte[16];
        data[1] = 1; data[2] = 2; data[3] = 3; data[5] = 4; data[7] = 5;
        var fake = new FakeScsiTransport().When(cdb, FakeScsiTransport.Response.Good(data));

        var r = LiteOnQscanCmd.CdErrcBlockNew(fake);

        fake.Calls[0].Cdb.Should().Equal(cdb);
        r.Value.Should().Be(new LiteOnErrcNewBlock(1 * 60 * 75 + 2 * 75 + 3, 4, 5));
    }

    [Fact]
    public void ShortResponses_AreParseErrors_AndSenseIsCommandError_WithoutRetry()
    {
        var fake = new FakeScsiTransport()
            .Then(FakeScsiTransport.Response.Good(new byte[4]))
            .Then(FakeScsiTransport.Response.Good(new byte[7]))
            .Then(FakeScsiTransport.Response.Check(0x03, 0x11, 0x00));

        LiteOnQscanCmd.CdErrcGetData(fake).Error.Should().BeOfType<MmcError.ParseError>();
        LiteOnQscanCmd.CdErrcBlockNew(fake).Error.Should().BeOfType<MmcError.ParseError>();
        LiteOnQscanCmd.CdErrcRead(fake).Error.Should().BeOfType<MmcError.CommandError>();
        fake.Calls.Should().HaveCount(3);
    }
}
