using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using Serilog;

namespace DiscCheck.Verify;

/// <summary>Input of a bit-perfect verification.</summary>
/// <param name="Offset">Offset in use in samples (Combined_Offset or manual Read_Offset), within ±5000 (Requirements 14.3, 15.5).</param>
/// <param name="MaxRereads">N, 1-10 (Requirement 14.6).</param>
public sealed record VerifyOptions(char DriveLetter, DriveRecord Drive, Toc Toc, string SourcePath, int Offset, int MaxRereads = 3);

public enum VerifyStatus
{
    /// <summary>All tracks were compared; see <see cref="VerifyReport.Result"/>.</summary>
    Completed,
    /// <summary>A pre-check failed before any extraction; no track result exists (Requirements 14.3, 14.4, 14.16).</summary>
    PreCheckFailed,
    /// <summary>READ CD or source decoding failed after extraction started; results of previous tracks are kept (Requirement 14.17).</summary>
    Error,
    /// <summary>The user cancelled. Never saved as IDENTICAL (Requirement 14.12).</summary>
    Cancelled,
}

public enum PreCheckFailure { None, TrackCountMismatch, SourceFile, DurationMismatch }

/// <summary>Progress of a track: percentage 0-100; <see cref="Completed"/> is set with the final 100 % update.</summary>
public sealed record VerifyProgress(int TrackNo, int TrackIndex, int TrackCount, double Percent, TrackResult? Completed);

/// <summary>Outcome of <see cref="Verifier.Run"/>.</summary>
/// <param name="Result">Value to save in <c>verifications.result</c>: Identical/Different when completed, Error on a read error,
/// null when the verification was cancelled or rejected by a pre-check (nothing to save).</param>
/// <param name="CacheDefeatLimited">Some re-read ran without Cache_Defeat for lack of a far area (to show in the report, Req. 12.9).</param>
public sealed record VerifyReport(VerifyStatus Status, VerificationResult? Result, int OffsetUsed,
                                  IReadOnlyList<TrackResult> Tracks, string? Message,
                                  PreCheckFailure PreCheck = PreCheckFailure.None, bool CacheDefeatLimited = false)
{
    /// <summary>True only for a completed run where every track is identical ("Disco identico alla sorgente").</summary>
    public bool IsDiscIdentical => Status == VerifyStatus.Completed && Result == VerificationResult.Identical;

    /// <summary>Rows for <c>verification_tracks</c>: hashes are the read-side CRC32 (8 hex digits) and SHA-256 (64 hex digits).</summary>
    public IReadOnlyList<VerificationTrackRecord> ToTrackRecords() =>
        Tracks.Select(t => new VerificationTrackRecord(t.TrackNo, t.Identical, t.DifferentSamples, t.FirstDiffIndex,
            t.CrcRead.ToString("X8"), t.Sha256Read, t.ExcludedEdgeSamples,
            t.UnreliableLbas.Count == 0 ? null : string.Join(',', t.UnreliableLbas))).ToList();
}

/// <summary>
/// Bit-perfect verification: pre-checks, extraction, comparison with <see cref="PcmComparer"/>, CRC32 and SHA-256
/// (Requirements 13.5, 14.2-14.13, 14.16-14.18, 15.10, 15.11). The Sorgente is the only reference.
/// </summary>
public sealed class Verifier
{
    private readonly IScsiTransportFactory _transports;
    private readonly SourceReader _source;
    private readonly ILogger _log;

    public Verifier(IScsiTransportFactory transports, SourceReader source, ILogger? log = null)
    {
        _transports = transports;
        _source = source;
        _log = log ?? Log.Logger;
    }

    /// <summary>C2 pointers are used for re-read decisions only on a C2 drive whose pointers are not known to be unreliable (Req. 13.5).</summary>
    public static bool ShouldUseC2(DriveRecord drive) => drive.C2Supported == true && drive.C2Reliable != false;

    /// <summary>Runs the verification synchronously; call it from a background thread. Never throws for device or source errors.</summary>
    public VerifyReport Run(VerifyOptions options, IProgress<VerifyProgress>? progress, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(options.Offset), OffsetDetector.MaxOffset, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRereads, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRereads, 10);

        var results = new List<TrackResult>();
        bool limited = false;
        try
        {
            ct.ThrowIfCancellationRequested();

            var tracks = options.Toc.Tracks
                .Select((t, i) => (Entry: t, Sectors: options.Toc.TrackSectors(i)))
                .Where(t => t.Entry.IsAudio)
                .ToList();

            if (PreCheck(options, tracks.Select(t => t.Sectors).ToList(), out var files) is { } failed)
                return failed;

            ct.ThrowIfCancellationRequested();

            var extractOptions = new ExtractOptions(ShouldUseC2(options.Drive), options.MaxRereads,
                options.Drive.CacheKb, options.Toc.LeadOutLba);
            if (!extractOptions.UseC2)
                _log.Information("Verifica senza puntatori C2 (supporto C2: {Supported}, affidabilità: {Reliable})",
                    options.Drive.C2Supported, options.Drive.C2Reliable);

            using var transport = _transports.Open(options.DriveLetter);
            var extractor = new AudioExtractor(transport, _log);

            for (int i = 0; i < tracks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (entry, sectors) = tracks[i];
                int trackNo = entry.TrackNo;
                progress?.Report(new VerifyProgress(trackNo, i, tracks.Count, 0, null));

                var reference = _source.ReadAll(files[i]);
                var extracted = extractor.ExtractTrack(trackNo, entry.StartLba, sectors, extractOptions,
                    f => progress?.Report(new VerifyProgress(trackNo, i, tracks.Count, Math.Min(99, f * 100), null)), ct);
                limited |= extracted.CacheDefeatLimited;

                var result = Compare(trackNo, reference, extracted, options.Offset);
                results.Add(result);
                progress?.Report(new VerifyProgress(trackNo, i, tracks.Count, 100, result));
            }

            ct.ThrowIfCancellationRequested();
            var overall = results.All(r => r.Identical) ? VerificationResult.Identical : VerificationResult.Different;
            return new VerifyReport(VerifyStatus.Completed, overall, options.Offset, results, null, CacheDefeatLimited: limited);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Verifica annullata dopo {Count} tracce", results.Count);
            return new VerifyReport(VerifyStatus.Cancelled, null, options.Offset, results, "Verifica annullata.", CacheDefeatLimited: limited);
        }
        catch (ReadCdException e)
        {
            return new VerifyReport(VerifyStatus.Error, VerificationResult.Error, options.Offset, results, e.Message, CacheDefeatLimited: limited);
        }
        catch (SourceFileException e)
        {
            _log.Error(e, "Sorgente non utilizzabile durante la verifica");
            return new VerifyReport(VerifyStatus.Error, VerificationResult.Error, options.Offset, results, e.Message, CacheDefeatLimited: limited);
        }
    }

    /// <summary>Track count, file format and duration checks, in this order. Returns a report on failure, null when all pass.</summary>
    private VerifyReport? PreCheck(VerifyOptions options, IReadOnlyList<int> trackSectors, out IReadOnlyList<string> files)
    {
        files = [];
        VerifyReport Fail(PreCheckFailure kind, string message)
        {
            _log.Warning("Pre-controllo della verifica fallito: {Message}", message);
            return new VerifyReport(VerifyStatus.PreCheckFailed, null, options.Offset, [], message, kind);
        }

        try
        {
            files = _source.ResolveFiles(options.SourcePath);
        }
        catch (SourceFileException e)
        {
            return Fail(PreCheckFailure.SourceFile, e.Message);
        }

        if (files.Count != trackSectors.Count)
        {
            return Fail(PreCheckFailure.TrackCountMismatch,
                $"Il numero di tracce del Disco ({trackSectors.Count}) è diverso dal numero di file della Sorgente ({files.Count}).");
        }

        var frames = new long[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            try
            {
                using var stream = _source.Open(files[i]);
                frames[i] = stream.TotalFrames;
            }
            catch (SourceFileException e)
            {
                return Fail(PreCheckFailure.SourceFile, e.Message);
            }
        }

        for (int i = 0; i < files.Count; i++)
        {
            long expected = (long)trackSectors[i] * AudioExtractor.FramesPerSector;
            if (Math.Abs(frames[i] - expected) > Math.Abs((long)options.Offset))
            {
                string fileSectors = (frames[i] / (double)AudioExtractor.FramesPerSector)
                    .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                return Fail(PreCheckFailure.DurationMismatch,
                    $"Traccia {i + 1}: durata del file {fileSectors} settori, durata della traccia sul Disco {trackSectors[i]} settori.");
            }
        }

        return null;
    }

    /// <summary>
    /// Compares read and source PCM with offset k (<c>drive[i] == source[i + k]</c>), excluding exactly |k| edge frames, and hashes the
    /// compared regions of both sides (Requirements 14.7-14.9, 15.8, 15.9).
    /// </summary>
    private static TrackResult Compare(int trackNo, short[] reference, ExtractedTrack extracted, int offset)
    {
        var drive = extracted.Pcm;
        var cmp = PcmComparer.Compare(reference, drive, offset);

        long from = offset < 0 ? cmp.ExcludedFrames : 0;
        var driveRegion = drive.AsSpan((int)(from * 2), (int)(cmp.ComparedFrames * 2));
        var sourceRegion = reference.AsSpan((int)((from + offset) * 2), (int)(cmp.ComparedFrames * 2));
        var driveBytes = MemoryMarshal.AsBytes(driveRegion);
        var sourceBytes = MemoryMarshal.AsBytes(sourceRegion);

        return new TrackResult(trackNo, cmp.IsIdentical, cmp.DifferentFrames, cmp.FirstDifferentFrame,
            Crc32.HashToUInt32(driveBytes), Crc32.HashToUInt32(sourceBytes),
            Convert.ToHexStringLower(SHA256.HashData(driveBytes)), Convert.ToHexStringLower(SHA256.HashData(sourceBytes)),
            (int)cmp.ExcludedFrames, extracted.UnreliableLbas);
    }
}
