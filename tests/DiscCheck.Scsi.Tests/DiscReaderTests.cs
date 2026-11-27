using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using FluentAssertions;
using NSubstitute;
using static DiscCheck.Scsi.Tests.FakeScsiTransport;

namespace DiscCheck.Scsi.Tests;

/// <summary>DiscReader over fake transports (Requirements 5.1, 5.10, 5.12, 5.13, 24.7, 24.8).</summary>
public class DiscReaderTests
{
    private static readonly AtipTable Table = AtipTable.Parse(
        """{"version":1,"entries":[{"atip":"97:26:64","manufacturer":"Test Maker","source":"https://x"}]}""");

    private static byte[] ConfigWithProfile(int profile) => [0, 0, 0, 4, 0, 0, (byte)(profile >> 8), (byte)profile];

    private static byte[] AudioToc() => [0, 0x12, 1, 1, 0, 0x10, 1, 0, 0, 0, 0, 0, 0, 0x10, 0xAA, 0, 0, 0, 0x4E, 0x20];

    private static byte[] Atip() => [0, 0x0A, 0, 0, 0x80, 0x1D, 0x1A, 0, 97, 26, 64, 0];

    private static DiscReader Reader(FakeScsiTransport t)
    {
        var f = Substitute.For<IScsiTransportFactory>();
        f.Open('D').Returns(t);
        return new DiscReader(f, Table);
    }

    private static FakeScsiTransport Transport(params Response[] responses)
    {
        var t = new FakeScsiTransport();
        foreach (var r in responses) t.Then(r);
        return t;
    }

    [Fact]
    public void CdR_AudioDisc_ReturnsTocMediaIdAndManufacturer()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x09)),
            Response.Good(AudioToc()), Response.Good(Atip()));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.Audio);
        r.Toc!.Tracks.Should().HaveCount(1);
        r.Toc.LeadOutLba.Should().Be(20000);
        r.TocHash.Should().NotBeNullOrEmpty();
        r.MediaId!.ToString().Should().Be("97:26:64");
        r.Manufacturer.Should().Be("Test Maker");
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public void NoDisc_ReportsNoDiscWithoutTocData()
    {
        var r = Reader(Transport(Response.Check(0x02, 0x3A, 0x00))).Read('D');

        r.Status.Should().Be(DiscStatus.NoDisc);
        r.Toc.Should().BeNull();
        r.Message.Should().Be(DiscReader.NoDiscMessage);
    }

    [Fact]
    public void BecomingReady_ReportsNotReady()
    {
        Reader(Transport(Response.Check(0x02, 0x04, 0x01))).Read('D').Status.Should().Be(DiscStatus.NotReady);
    }

    [Fact]
    public void Dvd_IsUnsupportedAndNoTocOrAtipIsRequested()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x1B)));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.UnsupportedMedium);
        r.Message.Should().Be(DiscReader.UnsupportedMediumMessage);
        t.Calls.Should().HaveCount(2);
        t.Calls.Should().NotContain(c => c.Cdb[0] == 0x43 || c.Cdb[0] == 0xBE);
    }

    [Fact]
    public void DataDisc_IsNotAudio()
    {
        byte[] dataToc = [0, 0x12, 1, 1, 0, 0x14, 1, 0, 0, 0, 0, 0, 0, 0x14, 0xAA, 0, 0, 0, 0x4E, 0x20];
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x08)), Response.Good(dataToc));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.NotAudio);
        r.Toc.Should().BeNull();
    }

    [Fact]
    public void PressedCd_ShowsTocWithMediaUnavailable()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x08)), Response.Good(AudioToc()));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.Audio);
        r.MediaId.Should().BeNull();
        r.MediaUnavailableReason.Should().Be(DiscReader.NotCdRReason);
        t.Calls.Should().NotContain(c => c.Cdb[0] == 0x43 && c.Cdb[2] == 4);
    }

    [Fact]
    public void AtipReadFails_KeepsTocAndReportsReason()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x09)),
            Response.Good(AudioToc()), Response.Check(0x05, 0x24, 0x00));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.Audio);
        r.Toc.Should().NotBeNull();
        r.MediaId.Should().BeNull();
        r.MediaUnavailableReason.Should().Contain("ATIP");
    }

    [Fact]
    public void CdR_UnknownMediaId_ShowsUnknownManufacturer()
    {
        byte[] atip = [0, 0x0A, 0, 0, 0x80, 0x1D, 0x1A, 0, 97, 10, 10, 0];
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x09)),
            Response.Good(AudioToc()), Response.Good(atip));

        var r = Reader(t).Read('D');

        r.MediaId!.ToString().Should().Be("97:10:10");
        r.Manufacturer.Should().Be(AtipTable.UnknownManufacturer);
    }

    [Fact]
    public void TruncatedAtip_KeepsTocAndReportsLengths()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x09)),
            Response.Good(AudioToc()), Response.Good([0, 0x06, 0, 0, 0x80, 0x1D, 0x1A, 0]));

        var r = Reader(t).Read('D');

        r.Toc.Should().NotBeNull();
        r.MediaId.Should().BeNull();
        r.MediaUnavailableReason.Should().Contain("ATIP");
    }

    [Fact]
    public void TocReadFails_ReportsSenseAndNoData()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x09)), Response.Check(0x03, 0x11, 0x05));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.ReadError);
        r.Toc.Should().BeNull();
        r.Message.Should().Contain("0x3").And.Contain("0x11");
    }

    [Fact]
    public void TruncatedToc_ReportsParseError()
    {
        var t = Transport(Response.Good([]), Response.Good(ConfigWithProfile(0x09)),
            Response.Good(AudioToc()[..10]));

        var r = Reader(t).Read('D');

        r.Status.Should().Be(DiscStatus.ReadError);
        r.TocHash.Should().BeNull();
    }
}
