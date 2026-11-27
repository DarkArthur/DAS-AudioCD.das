using System.Diagnostics;
using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Scanners;

/// <summary>
/// Universal scanner: reads the disc sequentially with READ CD (CD-DA, user data + C2 pointers) and counts
/// the C2 bits. C1 is not measured (null) and CU is 0 (Requirements 7.1-7.8, 7.10).
/// </summary>
public sealed class StandardC2Scanner : IQualityScanner
{
    /// <summary>READ CD flags (CDB byte 9): user data + 294 C2 pointer bytes (2646 B/sector).</summary>
    public const byte FlagsC2Only = MmcCommands.ReadCdUserData | MmcCommands.ReadCdC2Pointers;

    /// <summary>READ CD flags (CDB byte 9): user data + 296 bytes with block error byte (2648 B/sector).</summary>
    public const byte FlagsC2WithBlockError = MmcCommands.ReadCdUserData | MmcCommands.ReadCdC2AndBlockError;

    public const int DefaultBlockSectors = 26;

    private readonly IScsiTransportFactory _transports;
    private readonly byte _flags;
    private readonly int _blockSectors;
    private readonly ILogger _log;

    /// <param name="flags">0x12 or 0x14, constant for the whole scan (Requirement 7.4).</param>
    /// <param name="blockSectors">26 or 27, constant for the whole scan (Requirement 7.3).</param>
    public StandardC2Scanner(IScsiTransportFactory transports, byte flags = FlagsC2Only,
                             int blockSectors = DefaultBlockSectors, ILogger? log = null)
    {
        if (flags is not (FlagsC2Only or FlagsC2WithBlockError))
            throw new ArgumentOutOfRangeException(nameof(flags), flags, "READ CD flags must be 0x12 or 0x14.");
        if (blockSectors is not (26 or 27))
            throw new ArgumentOutOfRangeException(nameof(blockSectors), blockSectors, "Block size must be 26 or 27 sectors.");

        _transports = transports;
        _flags = flags;
        _blockSectors = blockSectors;
        _log = log ?? Log.Logger;
    }

    public string Name => "StandardC2";

    public ScannerCapabilities Probe(DriveInfoEx drive)
    {
        try
        {
            using var transport = _transports.Open(drive.Letter);
            var page = MmcCommands.ModeSense10Page2A(transport);
            if (!page.TryGetValue(out var data))
            {
                var reason = $"MODE SENSE 0x2A non riuscito: {ScsiErrorMessages.Describe(page.Error!)}";
                _log.Warning("StandardC2Scanner probe on {Drive}: {Reason}", drive.Letter, reason);
                return new ScannerCapabilities(false, false, false, false, reason);
            }

            if (!data.C2PointersSupported)
                return new ScannerCapabilities(false, false, false, false, "il drive non dichiara il supporto dei puntatori C2");

            return new ScannerCapabilities(true, C1: false, C2: true, CU: false, null);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "StandardC2Scanner probe on {Drive} failed", drive.Letter);
            return new ScannerCapabilities(false, false, false, false, $"probe non riuscito: {ex.Message}");
        }
    }

#pragma warning disable CS1998 // The SCSI pass-through is synchronous; the iterator only adapts it to IAsyncEnumerable.
    public async IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions options, IProgress<ScanProgress> progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        int leadOut = options.Toc.LeadOutLba;
        int sectorSize = MmcCommands.ReadCdSectorSize(_flags, 0);
        var aggregator = new SampleAggregator();
        var clock = new Stopwatch();

        using var transport = _transports.Open(options.Drive);
        int lba = 0;
        while (lba < leadOut)
        {
            ct.ThrowIfCancellationRequested();

            int count = Math.Min(_blockSectors, leadOut - lba);
            clock.Restart();
            var read = MmcCommands.ReadCd(transport, (uint)lba, count, _flags);
            double elapsed = clock.Elapsed.TotalSeconds;

            if (!read.TryGetValue(out var data))
                throw Failure(lba, read.Error!);

            var c2 = new int[count];
            for (int i = 0; i < count; i++)
                c2[i] = C2BitCounter.CountC2FromC2Area(data.AsSpan(i * sectorSize + MmcCommands.CdDaSectorSize,
                                                                     sectorSize - MmcCommands.CdDaSectorSize));

            double? speed = SampleAggregator.ComputeSpeedX(count, elapsed);
            foreach (var sample in aggregator.AddBlock(lba, c2, elapsed))
                yield return sample;

            lba += count;
            progress.Report(new ScanProgress(lba, 100.0 * lba / leadOut, speed));
        }

        // Residual group of 1-74 sectors at the lead-out (Requirement 7.7).
        if (aggregator.Flush() is { } last)
            yield return last;
    }
#pragma warning restore CS1998

    private ScanFailedException Failure(int lba, MmcError error)
    {
        string reason = ScsiErrorMessages.Describe(error);
        _log.Error("StandardC2Scanner READ CD failed at LBA {Lba}: {Reason}", lba, reason);

        var sense = (error as MmcError.CommandError)?.Sense;
        string message = $"Lettura non riuscita all'LBA {lba}: {reason}. La scansione è stata interrotta.";
        return sense is { } s
            ? new ScanFailedException(message, lba, s.Key, s.Asc, s.Ascq)
            : new ScanFailedException(message, lba);
    }
}
