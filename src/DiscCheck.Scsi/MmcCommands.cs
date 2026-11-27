namespace DiscCheck.Scsi;

/// <summary>
/// Typed MMC/SPC commands on top of <see cref="IScsiTransport"/>. Each method builds the CDB, executes it and
/// parses the response. On a non-GOOD status or transport error the data is never parsed and a
/// <see cref="MmcError.CommandError"/> is returned; on a short response a <see cref="MmcError.ParseError"/>
/// is returned. No partial values are produced and no exceptions are raised for device errors
/// (only argument validation throws, before anything is sent).
/// Minimum response lengths: INQUIRY 36; MODE SENSE(10) 0x2A 24; sense 14; READ TOC/PMA/ATIP 4;
/// GET CONFIGURATION 8; READ CD count x bytes-per-sector; SET CD SPEED, TEST UNIT READY, START STOP UNIT: no data.
/// </summary>
public static class MmcCommands
{
    public const int KBpsPer1x = 176;

    /// <summary>SET CD SPEED value meaning "maximum speed".</summary>
    public const int MaxSpeed = 0xFFFF;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // READ CD flag bits (CDB byte 9) and sector sizes for CD-DA.
    public const byte ReadCdUserData = 0x10;
    public const byte ReadCdC2Pointers = 0x02;      // 294 bytes of C2 error bits
    public const byte ReadCdC2AndBlockError = 0x04; // 296 bytes: block error byte + 294 C2 bytes + padding byte
    public const int CdDaSectorSize = 2352;

    /// <summary>INQUIRY (0x12). Minimum response: 36 bytes.</summary>
    public static MmcResult<InquiryData> Inquiry(IScsiTransport transport, TimeSpan? timeout = null)
    {
        var cdb = new byte[6];
        cdb[0] = 0x12;
        cdb[4] = 96;
        var buf = new byte[96];
        return Run("INQUIRY", transport, cdb, buf, DataDirection.In, timeout, MmcParsers.ParseInquiry);
    }

    /// <summary>MODE SENSE(10) (0x5A) for <paramref name="page"/>. Parsed result only for page 0x2A; use <see cref="ModeSense10Raw"/> otherwise.</summary>
    public static MmcResult<ModePage2AData> ModeSense10Page2A(IScsiTransport transport, TimeSpan? timeout = null)
    {
        var cdb = ModeSense10Cdb(0x2A, 128);
        var buf = new byte[128];
        return Run("MODE SENSE(10)", transport, cdb, buf, DataDirection.In, timeout, MmcParsers.ParseModePage2A);
    }

    /// <summary>MODE SENSE(10) (0x5A) returning the raw response. Minimum: 8 bytes (mode parameter header).</summary>
    public static MmcResult<byte[]> ModeSense10Raw(IScsiTransport transport, byte page, ushort allocation = 256, TimeSpan? timeout = null)
    {
        var cdb = ModeSense10Cdb(page, allocation);
        var buf = new byte[allocation];
        return Run("MODE SENSE(10)", transport, cdb, buf, DataDirection.In, timeout, d =>
            d.Length < 8
                ? MmcResult<byte[]>.Fail(new MmcError.ParseError("MODE SENSE(10)", 8, d.Length))
                : MmcResult<byte[]>.Ok(d.ToArray()));
    }

    /// <summary>GET CONFIGURATION (0x46). Minimum response: 8 bytes (feature header).</summary>
    public static MmcResult<byte[]> GetConfiguration(IScsiTransport transport, ushort startingFeature = 0, byte requestType = 0, ushort allocation = 512, TimeSpan? timeout = null)
    {
        var cdb = new byte[10];
        cdb[0] = 0x46;
        cdb[1] = (byte)(requestType & 0x03);
        cdb[2] = (byte)(startingFeature >> 8);
        cdb[3] = (byte)startingFeature;
        cdb[7] = (byte)(allocation >> 8);
        cdb[8] = (byte)allocation;
        var buf = new byte[allocation];
        return Run("GET CONFIGURATION", transport, cdb, buf, DataDirection.In, timeout, MmcParsers.ParseConfigurationPayload);
    }

    /// <summary>
    /// READ TOC/PMA/ATIP (0x43). <paramref name="format"/> is the format code (0 = TOC, 2 = full TOC, 3 = PMA, 4 = ATIP).
    /// Returns the response trimmed to its declared length. Minimum response: 4 bytes.
    /// </summary>
    public static MmcResult<byte[]> ReadToc(IScsiTransport transport, byte format, bool msf = false, byte trackOrSession = 0, ushort allocation = 2048, TimeSpan? timeout = null)
    {
        var cdb = new byte[10];
        cdb[0] = 0x43;
        cdb[1] = (byte)(msf ? 0x02 : 0x00);
        cdb[2] = (byte)(format & 0x0F);
        cdb[6] = trackOrSession;
        cdb[7] = (byte)(allocation >> 8);
        cdb[8] = (byte)allocation;
        var buf = new byte[allocation];
        return Run("READ TOC/PMA/ATIP", transport, cdb, buf, DataDirection.In, timeout, MmcParsers.ParseTocPayload);
    }

    /// <summary>
    /// READ CD (0xBE) of <paramref name="count"/> CD-DA sectors (expected sector type 0x04 in CDB byte 1).
    /// <paramref name="flags"/> is CDB byte 9 (e.g. <see cref="ReadCdUserData"/> | <see cref="ReadCdC2Pointers"/>).
    /// Returns exactly count x bytes-per-sector bytes; fewer bytes transferred is a <see cref="MmcError.ParseError"/>.
    /// </summary>
    public static MmcResult<byte[]> ReadCd(IScsiTransport transport, uint lba, int count, byte flags, byte subChannel = 0, TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (count > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(count), count, "Sector count exceeds 24 bits.");

        int perSector = ReadCdSectorSize(flags, subChannel);
        long total = (long)perSector * count;
        if (total > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(count), count, "Transfer too large.");

        var cdb = new byte[12];
        cdb[0] = 0xBE;
        cdb[1] = 0x04;
        cdb[2] = (byte)(lba >> 24);
        cdb[3] = (byte)(lba >> 16);
        cdb[4] = (byte)(lba >> 8);
        cdb[5] = (byte)lba;
        cdb[6] = (byte)(count >> 16);
        cdb[7] = (byte)(count >> 8);
        cdb[8] = (byte)count;
        cdb[9] = flags;
        cdb[10] = subChannel;

        var buf = new byte[total];
        int expected = (int)total;
        return Run("READ CD", transport, cdb, buf, DataDirection.In, timeout, d =>
            d.Length < expected
                ? MmcResult<byte[]>.Fail(new MmcError.ParseError("READ CD", expected, d.Length))
                : MmcResult<byte[]>.Ok(d[..expected].ToArray()));
    }

    /// <summary>Bytes per sector returned by READ CD for the given CDB byte 9 flags and sub-channel selection.</summary>
    public static int ReadCdSectorSize(byte flags, byte subChannel)
    {
        int size = (flags & ReadCdUserData) != 0 ? CdDaSectorSize : 0;
        size += ((flags >> 1) & 0x03) switch { 1 => 294, 2 => 296, _ => 0 };
        size += (subChannel & 0x07) switch { 1 => 96, 2 => 16, 4 => 96, _ => 0 };
        return size;
    }

    /// <summary>
    /// SET CD SPEED (0xBB). <paramref name="kBps"/> is the read speed in kB/s (1x = 176 kB/s, 8x = 1408),
    /// 0xFFFF = maximum. 0 or any value above 0xFFFE other than 0xFFFF throws <see cref="ArgumentException"/>
    /// before anything is sent. No data phase.
    /// </summary>
    public static MmcResult<bool> SetCdSpeed(IScsiTransport transport, int kBps, TimeSpan? timeout = null)
    {
        if (kBps != MaxSpeed && (kBps <= 0 || kBps > 0xFFFE))
            throw new ArgumentException($"Invalid CD speed: {kBps} kB/s (valid: 1-65534 or 65535 for maximum).", nameof(kBps));

        var cdb = new byte[12];
        cdb[0] = 0xBB;
        cdb[2] = (byte)(kBps >> 8);
        cdb[3] = (byte)kBps;
        cdb[4] = 0xFF; // write speed: maximum
        cdb[5] = 0xFF;
        return RunNoData("SET CD SPEED", transport, cdb, timeout);
    }

    /// <summary>TEST UNIT READY (0x00). No data phase.</summary>
    public static MmcResult<bool> TestUnitReady(IScsiTransport transport, TimeSpan? timeout = null)
        => RunNoData("TEST UNIT READY", transport, new byte[6], timeout);

    /// <summary>START STOP UNIT (0x1B). <paramref name="start"/> spins up, <paramref name="loadEject"/> loads/ejects. No data phase.</summary>
    public static MmcResult<bool> StartStopUnit(IScsiTransport transport, bool start, bool loadEject = false, TimeSpan? timeout = null)
    {
        var cdb = new byte[6];
        cdb[0] = 0x1B;
        cdb[4] = (byte)((loadEject ? 0x02 : 0) | (start ? 0x01 : 0));
        return RunNoData("START STOP UNIT", transport, cdb, timeout);
    }

    private static byte[] ModeSense10Cdb(byte page, ushort allocation)
    {
        var cdb = new byte[10];
        cdb[0] = 0x5A;
        cdb[1] = 0x08; // DBD: no block descriptors
        cdb[2] = (byte)(page & 0x3F);
        cdb[7] = (byte)(allocation >> 8);
        cdb[8] = (byte)allocation;
        return cdb;
    }

    private static MmcResult<bool> RunNoData(string name, IScsiTransport transport, byte[] cdb, TimeSpan? timeout)
    {
        var r = transport.Execute(cdb, Span<byte>.Empty, DataDirection.None, timeout ?? DefaultTimeout);
        return r.IsGood ? MmcResult<bool>.Ok(true) : MmcResult<bool>.Fail(ToError(name, r));
    }

    private delegate MmcResult<T> Parser<T>(ReadOnlySpan<byte> data);

    private static MmcResult<T> Run<T>(string name, IScsiTransport transport, byte[] cdb, byte[] buffer,
        DataDirection direction, TimeSpan? timeout, Parser<T> parse)
    {
        var r = transport.Execute(cdb, buffer, direction, timeout ?? DefaultTimeout);
        if (!r.IsGood)
            return MmcResult<T>.Fail(ToError(name, r));

        int n = Math.Clamp(r.BytesTransferred, 0, buffer.Length);
        return parse(buffer.AsSpan(0, n));
    }

    private static MmcError ToError(string name, ScsiResult r)
        => new MmcError.CommandError(name, r.ScsiStatus, r.Sense, r.Error);
}
