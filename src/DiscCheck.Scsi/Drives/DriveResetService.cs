using System.Diagnostics;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.Scsi.Drives;

/// <summary>
/// The vendor "end of scan" command that closes Test_Mode. The Lite-On CDB (<c>cmd_cd_errc_end</c>) must be
/// ported from QPxTool_Source in M6 (task 13.3) and registered here; it is never guessed.
/// </summary>
public interface IScanEndCommand
{
    /// <summary>Name for logs and messages.</summary>
    string Name { get; }

    /// <summary>The CDB to send (no data phase).</summary>
    ReadOnlyMemory<byte> Cdb { get; }
}

/// <summary>
/// "Reset drive": end-of-scan command, then TEST UNIT READY. Outcome within <see cref="Deadline"/>
/// (Requirements 4.9, 4.10, 26.5, 26.6).
/// </summary>
public sealed class DriveResetService : IDriveResetService
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinStep = TimeSpan.FromSeconds(1);

    public const string RestoredMessage = "Drive ripristinato";
    public const string RecoveryAdvice = "Espellere e reinserire il disco oppure ricollegare il drive, quindi riprovare.";

    private readonly IScsiTransportFactory _transports;
    private readonly IScanEndCommand? _endScan;
    private readonly ILogger _log;

    public DriveResetService(IScsiTransportFactory transports, IScanEndCommand? endScan = null, ILogger? log = null)
    {
        _transports = transports;
        _endScan = endScan;
        _log = log ?? Log.Logger;
    }

    public DriveResetResult Reset(char driveLetter)
    {
        var clock = Stopwatch.StartNew();
        using var transport = _transports.Open(driveLetter);

        if (_endScan is null)
            _log.Information("Reset drive {Drive}: no vendor end-of-scan command registered, sending TEST UNIT READY only", driveLetter);
        else
        {
            var cdb = _endScan.Cdb.Span;
            var r = transport.Execute(cdb, Span<byte>.Empty, DataDirection.None, Remaining(clock));
            if (!r.IsGood)
                return Fail(driveLetter, _endScan.Name, ScsiErrorMessages.Hex(cdb), r);
        }

        var tur = new byte[6];
        var t = transport.Execute(tur, Span<byte>.Empty, DataDirection.None, Remaining(clock));
        if (t.IsGood || (t.Error is null && ScsiErrorMessages.IsNoDisc(t.Sense)))
        {
            _log.Information("Reset drive {Drive}: restored", driveLetter);
            return new DriveResetResult(true, RestoredMessage);
        }
        return Fail(driveLetter, "TEST UNIT READY", ScsiErrorMessages.Hex(tur), t);
    }

    private static TimeSpan Remaining(Stopwatch clock)
    {
        var left = Deadline - clock.Elapsed;
        return left < MinStep ? MinStep : left;
    }

    private DriveResetResult Fail(char drive, string command, string cdbHex, ScsiResult r)
    {
        string cause = r.Error is { } e
            ? ScsiErrorMessages.Describe(e)
            : $"status 0x{r.ScsiStatus:X2}" + (r.Sense is { } s ? $", {ScsiErrorMessages.DescribeSense(s)}" : string.Empty);

        _log.Error("Reset drive {Drive} failed: {Command} CDB {Cdb}: {Cause}", drive, command, cdbHex, cause);

        var message = r.Error is TransportError.OpenFailed { ErrorCode: ScsiErrorMessages.ErrorAccessDenied }
            ? ScsiErrorMessages.AdministratorRequired
            : $"Reset drive non riuscito ({command}: {cause}). {RecoveryAdvice}";
        return new DriveResetResult(false, message);
    }
}
