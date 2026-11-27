using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Verify;

/// <summary>A READ CD failed during extraction (Requirement 14.17). The message names track, LBA and sense.</summary>
public sealed class ReadCdException(int trackNo, long lba, string detail, MmcError error)
    : Exception($"Lettura fallita: traccia {trackNo}, LBA {lba}: {detail}")
{
    public int TrackNo { get; } = trackNo;
    public long Lba { get; } = lba;
    public MmcError Error { get; } = error;
}

/// <summary>
/// How a track is extracted. <paramref name="UseC2"/> is false when the drive has no C2 or <c>drives.c2_reliable = 0</c>
/// (Requirement 13.5): the reading is then plain user data and C2 never triggers re-reads.
/// </summary>
/// <param name="TotalSectors">Disc sectors (lead-out LBA), used to plan the Cache_Defeat far area.</param>
public sealed record ExtractOptions(bool UseC2, int MaxRereads, int? CacheKb, long TotalSectors);

/// <summary>Extracted track audio (interleaved L,R 16-bit) with the reliability notes of Requirement 14.18.</summary>
/// <param name="UnreliableLbas">First LBA of each block that still reported C2 after the last re-read.</param>
/// <param name="CacheDefeatLimited">At least one re-read ran without Cache_Defeat because no far area was available (Req. 12.9).</param>
public sealed record ExtractedTrack(short[] Pcm, IReadOnlyList<int> UnreliableLbas, bool CacheDefeatLimited);

/// <summary>
/// READ CD extraction of one track with C2 pointers and re-reads with Cache_Defeat up to N times
/// (Requirements 14.5, 14.6, 14.17, 14.18).
/// </summary>
public sealed class AudioExtractor
{
    /// <summary>Sectors per READ CD (26 or 27 per Requirement 7.3).</summary>
    public const int BlockSectors = 26;

    public const int FramesPerSector = 588;

    private const byte FlagsWithC2 = MmcCommands.ReadCdUserData | MmcCommands.ReadCdC2Pointers; // 0x12
    private const int C2Bytes = C2BitCounter.PointerBytes;

    private readonly IScsiTransport _transport;
    private readonly ILogger _log;

    public AudioExtractor(IScsiTransport transport, ILogger? log = null)
    {
        _transport = transport;
        _log = log ?? Log.Logger;
    }

    /// <param name="progress">Receives the fraction (0-1) of sectors read, once per block.</param>
    /// <exception cref="ReadCdException">A READ CD failed (not retried).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public ExtractedTrack ExtractTrack(int trackNo, int startLba, int sectorCount, ExtractOptions options,
                                       Action<double>? progress, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sectorCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRereads, 0);

        var pcm = new short[(long)sectorCount * FramesPerSector * 2];
        var unreliable = new List<int>();
        bool limited = false;

        byte flags = options.UseC2 ? FlagsWithC2 : MmcCommands.ReadCdUserData;
        int stride = MmcCommands.ReadCdSectorSize(flags, 0);

        for (int done = 0; done < sectorCount; done += BlockSectors)
        {
            ct.ThrowIfCancellationRequested();
            int count = Math.Min(BlockSectors, sectorCount - done);
            int lba = startLba + done;

            var data = ReadBlock(trackNo, lba, count, flags);
            int c2 = options.UseC2 ? CountC2(data, count, stride) : 0;

            for (int attempt = 0; options.UseC2 && c2 > 0 && attempt < options.MaxRereads; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                limited |= !DefeatCache(trackNo, lba, options, ct);
                data = ReadBlock(trackNo, lba, count, flags);
                c2 = CountC2(data, count, stride);
            }

            if (c2 > 0)
            {
                unreliable.Add(lba);
                _log.Warning("Traccia {Track}: blocco a LBA {Lba} ancora con {C2} C2 dopo {N} riletture", trackNo, lba, c2, options.MaxRereads);
            }

            for (int s = 0; s < count; s++)
                Buffer.BlockCopy(data, s * stride, pcm, (done + s) * FramesPerSector * 4, MmcCommands.CdDaSectorSize);

            progress?.Invoke((double)(done + count) / sectorCount);
        }

        return new ExtractedTrack(pcm, unreliable, limited);
    }

    private static int CountC2(byte[] data, int sectors, int stride)
    {
        int total = 0;
        for (int s = 0; s < sectors; s++)
            total += C2BitCounter.CountC2(data.AsSpan(s * stride + MmcCommands.CdDaSectorSize, C2Bytes));
        return total;
    }

    private byte[] ReadBlock(int trackNo, int lba, int count, byte flags)
    {
        var r = MmcCommands.ReadCd(_transport, (uint)lba, count, flags);
        if (r.IsSuccess) return r.Value;

        string detail = ScsiErrorMessages.Describe(r.Error!);
        _log.Error("READ CD fallito: traccia {Track}, LBA {Lba}: {Detail}", trackNo, lba, detail);
        throw new ReadCdException(trackNo, lba, detail, r.Error!);
    }

    /// <summary>
    /// Reads the far area planned by <see cref="CacheDefeatPlanner"/> so the next read of the block cannot be served by the
    /// drive cache. Returns false when no area is available or it cannot be read: the re-read then runs without Cache_Defeat.
    /// </summary>
    private bool DefeatCache(int trackNo, int blockLba, ExtractOptions options, CancellationToken ct)
    {
        var plan = CacheDefeatPlanner.Plan(blockLba, options.TotalSectors, options.CacheKb);
        if (plan.Area is not { } area)
        {
            _log.Warning("Cache_Defeat non applicabile alla traccia {Track}, LBA {Lba}: mancano {Missing} settori", trackNo, blockLba, plan.MissingSectors);
            return false;
        }

        for (int done = 0; done < area.SectorCount; done += BlockSectors)
        {
            ct.ThrowIfCancellationRequested();
            int count = Math.Min(BlockSectors, area.SectorCount - done);
            var r = MmcCommands.ReadCd(_transport, (uint)(area.StartLba + done), count, MmcCommands.ReadCdUserData);
            if (!r.IsSuccess)
            {
                _log.Warning("Cache_Defeat: lettura dell'area lontana fallita a LBA {Lba}: {Detail}",
                    area.StartLba + done, ScsiErrorMessages.Describe(r.Error!));
                return false;
            }
        }

        return true;
    }
}
