using FluentAssertions;
using static DiscCheck.Scsi.Tests.FakeScsiTransport;

namespace DiscCheck.Scsi.Tests;

/// <summary>MMC command tests driven by <c>tests/Fixtures/*.bin</c> through <see cref="FakeScsiTransport"/>.</summary>
public class MmcCommandsTests
{
    private static FakeScsiTransport WithFixture(string file) =>
        new FakeScsiTransport().Then(Response.Good(LoadFixture(file)));

    private static FakeScsiTransport WithCheck() =>
        new FakeScsiTransport().Then(Response.Check(0x02, 0x3A, 0x00));

    private static void ShouldBeParseError<T>(MmcResult<T> r, string command, int min, int actual)
    {
        r.IsSuccess.Should().BeFalse();
        r.Error.Should().BeOfType<MmcError.ParseError>()
            .Which.Should().Be(new MmcError.ParseError(command, min, actual));
    }

    private static void ShouldBeCommandError<T>(MmcResult<T> r, string name)
    {
        r.IsSuccess.Should().BeFalse();
        var e = r.Error.Should().BeOfType<MmcError.CommandError>().Subject;
        e.Name.Should().Be(name);
        e.Status.Should().Be(ScsiResult.StatusCheckCondition);
        e.Sense.Should().Be(new SenseData(0x02, 0x3A, 0x00));
    }

    // ---- INQUIRY ----

    [Fact]
    public void Inquiry_ValidFixture_ParsesVendorProductRevision()
    {
        var r = MmcCommands.Inquiry(WithFixture("inquiry.bin"));

        r.IsSuccess.Should().BeTrue();
        r.Value.Vendor.Should().Be("LITE-ON");
        r.Value.Product.Should().Be("iHAS124   B");
        r.Value.Revision.Should().Be("CL9K");
    }

    [Fact]
    public void Inquiry_TruncatedFixture_IsParseError() =>
        ShouldBeParseError(MmcCommands.Inquiry(WithFixture("inquiry-truncated.bin")), "INQUIRY", 36, 20);

    [Fact]
    public void Inquiry_NonGood_IsCommandErrorWithoutParsing() =>
        ShouldBeCommandError(MmcCommands.Inquiry(WithCheck()), "INQUIRY");

    // ---- MODE SENSE(10) 0x2A ----

    [Fact]
    public void ModeSense2A_ValidFixture_ParsesCapabilities()
    {
        var t = WithFixture("mode-sense-2a.bin");
        var r = MmcCommands.ModeSense10Page2A(t);

        r.IsSuccess.Should().BeTrue();
        r.Value.C2PointersSupported.Should().BeTrue();
        r.Value.MaxReadSpeedKBps.Should().Be(0x23C0);
        r.Value.CurrentReadSpeedKBps.Should().Be(1408);
        r.Value.CurrentReadSpeedX.Should().Be(8.0);
        t.Calls[0].Cdb[0].Should().Be(0x5A);
        t.Calls[0].Cdb[2].Should().Be(0x2A);
    }

    [Fact]
    public void ModeSense2A_TruncatedFixture_IsParseError() =>
        ShouldBeParseError(MmcCommands.ModeSense10Page2A(WithFixture("mode-sense-2a-truncated.bin")),
            "MODE SENSE(10) 0x2A", 24, 12);

    [Fact]
    public void ModeSense2A_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.ModeSense10Page2A(WithCheck()), "MODE SENSE(10)");

    // ---- GET CONFIGURATION ----

    [Fact]
    public void GetConfiguration_ValidFixture_ReturnsPayloadTrimmedToDeclaredLength()
    {
        var r = MmcCommands.GetConfiguration(WithFixture("get-configuration.bin"));

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Equal(LoadFixture("get-configuration.bin"));
    }

    [Fact]
    public void GetConfiguration_TruncatedFixture_IsParseError() =>
        ShouldBeParseError(MmcCommands.GetConfiguration(WithFixture("get-configuration-truncated.bin")),
            "GET CONFIGURATION", 8, 6);

    [Fact]
    public void GetConfiguration_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.GetConfiguration(WithCheck()), "GET CONFIGURATION");

    // ---- READ TOC / PMA / ATIP ----

    [Theory]
    [InlineData("read-toc.bin", (byte)0)]
    [InlineData("read-pma.bin", (byte)3)]
    [InlineData("read-atip.bin", (byte)4)]
    public void ReadToc_ValidFixtures_ReturnPayloadAndSendFormat(string file, byte format)
    {
        var t = WithFixture(file);
        var r = MmcCommands.ReadToc(t, format);

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Equal(LoadFixture(file));
        t.Calls[0].Cdb[0].Should().Be(0x43);
        t.Calls[0].Cdb[2].Should().Be(format);
    }

    [Fact]
    public void ReadToc_TruncatedFixture_IsParseError() =>
        ShouldBeParseError(MmcCommands.ReadToc(WithFixture("read-toc-truncated.bin"), 0),
            "READ TOC/PMA/ATIP", 4, 3);

    [Fact]
    public void ReadToc_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.ReadToc(WithCheck(), 0), "READ TOC/PMA/ATIP");

    // ---- READ CD ----

    [Fact]
    public void ReadCd_ValidFixture_ReturnsOneSector()
    {
        var t = WithFixture("read-cd.bin");
        var r = MmcCommands.ReadCd(t, 100, 1, MmcCommands.ReadCdUserData);

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Equal(LoadFixture("read-cd.bin"));
        t.Calls[0].Cdb[0].Should().Be(0xBE);
        t.Calls[0].Cdb[5].Should().Be(100);
        t.Calls[0].Cdb[8].Should().Be(1);
    }

    [Fact]
    public void ReadCd_TruncatedFixture_IsParseError() =>
        ShouldBeParseError(MmcCommands.ReadCd(WithFixture("read-cd-truncated.bin"), 0, 1, MmcCommands.ReadCdUserData),
            "READ CD", 2352, 1000);

    [Fact]
    public void ReadCd_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.ReadCd(WithCheck(), 0, 1, MmcCommands.ReadCdUserData), "READ CD");

    // ---- SET CD SPEED ----

    [Fact]
    public void SetCdSpeed_8x_SendsCdbWith1408()
    {
        var t = new FakeScsiTransport().Then(Response.Good([]));
        var r = MmcCommands.SetCdSpeed(t, 8 * MmcCommands.KBpsPer1x);

        r.IsSuccess.Should().BeTrue();
        var cdb = t.Calls.Single().Cdb;
        cdb[0].Should().Be(0xBB);
        ((cdb[2] << 8) | cdb[3]).Should().Be(1408);
    }

    [Fact]
    public void SetCdSpeed_Maximum0xFFFF_IsAccepted()
    {
        var t = new FakeScsiTransport().Then(Response.Good([]));
        var r = MmcCommands.SetCdSpeed(t, 0xFFFF);

        r.IsSuccess.Should().BeTrue();
        var cdb = t.Calls.Single().Cdb;
        cdb[2].Should().Be(0xFF);
        cdb[3].Should().Be(0xFF);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0xFFFF + 1)]
    public void SetCdSpeed_InvalidValue_IsRejectedBeforeSending(int kBps)
    {
        var t = new FakeScsiTransport();
        var act = () => MmcCommands.SetCdSpeed(t, kBps);

        act.Should().Throw<ArgumentException>();
        t.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SetCdSpeed_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.SetCdSpeed(WithCheck(), 1408), "SET CD SPEED");

    // ---- TEST UNIT READY / START STOP UNIT ----

    [Fact]
    public void TestUnitReady_Good_Succeeds()
    {
        var t = new FakeScsiTransport().Then(Response.Good([]));

        MmcCommands.TestUnitReady(t).IsSuccess.Should().BeTrue();
        t.Calls.Single().Cdb.Should().Equal(new byte[6]);
        t.Calls.Single().Direction.Should().Be(DataDirection.None);
    }

    [Fact]
    public void TestUnitReady_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.TestUnitReady(WithCheck()), "TEST UNIT READY");

    [Theory]
    [InlineData(true, false, (byte)0x01)]
    [InlineData(false, false, (byte)0x00)]
    [InlineData(false, true, (byte)0x02)]
    public void StartStopUnit_Good_SendsExpectedFlags(bool start, bool loadEject, byte expected)
    {
        var t = new FakeScsiTransport().Then(Response.Good([]));

        MmcCommands.StartStopUnit(t, start, loadEject).IsSuccess.Should().BeTrue();
        var cdb = t.Calls.Single().Cdb;
        cdb[0].Should().Be(0x1B);
        cdb[4].Should().Be(expected);
    }

    [Fact]
    public void StartStopUnit_NonGood_IsCommandError() =>
        ShouldBeCommandError(MmcCommands.StartStopUnit(WithCheck(), true), "START STOP UNIT");

    // ---- non-GOOD status: data never parsed ----

    [Fact]
    public void NonGoodStatus_WithValidPayload_DoesNotParseData()
    {
        // A CHECK CONDITION that still carries a perfectly valid INQUIRY payload must not be parsed.
        var data = LoadFixture("inquiry.bin");
        var result = new ScsiResult(ScsiResult.StatusCheckCondition, new SenseData(0x02, 0x04, 0x01), data.Length, null);
        var t = new FakeScsiTransport().Then(new Response(result, data));

        var r = MmcCommands.Inquiry(t);

        r.IsSuccess.Should().BeFalse();
        r.Error.Should().Be(new MmcError.CommandError("INQUIRY", ScsiResult.StatusCheckCondition, new SenseData(0x02, 0x04, 0x01)));
    }
}
