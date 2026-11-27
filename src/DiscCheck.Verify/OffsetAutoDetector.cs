using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Verify;

/// <summary>
/// Automatic Read_Offset detection on the first track of the disc (Requirements 15.4-15.7): extracts the track,
/// decodes the first Sorgente file and hands both to <see cref="OffsetService.DetectAndSave"/>.
/// </summary>
public sealed class OffsetAutoDetector
{
    private readonly IScsiTransportFactory _transports;
    private readonly SourceReader _source;
    private readonly OffsetService _offsets;
    private readonly ILogger _log;

    public OffsetAutoDetector(IScsiTransportFactory transports, SourceReader source, OffsetService offsets, ILogger? log = null)
    {
        _transports = transports;
        _source = source;
        _offsets = offsets;
        _log = log ?? Log.Logger;
    }

    /// <summary>On success the offset is saved in <c>drives.read_offset</c>; otherwise the drive is left untouched.</summary>
    /// <exception cref="SourceFileException">The first Sorgente file cannot be used.</exception>
    /// <exception cref="ReadCdException">READ CD failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public OffsetDetectionResult Run(char driveLetter, DriveRecord drive, Toc toc, string sourcePath,
                                     Action<double>? progress, CancellationToken ct)
    {
        int index = -1;
        for (int i = 0; i < toc.Tracks.Count; i++)
        {
            if (toc.Tracks[i].IsAudio) { index = i; break; }
        }
        if (index < 0)
            return new OffsetDetectionResult(OffsetDetectionStatus.TrackTooShort, null, "Il disco non contiene tracce audio.");

        var files = _source.ResolveFiles(sourcePath);
        if (files.Count == 0)
            throw new SourceFileException(sourcePath, SourceFileFailure.Unreadable, "nessun file audio nella Sorgente");

        var reference = _source.ReadAll(files[0]);
        ct.ThrowIfCancellationRequested();

        using var transport = _transports.Open(driveLetter);
        var extractor = new AudioExtractor(transport, _log);
        var entry = toc.Tracks[index];
        var options = new ExtractOptions(false, 0, drive.CacheKb, toc.LeadOutLba); // plain reads: C2 is not needed to align
        var track = extractor.ExtractTrack(entry.TrackNo, entry.StartLba, toc.TrackSectors(index), options, progress, ct);

        var result = _offsets.DetectAndSave(drive, reference, track.Pcm);
        _log.Information("Rilevamento offset su {Drive}: {Status} {Offset}", driveLetter, result.Status, result.Offset);
        return result;
    }
}
