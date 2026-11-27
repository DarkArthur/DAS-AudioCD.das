using DiscCheck.Scsi;
using FluentAssertions;
using Xunit;

namespace DiscCheck.Scsi.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WinScsiPassThroughTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(-5, 30)]
    [InlineData(0.2, 1)]
    [InlineData(45, 45)]
    [InlineData(9999, 600)]
    public void ClampTimeout_keeps_values_within_1_and_600_seconds(double seconds, double expected) =>
        WinScsiPassThrough.ClampTimeout(TimeSpan.FromSeconds(seconds)).TotalSeconds.Should().Be(expected);

    [Fact]
    public void ResolveTimeout_replaces_only_the_default_with_the_configured_value()
    {
        Func<TimeSpan> configured = () => TimeSpan.FromSeconds(90);
        WinScsiPassThrough.ResolveTimeout(WinScsiPassThrough.DefaultTimeout, configured).Should().Be(TimeSpan.FromSeconds(90));
        WinScsiPassThrough.ResolveTimeout(WinScsiPassThrough.ScanStartTimeout, configured).Should().Be(WinScsiPassThrough.ScanStartTimeout);
        WinScsiPassThrough.ResolveTimeout(WinScsiPassThrough.DefaultTimeout, null).Should().Be(WinScsiPassThrough.DefaultTimeout);
    }

    [Fact]
    public void ParseSense_reads_fixed_format()
    {
        var sense = new byte[32];
        sense[0] = 0x70; sense[2] = 0x05; sense[12] = 0x24; sense[13] = 0x00;
        WinScsiPassThrough.ParseSense(sense, 18).Should().Be(new SenseData(5, 0x24, 0));
    }

    [Fact]
    public void ParseSense_returns_null_when_drive_reports_no_sense() =>
        WinScsiPassThrough.ParseSense(new byte[32], 0).Should().BeNull();

    [Fact]
    public void Invalid_drive_letter_yields_OpenFailed_value_without_exception()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = new WinScsiPassThrough('1');
        t.Execute(new byte[6], [], DataDirection.None, TimeSpan.FromSeconds(1)).Error
            .Should().BeOfType<TransportError.OpenFailed>();
    }
}
