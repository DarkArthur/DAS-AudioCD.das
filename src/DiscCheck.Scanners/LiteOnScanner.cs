using System.Diagnostics;
using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Scanners;

/// <summary>
/// Lite-On scanner (C1/C2/CU) built on the vendor commands ported in <see cref="LiteOnQscanCmd"/> and
/// <see cref="MmcSeekPort"/>. Flow (Requirement 8.3, docs/liteon-cdb.md): SET CD SPEED, <c>cmd_cd_errc_init</c>,
/// read loop, <c>cmd_cd_errc_end</c> in a <c>finally</c> block (30 s timeout).
/// <list type="bullet">
/// <item>Metodo_Nuovo (default): SEEK + <c>F3 0E</c> probe, then <c>F3 0E</c> polls. CU (E32) is not measured: it is null ("n/d").</item>
/// <item>Metodo_Vecchio (<see cref="ScanOptions.Method"/>): five init commands, three commands per sample, CU = E32.</item>
/// <item>One automatic fallback Nuovo → Vecchio when the Nuovo init fails; the effective method is then fixed to Vecchio.</item>
/// </list>
/// </summary>
public sealed class LiteOnScanner : IQualityScanner
{
    public const string VendorName = "LITE-ON";

    /// <summary>Timeout of the init commands (Requirement 8.8).</summary>
    public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Timeout of the end-of-scan command (Requirement 8.6).</summary>
    public static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Consecutive Metodo_Nuovo polls without progress after which the scan fails (qpx_scan_algo.cpp).</summary>
    public const int MaxStalePolls = 64;

    /// <summary>Default pause after a poll without progress.</summary>
    public static readonly TimeSpan DefaultPollDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>Sectors added by the MSF address of LBA 0 (Red Book 2-second offset).</summary>
    private const int MsfOffsetSectors = 150;

    private const int SectorsPerSample = SampleAggregator.SectorsPerSample;

    private readonly IScsiTransportFactory _transports;
    private readonly TimeSpan _pollDelay;
    private readonly ILogger _log;

    public LiteOnScanner(IScsiTransportFactory transports, TimeSpan? pollDelay = null, ILogger? log = null)
    {
        _transports = transports;
        _pollDelay = pollDelay ?? DefaultPollDelay;
        _log = log ?? Log.Logger;
    }

    public string Name => ScannerSelector.LiteOnName;

    /// <summary>
    /// Supported when the INQUIRY vendor is "LITE-ON" (case-insensitive, Requirement 8.7). No command is sent.
    /// CU is declared available because the Metodo_Vecchio measures it; the Metodo_Nuovo reports it as null.
    /// </summary>
    public ScannerCapabilities Probe(DriveInfoEx drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (string.Equals(drive.Vendor?.Trim(), VendorName, StringComparison.OrdinalIgnoreCase))
            return new ScannerCapabilities(true, C1: true, C2: true, CU: true, null);

        return new ScannerCapabilities(false, false, false, false, "il drive non è un Lite-On (vendor INQUIRY diverso da \"LITE-ON\")");
    }

    private sealed class ScanState
    {
        public LiteOnMethod Method;
        public bool EndRequired;

        /// <summary>The init was rejected: the drive never entered Test_Mode, so a failing end command is only logged.</summary>
        public bool InitFailed;
    }

#pragma warning disable CS1998 // The SCSI pass-through is synchronous; the iterator only adapts it to IAsyncEnumerable.
    public async IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions options, IProgress<ScanProgress> progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var transport = _transports.Open(options.Drive);
        var state = new ScanState { Method = options.Method };

        try
        {
            var speed = MmcCommands.SetCdSpeed(transport, options.SpeedKBps, InitTimeout);
            if (!speed.IsSuccess)
                _log.Warning("LiteOnScanner SET CD SPEED {Speed} kB/s failed: {Reason}", options.SpeedKBps,
                             ScsiErrorMessages.Describe(speed.Error!));

            ct.ThrowIfCancellationRequested();

            // From here on the drive may be in Test_Mode: the end command must run (Requirement 8.6).
            state.EndRequired = true;
            Init(transport, state);

            var samples = state.Method == LiteOnMethod.New
                ? ReadNew(transport, options, progress, ct)
                : ReadOld(transport, options, progress, ct);
            foreach (var sample in samples)
                yield return sample;
        }
        finally
        {
            EndScan(transport, state);
        }
    }
#pragma warning restore CS1998

    // ---- init -------------------------------------------------------------------------------------------------

    private void Init(IScsiTransport transport, ScanState state)
    {
        if (state.Method == LiteOnMethod.New)
        {
            var error = InitNew(transport);
            if (error is null)
                return;

            _log.Warning("LiteOnScanner Metodo_Nuovo init failed ({Reason}); falling back to Metodo_Vecchio",
                         ScsiErrorMessages.Describe(error));
            // The effective method is fixed explicitly (docs/liteon-cdb.md, point 7).
            state.Method = LiteOnMethod.Old;
        }

        var oldError = InitOld(transport);
        if (oldError is not null)
        {
            state.InitFailed = true;
            string reason = ScsiErrorMessages.Describe(oldError);
            _log.Error("LiteOnScanner init failed: {Reason}", reason);
            throw new ScanInitFailedException(
                $"Inizializzazione della scansione Lite-On non riuscita: {reason}.",
                (oldError as MmcError.CommandError)?.Sense?.Key,
                (oldError as MmcError.CommandError)?.Sense?.Asc,
                (oldError as MmcError.CommandError)?.Sense?.Ascq);
        }
    }

    /// <summary>cmd_cd_errc_init_new: SEEK to LBA 0, then the F3 0E probe.</summary>
    private static MmcError? InitNew(IScsiTransport transport)
    {
        var seek = MmcSeekPort.Seek(transport, 0, 0, InitTimeout);
        if (!seek.IsSuccess) return seek.Error;
        var probe = LiteOnQscanCmd.CdErrcProbeNew(transport, InitTimeout);
        return probe.IsSuccess ? null : probe.Error;
    }

    /// <summary>cmd_cd_errc_init_old: steps A-E in order; the first failure stops the sequence.</summary>
    private static MmcError? InitOld(IScsiTransport transport)
    {
        Func<IScsiTransport, TimeSpan?, MmcResult<bool>>[] steps =
        [
            LiteOnQscanCmd.CdErrcInitOldA, LiteOnQscanCmd.CdErrcInitOldB, LiteOnQscanCmd.CdErrcInitOldC,
            LiteOnQscanCmd.CdErrcInitOldD, LiteOnQscanCmd.CdErrcInitOldE,
        ];
        foreach (var step in steps)
        {
            var r = step(transport, InitTimeout);
            if (!r.IsSuccess) return r.Error;
        }
        return null;
    }

    // ---- read loops -------------------------------------------------------------------------------------------

    /// <summary>Metodo_Vecchio: read, get data, reset interval per sample; one sample = 75 sectors.</summary>
    private IEnumerable<ScanSample> ReadOld(IScsiTransport transport, ScanOptions options,
                                            IProgress<ScanProgress> progress, CancellationToken ct)
    {
        int leadOut = options.Toc.LeadOutLba;
        var clock = new Stopwatch();
        int lba = 0, index = 0;

        while (lba < leadOut)
        {
            ct.ThrowIfCancellationRequested();
            clock.Restart();
            Require(LiteOnQscanCmd.CdErrcRead(transport), lba);
            var data = Require(LiteOnQscanCmd.CdErrcGetData(transport), lba);
            Require(LiteOnQscanCmd.CdErrcIntervalReset(transport), lba);
            double? speed = SampleAggregator.ComputeSpeedX(SectorsPerSample, clock.Elapsed.TotalSeconds);

            yield return new ScanSample(lba, index, data.Bler, data.E22, data.E32, speed);

            lba += SectorsPerSample;
            index++;
            int reached = Math.Min(lba, leadOut);
            progress.Report(new ScanProgress(reached, 100.0 * reached / leadOut, speed));
        }
    }

    /// <summary>
    /// Metodo_Nuovo: polls without progress are discarded (64 in a row fail the scan); counters of the polls that
    /// advance are summed until 75 sectors are reached, then one sample per 75 sectors is published. CU is null.
    /// </summary>
    private IEnumerable<ScanSample> ReadNew(IScsiTransport transport, ScanOptions options,
                                            IProgress<ScanProgress> progress, CancellationToken ct)
    {
        int leadOut = options.Toc.LeadOutLba;
        var clock = Stopwatch.StartNew();
        int lastLba = 0, sampleLba = 0, index = 0;
        int pendingSectors = 0, windowSectors = 0, stale = 0;
        long c1 = 0, c2 = 0;

        while (lastLba < leadOut)
        {
            ct.ThrowIfCancellationRequested();
            var block = Require(LiteOnQscanCmd.CdErrcBlockNew(transport), lastLba);
            int lba = Math.Min(Math.Max(0, block.Lba - MsfOffsetSectors), leadOut);

            if (lba <= lastLba)
            {
                if (++stale >= MaxStalePolls)
                {
                    _log.Error("LiteOnScanner Metodo_Nuovo: {Count} polls without progress at LBA {Lba}", stale, lastLba);
                    throw new ScanFailedException(
                        $"Il drive non avanza: {stale} letture consecutive senza progresso all'LBA {lastLba}. La scansione è stata interrotta.",
                        lastLba);
                }
                if (_pollDelay > TimeSpan.Zero)
                    ct.WaitHandle.WaitOne(_pollDelay);
                continue;
            }

            stale = 0;
            int advance = lba - lastLba;
            lastLba = lba;
            pendingSectors += advance;
            windowSectors += advance;
            c1 += block.Bler;
            c2 += block.E22;

            int whole = pendingSectors / SectorsPerSample;
            if (whole > 0)
            {
                double? speed = SampleAggregator.ComputeSpeedX(windowSectors, clock.Elapsed.TotalSeconds);
                for (int i = 0; i < whole; i++)
                {
                    // The counters of the polls are shared out evenly: totals are preserved.
                    yield return new ScanSample(sampleLba, index, Share(c1, whole, i), Share(c2, whole, i), null, speed);
                    sampleLba += SectorsPerSample;
                    index++;
                }
                pendingSectors %= SectorsPerSample;
                windowSectors = 0;
                c1 = c2 = 0;
                clock.Restart();
                progress.Report(new ScanProgress(lastLba, 100.0 * lastLba / leadOut, speed));
            }
            else
            {
                progress.Report(new ScanProgress(lastLba, 100.0 * lastLba / leadOut, null));
            }
        }

        // Residual group of 1-74 sectors at the lead-out (as in StandardC2Scanner, Requirement 7.7).
        if (pendingSectors > 0)
        {
            yield return new ScanSample(sampleLba, index, ToInt(c1), ToInt(c2), null,
                                        SampleAggregator.ComputeSpeedX(windowSectors, clock.Elapsed.TotalSeconds));
        }
    }

    private static int Share(long total, int parts, int index) =>
        ToInt(total / parts + (index < total % parts ? 1 : 0));

    private static int ToInt(long value) => (int)Math.Min(value, int.MaxValue);

    // ---- end of scan ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>cmd_cd_errc_end</c>: nothing for the Metodo_Nuovo, <c>DF A3 01</c> for the Metodo_Vecchio, 30 s timeout.
    /// A failure is reported as <see cref="TestModeEndFailedException"/> (Requirement 9.12).
    /// </summary>
    private void EndScan(IScsiTransport transport, ScanState state)
    {
        if (!state.EndRequired || state.Method == LiteOnMethod.New)
            return;

        var end = LiteOnQscanCmd.CdErrcEndOld(transport, EndTimeout);
        if (end.IsSuccess)
            return;

        string reason = ScsiErrorMessages.Describe(end.Error!);
        string cdb = Convert.ToHexString([0xDF, 0xA3, 0x01, 0, 0, 0, 0, 0, 0, 0]);
        if (state.InitFailed)
        {
            // Best effort after a rejected init (as the original end_test): the init error is the one to report.
            _log.Warning("LiteOnScanner end-of-scan command after a failed init also failed: CDB {Cdb}: {Reason}", cdb, reason);
            return;
        }

        _log.Error("LiteOnScanner end-of-scan command failed: CDB {Cdb}: {Reason}", cdb, reason);
        throw new TestModeEndFailedException(
            $"Il comando di fine scansione non è riuscito ({reason}): il drive potrebbe essere rimasto in Test_Mode.",
            cdb, reason);
    }

    // ---- errors -----------------------------------------------------------------------------------------------

    private T Require<T>(MmcResult<T> result, int lba)
    {
        if (result.TryGetValue(out var value))
            return value;
        throw Failure(lba, result.Error!);
    }

    private ScanFailedException Failure(int lba, MmcError error)
    {
        string reason = ScsiErrorMessages.Describe(error);
        _log.Error("LiteOnScanner command failed at LBA {Lba}: {Reason}", lba, reason);

        var sense = (error as MmcError.CommandError)?.Sense;
        string message = $"Lettura non riuscita all'LBA {lba}: {reason}. La scansione è stata interrotta.";
        return sense is { } s
            ? new ScanFailedException(message, lba, s.Key, s.Asc, s.Ascq)
            : new ScanFailedException(message, lba);
    }
}
