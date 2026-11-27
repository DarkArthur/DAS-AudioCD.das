using DiscCheck.Core;
using Serilog;

namespace DiscCheck.Scsi.Drives;

/// <summary>
/// Reads medium type, TOC and ATIP of the disc in a drive through <see cref="IScsiTransport"/>
/// (Requirements 5.1-5.5, 5.10, 5.12, 5.13, 24.7, 24.8). Never throws for device errors.
/// </summary>
public sealed class DiscReader : IDiscReader
{
    public const string NoDiscMessage = "Nessun disco inserito nel drive.";
    public const string NotReadyMessage = "Drive non pronto: attendere che il disco sia caricato e riprovare.";
    public const string NotAudioMessage = "Il disco inserito non è un CD audio.";
    public const string UnsupportedMediumMessage =
        "Tipo di disco non supportato in questa versione (DVD o BD): sono supportati solo i CD audio.";
    public const string NotCdRReason = "supporto non CD-R";

    private readonly IScsiTransportFactory _transports;
    private readonly AtipTable _atip;
    private readonly ILogger _log;

    public DiscReader(IScsiTransportFactory transports, AtipTable atip, ILogger? log = null)
    {
        _transports = transports;
        _atip = atip;
        _log = log ?? Log.Logger;
    }

    public DiscReadResult Read(char driveLetter)
    {
        using var transport = _transports.Open(driveLetter);

        var ready = MmcCommands.TestUnitReady(transport);
        if (!ready.IsSuccess)
            return NotPresentOrError(ready.Error!, "TEST UNIT READY");

        // Medium type first: DVD/BD must not receive any TOC/ATIP or read command (Requirement 24.8).
        var kind = MediumKind.Unknown;
        var config = MmcCommands.GetConfiguration(transport, startingFeature: 0, requestType: 0, allocation: 8);
        if (config.TryGetValue(out var cfg) && cfg.Length >= 8)
        {
            kind = MediumProfiles.Classify((cfg[6] << 8) | cfg[7]);
            if (kind is MediumKind.Dvd or MediumKind.Bd)
                return new DiscReadResult(DiscStatus.UnsupportedMedium, UnsupportedMediumMessage);
        }
        else if (config.Error is { } ce)
        {
            _log.Warning("GET CONFIGURATION failed on {Drive}: {Reason}", driveLetter, ScsiErrorMessages.Describe(ce));
        }

        var tocResult = MmcCommands.ReadToc(transport, format: 0);
        if (!tocResult.TryGetValue(out var tocBytes))
            return NotPresentOrError(tocResult.Error!, "READ TOC");

        if (!TocParser.TryParse(tocBytes, out var toc, out var parseError))
        {
            _log.Warning("TOC parse failed on {Drive}: {Reason}", driveLetter, parseError);
            return new DiscReadResult(DiscStatus.ReadError, $"Lettura TOC non riuscita: {parseError}.");
        }

        if (!toc!.HasAudioTracks)
            return new DiscReadResult(DiscStatus.NotAudio, NotAudioMessage);

        string hash = TocHasher.Hash(toc);
        var (mediaId, manufacturer, reason) = ReadMedia(transport, kind, driveLetter);
        return new DiscReadResult(DiscStatus.Audio, string.Empty, toc, hash, mediaId, manufacturer, reason);
    }

    public string? Eject(char driveLetter)
    {
        using var transport = _transports.Open(driveLetter);
        var r = MmcCommands.StartStopUnit(transport, start: false, loadEject: true);
        if (r.IsSuccess)
            return null;

        var reason = ScsiErrorMessages.Describe(r.Error!);
        _log.Warning("Eject failed on {Drive}: {Reason}", driveLetter, reason);
        return $"Espulsione non riuscita: {reason}.";
    }

    private (AtipTime? MediaId, string? Manufacturer, string? Reason) ReadMedia(IScsiTransport transport, MediumKind kind, char drive)
    {
        if (kind is not (MediumKind.CdR or MediumKind.CdRw or MediumKind.Unknown))
            return (null, null, NotCdRReason);

        var atip = MmcCommands.ReadToc(transport, format: 4);
        if (!atip.TryGetValue(out var bytes))
        {
            // A pressed CD (profile unknown) typically rejects format 4: report it as "not CD-R".
            var reason = $"errore di lettura ATIP: {ScsiErrorMessages.Describe(atip.Error!)}";
            _log.Warning("ATIP read failed on {Drive}: {Reason}", drive, reason);
            return (null, null, kind == MediumKind.Unknown ? NotCdRReason + " o " + reason : reason);
        }

        if (!AtipParser.TryParse(bytes, out var time, out var parseError))
        {
            _log.Warning("ATIP parse failed on {Drive}: {Reason}", drive, parseError);
            return (null, null, $"errore di lettura ATIP: {parseError}");
        }

        return (time, _atip.Lookup(time!), null);
    }

    private DiscReadResult NotPresentOrError(MmcError error, string command)
    {
        if (error is MmcError.CommandError { Sense: { } sense })
        {
            if (ScsiErrorMessages.IsNoDisc(sense))
                return new DiscReadResult(DiscStatus.NoDisc, NoDiscMessage);
            if (sense.Key == 0x02 && command == "TEST UNIT READY")
                return new DiscReadResult(DiscStatus.NotReady, NotReadyMessage);
        }

        var reason = ScsiErrorMessages.Describe(error);
        _log.Warning("{Command} failed: {Reason}", command, reason);
        return new DiscReadResult(DiscStatus.ReadError, $"Lettura del disco non riuscita: {reason}.");
    }
}
