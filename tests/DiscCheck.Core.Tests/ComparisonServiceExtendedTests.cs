using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

/// <summary>Task 15.6: Comparator and SpeedProposer edge cases (Requirements 19.2, 19.4-19.7).</summary>
public class ComparisonServiceExtendedTests
{
    private static ComparisonInput Input(long id, long driveId = 1, double speed = 8, Verdict verdict = Verdict.OK,
                                         params ScanSample[] samples) =>
        new(new ScanRecord(id, 1, driveId, DateTimeOffset.UnixEpoch, speed, ScannerKind.LiteOn, verdict,
                           null, null, null, null, null, null),
            samples, "drive", "CD-R");

    private static int K(int x) => ScanSpeeds.ToKBps(x);

    // ---- Comparator: limit of 8 (19.2) ----

    [Fact]
    public void Exactly_eight_scans_are_all_kept_without_limit_message()
    {
        var r = ComparisonService.Compare(Enumerable.Range(1, 8).Select(i => Input(i)).ToList());
        r.Accepted.Should().HaveCount(8);
        r.Rows.Should().HaveCount(8);
        r.LimitMessage.Should().BeNull();
        r.MinimumMessage.Should().BeNull();
        r.CanOverlay.Should().BeTrue();
    }

    [Fact]
    public void Nine_scans_drop_the_ninth_from_accepted_and_rows_and_still_overlay()
    {
        var r = ComparisonService.Compare(Enumerable.Range(1, 9).Select(i => Input(i)).ToList());
        r.Rows.Select(x => x.ScanId).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        r.Rows.Select(x => x.Index).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
        r.LimitMessage.Should().Be(ComparisonService.LimitMessage).And.Contain("8");
        r.CanOverlay.Should().BeTrue();
    }

    [Fact]
    public void Mismatch_is_evaluated_on_the_first_eight_only()
    {
        var all = Enumerable.Range(1, 8).Select(i => Input(i)).Append(Input(9, driveId: 2)).ToList();
        ComparisonService.Compare(all).MismatchWarning.Should().BeNull();
    }

    // ---- Comparator: single scan (19.5) ----

    [Fact]
    public void Single_scan_shows_minimum_message_no_warning_and_no_limit()
    {
        var r = ComparisonService.Compare([Input(1)]);
        r.MinimumMessage.Should().Be(ComparisonService.MinimumMessage);
        r.MismatchWarning.Should().BeNull();
        r.LimitMessage.Should().BeNull();
        r.CanOverlay.Should().BeFalse();
        r.Accepted.Should().HaveCount(1);
    }

    [Fact]
    public void Empty_selection_does_not_overlay_and_has_no_messages()
    {
        var r = ComparisonService.Compare([]);
        r.CanOverlay.Should().BeFalse();
        r.Rows.Should().BeEmpty();
        r.MinimumMessage.Should().BeNull();
    }

    [Fact]
    public void Speeds_within_tolerance_do_not_warn()
    {
        ComparisonService.Compare([Input(1, speed: 8), Input(2, speed: 8.04)]).MismatchWarning.Should().BeNull();
        ComparisonService.Compare([Input(1, speed: 8), Input(2, speed: 8.2)]).MismatchWarning
            .Should().Be(ComparisonService.MismatchWarning);
    }

    // ---- Comparator: null exclusion (19.4) ----

    [Fact]
    public void Row_excludes_null_c1_from_mean_and_max()
    {
        var row = ComparisonService.Row(0, Input(1, 1, 8, Verdict.WARN,
            new ScanSample(0, 0, 4, 0, 1, null), new ScanSample(0, 1, null, 0, null, null), new ScanSample(0, 2, 7, 0, 2, null)));
        row.C1Mean.Should().Be(5.5);
        row.C1Max.Should().Be(7);
        row.CuTotal.Should().Be(3);
        row.Verdict.Should().Be(Verdict.WARN);
    }

    [Fact]
    public void Row_with_all_values_null_gives_null_not_zero()
    {
        var row = ComparisonService.Row(0, Input(1, 1, 8, Verdict.OK,
            new ScanSample(0, 0, null, 0, null, null), new ScanSample(0, 1, null, 0, null, null)));
        row.C1Mean.Should().BeNull();
        row.C1Max.Should().BeNull();
        row.CuTotal.Should().BeNull();
        row.C2Total.Should().Be(0);
    }

    [Fact]
    public void Row_without_samples_has_all_values_null()
    {
        var row = ComparisonService.Row(3, Input(5));
        row.Index.Should().Be(3);
        row.ScanId.Should().Be(5);
        row.C1Mean.Should().BeNull();
        row.C1Max.Should().BeNull();
        row.C2Total.Should().BeNull();
        row.CuTotal.Should().BeNull();
    }

    [Fact]
    public void Row_with_zero_values_keeps_them_in_the_mean()
    {
        var row = ComparisonService.Row(0, Input(1, 1, 8, Verdict.OK,
            new ScanSample(0, 0, 0, 0, null, null), new ScanSample(0, 1, 3, 0, null, null)));
        row.C1Mean.Should().Be(1.5);
        row.C1Max.Should().Be(3);
    }

    [Fact]
    public void Value_returns_the_selected_metric_or_null()
    {
        var s = new ScanSample(0, 0, 9, 4, null, null);
        ComparisonService.Value(s, CompareMetric.C1).Should().Be(9);
        ComparisonService.Value(s, CompareMetric.C2).Should().Be(4);
        ComparisonService.Value(s, CompareMetric.CU).Should().BeNull();
    }

    [Fact]
    public void Legend_contains_medium_and_speed_and_falls_back_for_empty_medium()
    {
        var withMedium = ComparisonService.Legend(Input(1, speed: 8));
        withMedium.Should().Contain("CD-R").And.EndWith("8x");
        var noMedium = ComparisonService.Legend(Input(1) with { Medium = "  " });
        noMedium.Should().Contain(ComparisonService.NotAvailable);
    }

    // ---- SpeedProposer (19.6, 19.7) ----

    [Fact]
    public void Proposer_includes_both_bounds_of_the_range()
    {
        SpeedProposer.Propose([K(4), K(12), K(16)]).SpeedsX.Should().Equal(4, 12);
    }

    [Fact]
    public void Proposer_starts_from_the_minimum_supported_even_below_4x()
    {
        SpeedProposer.Propose([K(2), K(4), K(8), K(24)]).SpeedsX.Should().Equal(2, 4, 8);
    }

    [Fact]
    public void Proposer_sorts_and_deduplicates_input()
    {
        SpeedProposer.Propose([K(12), K(4), K(8), K(8)]).SpeedsX.Should().Equal(4, 8, 12);
    }

    [Fact]
    public void Proposer_falls_back_to_minimum_when_nothing_is_in_4_to_12()
    {
        SpeedProposer.Propose([K(16), K(24), K(52)]).SpeedsX.Should().Equal(16);
        SpeedProposer.Propose([K(1), K(2), K(3), K(24)]).SpeedsX.Should().Equal(1);
    }

    [Fact]
    public void Proposer_with_no_supported_speed_gives_message_and_nothing_else()
    {
        foreach (var input in new[] { Array.Empty<int>(), new[] { 0 }, new[] { -5, 0 } })
        {
            var p = SpeedProposer.Propose(input);
            p.SpeedsX.Should().BeEmpty();
            p.Message.Should().Be(SpeedProposer.UndeterminableMessage);
        }
    }

    [Fact]
    public void Proposer_has_no_message_when_speeds_are_proposed()
    {
        SpeedProposer.Propose([K(8)]).Message.Should().BeNull();
    }
}
