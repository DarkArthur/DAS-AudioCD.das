using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

public class ComparisonServiceTests
{
    private static ComparisonInput Input(long id, long driveId = 1, double speed = 8, params ScanSample[] samples) =>
        new(new ScanRecord(id, 1, driveId, DateTimeOffset.UnixEpoch, speed, ScannerKind.LiteOn, Verdict.OK,
                           null, null, null, null, null, null),
            samples, "drive", "CD-R");

    [Fact]
    public void Nine_scans_keep_the_first_eight_and_report_the_limit()
    {
        var all = Enumerable.Range(1, 9).Select(i => Input(i)).ToList();
        var r = ComparisonService.Compare(all);
        r.Accepted.Should().HaveCount(8);
        r.Accepted.Select(a => a.Scan.Id).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        r.LimitMessage.Should().NotBeNull();
    }

    [Fact]
    public void Single_scan_asks_for_two_and_does_not_overlay()
    {
        var r = ComparisonService.Compare([Input(1)]);
        r.MinimumMessage.Should().NotBeNull();
        r.CanOverlay.Should().BeFalse();
    }

    [Fact]
    public void Different_drive_or_speed_gives_the_warning()
    {
        ComparisonService.Compare([Input(1), Input(2, driveId: 2)]).MismatchWarning.Should().NotBeNull();
        ComparisonService.Compare([Input(1), Input(2, speed: 16)]).MismatchWarning.Should().NotBeNull();
        ComparisonService.Compare([Input(1), Input(2)]).MismatchWarning.Should().BeNull();
    }

    [Fact]
    public void Null_values_are_excluded_from_the_row()
    {
        var row = ComparisonService.Row(0, Input(1, 1, 8,
            new ScanSample(0, 0, 10, 1, null, null), new ScanSample(0, 1, null, 2, null, null), new ScanSample(0, 2, 20, 0, null, null)));
        row.C1Mean.Should().Be(15);
        row.C1Max.Should().Be(20);
        row.C2Total.Should().Be(3);
        row.CuTotal.Should().BeNull();
    }

    [Fact]
    public void SpeedProposer_covers_range_fallback_and_empty()
    {
        int K(int x) => ScanSpeeds.ToKBps(x);
        SpeedProposer.Propose([K(4), K(8), K(12), K(24), K(52)]).SpeedsX.Should().Equal(4, 8, 12);
        SpeedProposer.Propose([K(24), K(52)]).SpeedsX.Should().Equal(24);
        SpeedProposer.Propose([K(2), K(24)]).SpeedsX.Should().Equal(2);
        var none = SpeedProposer.Propose([]);
        none.SpeedsX.Should().BeEmpty();
        none.Message.Should().NotBeNull();
    }
}
