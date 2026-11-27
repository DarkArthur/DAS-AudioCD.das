using System.Diagnostics;
using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Verify;

/// <summary>
/// Performs the READ CD I/O for <see cref="CacheDetector"/>, logs warnings and saves the outcome
/// (Requirements 12.1-12.5, 12.10). On an aborted detection nothing is saved, so the columns stay NULL.
/// </summary>
public sealed class CacheDetectorRunner
{
    private readonly IScsiTransportFactory _transports;
    private readonly IDriveRepository _drives;
    private readonly ILogger _log;

    public CacheDetectorRunner(IScsiTransportFactory transports, IDriveRepository drives, ILogger? log = null)
    {
        _transports = transports;
        _drives = drives;
        _log = log ?? Log.Logger;
    }

    public static bool IsIhas124(string model) => model.Contains("iHAS124", StringComparison.OrdinalIgnoreCase);

    public CacheDetectionResult Run(char driveLetter, DriveRecord drive, Toc toc)
    {
        using var transport = _transports.Open(driveLetter);
        var clock = Stopwatch.StartNew();

        CacheReadResult Read(int lba, int sectors)
        {
            var r = MmcCommands.ReadCd(transport, (uint)lba, sectors, MmcCommands.ReadCdUserData);
            return r.IsSuccess ? CacheReadResult.Ok : CacheReadResult.Fail(ScsiErrorMessages.Describe(r.Error!));
        }

        var result = CacheDetector.Detect(toc, Read, () => clock.Elapsed.TotalMilliseconds, IsIhas124(drive.Model));

        if (!result.Completed)
        {
            _log.Warning("Rilevamento cache interrotto su {Drive}: {Reason}", driveLetter, result.AbortReason);
            return result;
        }

        foreach (var w in result.Warnings)
            _log.Warning("{Warning}", w);

        _drives.AddIfMissing(drive with { CachesAudio = result.CachesAudio, CacheKb = result.CacheKb });
        return result;
    }
}
