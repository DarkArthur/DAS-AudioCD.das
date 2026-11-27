using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Verify;

public enum C2TestStatus { Completed, Unsupported, Error, Cancelled }

/// <param name="Outcome">Set only when <see cref="Status"/> is <see cref="C2TestStatus.Completed"/>.</param>
/// <param name="Saved">True when <c>drives.c2_reliable</c> was written.</param>
public sealed record C2TestResult(C2TestStatus Status, string Message, C2ReliabilityOutcome? Outcome = null,
                                  bool Saved = false, bool CacheDefeatLimited = false);

/// <summary>
/// Runs the C2_Reliability_Test: two READ CD readings with C2 of the same 7,500 sectors from the first audio sector,
/// each preceded by Cache_Defeat; the decision is delegated to <see cref="C2ReliabilityAnalyzer"/> (Requirements 13.2-13.8).
/// On error, cancellation or an inconclusive result <c>drives.c2_reliable</c> is left unchanged.
/// </summary>
public sealed class C2ReliabilityTestRunner
{
    public const string UnsupportedMessage = "Il Drive non supporta i puntatori C2: il test di affidabilità C2 non può essere eseguito.";
    public const string InconclusiveMessage = "Esito non conclusivo: le due letture sono identiche. Usare un disco con un difetto noto.";

    private const int BlockSectors = 26;
    private const byte Flags = MmcCommands.ReadCdUserData | MmcCommands.ReadCdC2Pointers;

    private readonly IScsiTransportFactory _transports;
    private readonly IDriveRepository _drives;
    private readonly ILogger _log;

    public C2ReliabilityTestRunner(IScsiTransportFactory transports, IDriveRepository drives, ILogger? log = null)
    {
        _transports = transports;
        _drives = drives;
        _log = log ?? Log.Logger;
    }

    /// <summary>Synchronous; call from a background thread. Never throws for device errors or cancellation.</summary>
    /// <param name="progress">Fraction 0-1 of the two readings.</param>
    public C2TestResult Run(char driveLetter, DriveRecord drive, Toc toc, Action<double>? progress, CancellationToken ct)
    {
        if (drive.C2Supported != true)
            return new C2TestResult(C2TestStatus.Unsupported, UnsupportedMessage);

        var first = toc.Tracks.FirstOrDefault(t => t.IsAudio);
        int count = C2ReliabilityAnalyzer.SectorsToRead;
        if (first is null || (long)first.StartLba + count > toc.LeadOutLba)
            return new C2TestResult(C2TestStatus.Error, $"Il disco non contiene {count} settori audio da leggere.");

        try
        {
            using var transport = _transports.Open(driveLetter);
            bool limited = false;
            var readings = new byte[2][];
            for (int pass = 0; pass < 2; pass++)
            {
                limited |= !DefeatCache(transport, first.StartLba, toc.LeadOutLba, drive.CacheKb, ct);
                readings[pass] = ReadArea(transport, first.StartLba, count, pass, progress, ct);
            }

            var outcome = C2ReliabilityAnalyzer.Analyze(readings[0], readings[1], count);
            if (outcome.ReliableValue is not { } reliable)
                return new C2TestResult(C2TestStatus.Completed, InconclusiveMessage, outcome, false, limited);

            _drives.AddIfMissing(drive with { C2Reliable = reliable });
            string message = reliable
                ? $"C2 affidabili: tutti i {outcome.DifferingSectors} settori con dati diversi sono segnalati dai puntatori C2."
                : $"C2 non affidabili: {outcome.UncoveredSectors} settori con dati diversi non sono coperti dai C2.";
            _log.Information("C2_Reliability_Test su {Drive}: {Message}", driveLetter, message);
            return new C2TestResult(C2TestStatus.Completed, message, outcome, true, limited);
        }
        catch (OperationCanceledException)
        {
            _log.Information("C2_Reliability_Test annullato su {Drive}", driveLetter);
            return new C2TestResult(C2TestStatus.Cancelled, "Test annullato: l'affidabilità C2 del Drive non è stata modificata.");
        }
        catch (ReadCdException ex)
        {
            _log.Error("C2_Reliability_Test: {Message}", ex.Message);
            return new C2TestResult(C2TestStatus.Error, $"{ex.Message}. L'affidabilità C2 del Drive non è stata modificata.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "C2_Reliability_Test fallito su {Drive}", driveLetter);
            return new C2TestResult(C2TestStatus.Error, $"Test non riuscito: {ex.Message}. L'affidabilità C2 del Drive non è stata modificata.");
        }
    }

    private static byte[] ReadArea(IScsiTransport transport, int startLba, int count, int pass,
                                   Action<double>? progress, CancellationToken ct)
    {
        int stride = C2ReliabilityAnalyzer.SectorStride;
        var buffer = new byte[(long)count * stride];
        for (int done = 0; done < count; done += BlockSectors)
        {
            ct.ThrowIfCancellationRequested();
            int n = Math.Min(BlockSectors, count - done);
            var r = MmcCommands.ReadCd(transport, (uint)(startLba + done), n, Flags);
            if (!r.IsSuccess)
                throw new ReadCdException(0, startLba + done, ScsiErrorMessages.Describe(r.Error!), r.Error!);
            Buffer.BlockCopy(r.Value, 0, buffer, done * stride, n * stride);
            progress?.Invoke((pass * (double)count + done + n) / (2.0 * count));
        }

        return buffer;
    }

    /// <summary>Reads the far area so the next read cannot come from the cache. False when no area is available or readable.</summary>
    private bool DefeatCache(IScsiTransport transport, int lba, int totalSectors, int? cacheKb, CancellationToken ct)
    {
        var plan = CacheDefeatPlanner.Plan(lba, totalSectors, cacheKb);
        if (plan.Area is not { } area)
        {
            _log.Warning("Cache_Defeat non applicabile al C2_Reliability_Test: mancano {Missing} settori", plan.MissingSectors);
            return false;
        }

        for (int done = 0; done < area.SectorCount; done += BlockSectors)
        {
            ct.ThrowIfCancellationRequested();
            int n = Math.Min(BlockSectors, area.SectorCount - done);
            var r = MmcCommands.ReadCd(transport, (uint)(area.StartLba + done), n, MmcCommands.ReadCdUserData);
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
