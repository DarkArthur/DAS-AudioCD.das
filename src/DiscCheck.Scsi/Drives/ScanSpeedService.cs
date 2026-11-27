using DiscCheck.Core;
using Serilog;

namespace DiscCheck.Scsi.Drives;

/// <summary>SET CD SPEED before the first read of a scan (Requirements 10.2, 10.7).</summary>
public sealed class ScanSpeedService : IScanSpeedService
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IScsiTransportFactory _transports;
    private readonly ILogger _log;

    public ScanSpeedService(IScsiTransportFactory transports, ILogger? log = null)
    {
        _transports = transports;
        _log = log ?? Log.Logger;
    }

    public string? SetSpeed(char driveLetter, int speedKBps)
    {
        try
        {
            using var transport = _transports.Open(driveLetter);
            var result = MmcCommands.SetCdSpeed(transport, speedKBps, Timeout);
            if (result.TryGetValue(out _))
                return null;

            string reason = ScsiErrorMessages.Describe(result.Error!);
            _log.Error("SET CD SPEED {Speed} kB/s on {Drive} failed: {Reason}", speedKBps, driveLetter, reason);
            return $"Il drive ha rifiutato la velocità richiesta ({reason}). La scansione non è stata avviata.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "SET CD SPEED {Speed} kB/s on {Drive} failed", speedKBps, driveLetter);
            return $"Impostazione della velocità non riuscita: {ex.Message}. La scansione non è stata avviata.";
        }
    }
}
