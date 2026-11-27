using DiscCheck.Scsi;
using FluentAssertions;
using Xunit.Abstractions;

namespace DiscCheck.Hardware.Tests;

/// <summary>
/// M1 hardware tests on a Lite-On iHAS124 behind an ICY BOX USB bridge. Excluded from the default run
/// (<c>dotnet test --filter "Category!=Hardware"</c>); skipped when the drive or administrator rights are missing.
/// Requirements 4.4, 26.1, 27.2.
/// </summary>
[Trait("Category", "Hardware")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class M1HardwareTests(ITestOutputHelper output)
{
    [LiteOnHardwareFact]
    public void Inquiry_identifies_LiteOn_iHAS124()
    {
        var drive = LiteOnDrive.Find()!;
        using var transport = new WinScsiPassThrough(drive.Letter);

        var result = MmcCommands.Inquiry(transport);

        result.IsSuccess.Should().BeTrue(because: "INQUIRY must pass through the bridge: {0}", result.Error);
        var inq = result.Value;
        output.WriteLine($"INQUIRY: '{inq.Vendor}' '{inq.Product}' '{inq.Revision}'");
        inq.Vendor.Trim().Should().StartWith(LiteOnDrive.VendorPrefix, because: "vendor must be LITE-ON");
        inq.Product.Trim().Should().StartWith(LiteOnDrive.ModelPrefix);
    }

    [LiteOnHardwareFact]
    public void ModeSense_page_2A_returns_capabilities()
    {
        var drive = LiteOnDrive.Find()!;
        using var transport = new WinScsiPassThrough(drive.Letter);

        var result = MmcCommands.ModeSense10Page2A(transport);

        result.IsSuccess.Should().BeTrue(because: "MODE SENSE 0x2A must pass through the bridge: {0}", result.Error);
        var page = result.Value;
        output.WriteLine($"2A: C2={page.C2PointersSupported}, maxRead={page.MaxReadSpeedKBps} kB/s, speeds=[{string.Join(",", page.SupportedSpeedsKBps)}]");
        page.MaxReadSpeedKBps.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Vendor probe <c>cmd_cd_errc_init</c> ("new" variant, <c>cmd_cd_errc_init_new</c>) from QPxTool
    /// <c>plugins/liteon/qscan_cmd.cpp</c> (fork speed47/qpxtool). CDBs are copied from that source:
    /// SEEK(10) <c>2B</c> with LBA 0 (<c>seek()</c> in <c>lib/qpxtransport/qpx_mmc.cpp</c>, flags 0), then the 12-byte
    /// <c>F3 0E</c> probe with a 0x10-byte data-in phase. The outcome (accepted/rejected by the bridge) is
    /// reported, not asserted: both are valid findings recorded in <c>docs/risks.md</c>. Needs a disc in the drive.
    /// </summary>
    [LiteOnHardwareFact(requireDisc: true)]
    public void Vendor_cd_errc_init_probe_via_bridge_reports_outcome()
    {
        var drive = LiteOnDrive.Find()!;
        using var transport = new WinScsiPassThrough(drive.Letter);
        var timeout = MmcCommands.DefaultTimeout;

        try
        {
            // seek(dev, 0): MMC_SEEK = 0x2B, LBA in bytes 2-5, flags in byte 9, no data phase.
            var seekCdb = new byte[10];
            seekCdb[0] = 0x2B;
            var seek = transport.Execute(seekCdb, Span<byte>.Empty, DataDirection.None, timeout);
            output.WriteLine($"SEEK(0): {Describe(seek)}");

            // cmd_cd_errc_init_new probe: F3 0E ... byte 11 = 0, READ 0x10 bytes.
            var probeCdb = new byte[12];
            probeCdb[0] = 0xF3;
            probeCdb[1] = 0x0E;
            var data = new byte[0x10];
            var probe = transport.Execute(probeCdb, data, DataDirection.In, timeout);
            output.WriteLine($"VENDOR cd_errc_init (F3 0E): {(probe.IsGood ? "ACCETTATO" : "RIFIUTATO")} - {Describe(probe)}");
            if (probe.IsGood)
                output.WriteLine($"Response: {Convert.ToHexString(data.AsSpan(0, Math.Min(probe.BytesTransferred, data.Length)))}");

            // The bridge must answer (status or sense) without a transport-level failure such as a timeout.
            probe.Error.Should().BeNull(because: "the bridge must answer the vendor command, accepted or rejected");
        }
        finally
        {
            EndScan(transport);
        }
    }

    /// <summary>
    /// End of scan, always run. Per <c>cmd_cd_errc_end</c> in qscan_cmd.cpp the "new" ERRC variant has no end
    /// command (it returns 0 immediately); the old variant ends with <c>DF A3 01</c> and is not used here.
    /// Then TEST UNIT READY checks that the drive is back in a normal state.
    /// </summary>
    private void EndScan(IScsiTransport transport)
    {
        var tur = MmcCommands.TestUnitReady(transport);
        output.WriteLine($"End scan / TEST UNIT READY: {(tur.IsSuccess ? "OK" : tur.Error?.ToString())}");
    }

    private static string Describe(ScsiResult r) =>
        r.Error is not null ? $"transport error {r.Error}"
        : $"status 0x{r.ScsiStatus:X2}" + (r.Sense is { } s ? $", sense {s.Key:X}/{s.Asc:X2}/{s.Ascq:X2}" : "");
}
