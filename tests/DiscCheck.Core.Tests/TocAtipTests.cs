using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

/// <summary>TOC and ATIP logic (Requirements 5.2-5.8, 5.11, 5.14).</summary>
public class TocAtipTests
{
    private static byte[] TocBytes(params (byte Control, byte Track, int Lba)[] entries)
    {
        var list = new List<byte> { 0, 0, 1, (byte)(entries.Length - 1) };
        foreach (var (c, t, lba) in entries)
            list.AddRange([0, c, t, 0, (byte)(lba >> 24), (byte)(lba >> 16), (byte)(lba >> 8), (byte)lba]);
        int len = list.Count - 2;
        list[0] = (byte)(len >> 8);
        list[1] = (byte)len;
        return [.. list];
    }

    private static readonly byte[] TwoTracks = TocBytes((0x10, 1, 0), (0x10, 2, 15000), (0x10, 0xAA, 40000));

    [Fact]
    public void TocParser_ParsesTracksAndLeadOut()
    {
        TocParser.TryParse(TwoTracks, out var toc, out _).Should().BeTrue();

        toc!.Tracks.Should().Equal(new TocEntry(1, 0, true), new TocEntry(2, 15000, true));
        toc.LeadOutLba.Should().Be(40000);
        toc.TotalSectors.Should().Be(40000);
        toc.TrackSectors(0).Should().Be(15000);
    }

    [Fact]
    public void TocParser_FlagsDataTrack()
    {
        var bytes = TocBytes((0x14, 1, 0), (0x14, 0xAA, 100));
        TocParser.TryParse(bytes, out var toc, out _).Should().BeTrue();
        toc!.HasAudioTracks.Should().BeFalse();
    }

    [Fact]
    public void TocParser_RejectsTruncatedAndNonIncreasing()
    {
        TocParser.TryParse(TwoTracks.AsSpan(0, 10), out var t1, out var e1).Should().BeFalse();
        t1.Should().BeNull();
        e1.Should().NotBeNullOrEmpty();

        TocParser.TryParse(TocBytes((0x10, 1, 500), (0x10, 2, 100), (0x10, 0xAA, 900)), out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(74, "00:00")]
    [InlineData(75, "00:01")]
    [InlineData(75 * 125 + 74, "02:05")]
    public void Duration_RoundsSecondsDown(int sectors, string expected) =>
        TocFormat.Duration(sectors).Should().Be(expected);

    [Fact]
    public void Serializer_RoundTrips()
    {
        TocParser.TryParse(TocBytes((0x10, 1, 0), (0x14, 2, 15000), (0x10, 0xAA, 40000)), out var toc, out _);

        TocSerializer.TryParse(TocSerializer.Serialize(toc!), out var back).Should().BeTrue();

        toc!.IsEquivalentTo(back).Should().BeTrue();
    }

    [Fact]
    public void Hasher_IsDeterministicAndSensitive()
    {
        TocParser.TryParse(TwoTracks, out var a, out _);
        TocParser.TryParse(TwoTracks, out var b, out _);
        TocParser.TryParse(TocBytes((0x10, 1, 0), (0x10, 2, 15001), (0x10, 0xAA, 40000)), out var c, out _);
        TocParser.TryParse(TocBytes((0x10, 1, 0), (0x10, 2, 15000), (0x10, 0xAA, 40001)), out var d, out _);

        TocHasher.Hash(a!).Should().Be(TocHasher.Hash(b!));
        TocHasher.Hash(a!).Should().NotBe(TocHasher.Hash(c!)).And.NotBe(TocHasher.Hash(d!));
    }

    [Fact]
    public void AtipParser_ReadsLeadInStart()
    {
        byte[] atip = [0x00, 0x0A, 0, 0, 0x80, 0x1D, 0x1A, 0, 97, 26, 64, 0];

        AtipParser.TryParse(atip, out var t, out _).Should().BeTrue();

        t.Should().Be(new AtipTime(97, 26, 64));
        t!.ToString().Should().Be("97:26:64");
    }

    [Fact]
    public void AtipParser_RejectsTruncatedAndOutOfRange()
    {
        AtipParser.TryParse(new byte[8], out _, out var e).Should().BeFalse();
        e.Should().NotBeNullOrEmpty();
        AtipParser.TryParse(new byte[] { 0, 0x0A, 0, 0, 0, 0, 0, 0, 97, 60, 0, 0 }, out _, out _).Should().BeFalse();
        AtipParser.TryParse(new byte[] { 0, 0x0A, 0, 0, 0, 0, 0, 0, 97, 0, 75, 0 }, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void AtipParser_TruncatedError_ReportsMinAndActualLength()
    {
        AtipParser.TryParse(new byte[8], out _, out var e).Should().BeFalse();
        e.Should().Contain("8").And.Contain(AtipParser.MinLength.ToString());
    }

    [Fact]
    public void Table_MissingVersion_LogsErrorAndAllLookupsUnknown()
    {
        var errors = new List<string>();

        var table = AtipTable.Parse("""{"entries":[{"atip":"97:15:17","manufacturer":"X","source":"s"}]}""", errors.Add);

        errors.Should().ContainSingle().Which.Should().Contain("version");
        table.Count.Should().Be(0);
        table.Lookup(new AtipTime(97, 15, 17)).Should().Be(AtipTable.UnknownManufacturer);
    }

    [Fact]
    public void EmbeddedTable_LoadsAndExcludesUnconfirmedCodes()
    {
        var errors = new List<string>();
        var table = AtipTable.LoadEmbedded(errors.Add);

        errors.Should().BeEmpty();
        table.Version.Should().BeGreaterThanOrEqualTo(1);
        table.Count.Should().BeGreaterThan(0);
        table.Lookup(new AtipTime(97, 15, 17)).Should().Be("Ritek Co.");
        table.Lookup(new AtipTime(97, 24, 1)).Should().Be(AtipTable.UnknownManufacturer);
        table.Lookup(new AtipTime(97, 34, 23)).Should().Be(AtipTable.UnknownManufacturer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{\"entries\":[]}")]
    [InlineData("{\"version\":0,\"entries\":[]}")]
    public void Table_MissingOrInvalid_LogsAndReturnsUnknown(string? json)
    {
        var errors = new List<string>();

        var table = AtipTable.Parse(json, errors.Add);

        errors.Should().ContainSingle();
        table.Lookup(new AtipTime(97, 15, 17)).Should().Be(AtipTable.UnknownManufacturer);
    }

    [Fact]
    public void Table_DropsEntriesWithoutSourceAndExcludedCodes()
    {
        const string json = """
            {"version":1,"entries":[
              {"atip":"97:10:10","manufacturer":"A","source":"https://x"},
              {"atip":"97:11:11","manufacturer":"B"},
              {"atip":"97:24:01","manufacturer":"C","source":"https://x"}]}
            """;

        var table = AtipTable.Parse(json, _ => { });

        table.Count.Should().Be(1);
        table.Lookup(new AtipTime(97, 10, 10)).Should().Be("A");
        table.Lookup(new AtipTime(97, 11, 11)).Should().Be(AtipTable.UnknownManufacturer);
    }
}
