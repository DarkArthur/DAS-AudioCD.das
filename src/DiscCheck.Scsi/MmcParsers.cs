using System.Text;

namespace DiscCheck.Scsi;

/// <summary>Standard INQUIRY data (vendor, product, firmware revision).</summary>
public sealed record InquiryData(string Vendor, string Product, string Revision);

/// <summary>Subset of MODE SENSE page 0x2A (CD/DVD capabilities and mechanical status).</summary>
public sealed record ModePage2AData(
    bool C2PointersSupported,
    int MaxReadSpeedKBps,
    int CurrentReadSpeedKBps,
    IReadOnlyList<int> SupportedSpeedsKBps)
{
    public const int KBpsPer1x = 176;
    public double MaxReadSpeedX => MaxReadSpeedKBps / (double)KBpsPer1x;
    public double CurrentReadSpeedX => CurrentReadSpeedKBps / (double)KBpsPer1x;
    public IReadOnlyList<double> SupportedSpeedsX => SupportedSpeedsKBps.Select(k => k / (double)KBpsPer1x).ToArray();
}

/// <summary>Pure parsers (byte[] to value, no I/O).</summary>
public static class MmcParsers
{
    /// <summary>Minimum INQUIRY response length.</summary>
    public const int InquiryMinLength = 36;

    /// <summary>Minimum MODE SENSE(10) page 0x2A response length (8-byte header + 16 bytes of page data).</summary>
    public const int ModePage2AMinLength = 24;

    /// <summary>Minimum fixed-format sense data length.</summary>
    public const int SenseMinLength = 14;

    /// <summary>Minimum READ TOC/PMA/ATIP response length (4-byte header: data length + first/last).</summary>
    public const int TocHeaderMinLength = 4;

    /// <summary>Minimum GET CONFIGURATION response length (8-byte feature header).</summary>
    public const int GetConfigurationMinLength = 8;

    public static MmcResult<InquiryData> ParseInquiry(ReadOnlySpan<byte> data)
    {
        if (data.Length < InquiryMinLength)
            return MmcResult<InquiryData>.Fail(new MmcError.ParseError("INQUIRY", InquiryMinLength, data.Length));

        return MmcResult<InquiryData>.Ok(new InquiryData(
            Ascii(data.Slice(8, 8)),
            Ascii(data.Slice(16, 16)),
            Ascii(data.Slice(32, 4))));
    }

    public static MmcResult<ModePage2AData> ParseModePage2A(ReadOnlySpan<byte> data)
    {
        const string name = "MODE SENSE(10) 0x2A";
        if (data.Length < ModePage2AMinLength)
            return MmcResult<ModePage2AData>.Fail(new MmcError.ParseError(name, ModePage2AMinLength, data.Length));

        // Mode parameter header (10): bytes 6-7 = block descriptor length; the page follows the descriptors.
        int page = 8 + ((data[6] << 8) | data[7]);
        int minLen = Math.Max(ModePage2AMinLength, page + 16);
        if (data.Length < minLen)
            return MmcResult<ModePage2AData>.Fail(new MmcError.ParseError(name, minLen, data.Length));

        // Page byte 5, bit 4 = C2 Pointers supported. Bytes 8-9 max read speed, 14-15 current read speed (kB/s).
        bool c2 = (data[page + 5] & 0x10) != 0;
        int maxRead = (data[page + 8] << 8) | data[page + 9];
        int curRead = (data[page + 14] << 8) | data[page + 15];

        // Only the speeds in the fixed part of the page are reported (max and current read speed).
        var speeds = new List<int>();
        if (maxRead > 0) speeds.Add(maxRead);
        if (curRead > 0 && curRead != maxRead) speeds.Add(curRead);

        return MmcResult<ModePage2AData>.Ok(new ModePage2AData(c2, maxRead, curRead, speeds));
    }

    /// <summary>Parses fixed-format sense data (response code 0x70/0x71): Key = byte 2 low nibble, ASC = byte 12, ASCQ = byte 13.</summary>
    public static MmcResult<SenseData> ParseSense(ReadOnlySpan<byte> data)
    {
        if (data.Length < SenseMinLength)
            return MmcResult<SenseData>.Fail(new MmcError.ParseError("SENSE", SenseMinLength, data.Length));

        return MmcResult<SenseData>.Ok(new SenseData((byte)(data[2] & 0x0F), data[12], data[13]));
    }

    /// <summary>
    /// Validates a READ TOC/PMA/ATIP response and returns it trimmed to the length declared in its header
    /// (bytes 0-1 + 2), capped to what was received. Minimum 4 bytes. Detailed TOC/ATIP parsing belongs to M2.
    /// </summary>
    public static MmcResult<byte[]> ParseTocPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < TocHeaderMinLength)
            return MmcResult<byte[]>.Fail(new MmcError.ParseError("READ TOC/PMA/ATIP", TocHeaderMinLength, data.Length));

        int declared = ((data[0] << 8) | data[1]) + 2;
        return MmcResult<byte[]>.Ok(data[..Math.Min(declared, data.Length)].ToArray());
    }

    /// <summary>Validates a GET CONFIGURATION response (minimum 8 bytes) and returns it trimmed to the declared data length (bytes 0-3 + 4).</summary>
    public static MmcResult<byte[]> ParseConfigurationPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < GetConfigurationMinLength)
            return MmcResult<byte[]>.Fail(new MmcError.ParseError("GET CONFIGURATION", GetConfigurationMinLength, data.Length));

        long declared = (((long)data[0] << 24) | ((long)data[1] << 16) | ((long)data[2] << 8) | data[3]) + 4;
        return MmcResult<byte[]>.Ok(data[..(int)Math.Min(declared, data.Length)].ToArray());
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : ' ');
        return sb.ToString().TrimEnd();
    }
}
