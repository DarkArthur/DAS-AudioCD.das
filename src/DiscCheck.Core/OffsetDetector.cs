namespace DiscCheck.Core;

/// <summary>Outcome of <see cref="OffsetDetector.Detect"/>.</summary>
public enum OffsetDetectionStatus { Found, NotFound, TrackTooShort }

/// <summary>Detection outcome. <see cref="Offset"/> is set only when <see cref="Status"/> is <see cref="OffsetDetectionStatus.Found"/>.</summary>
public sealed record OffsetDetectionResult(OffsetDetectionStatus Status, int? Offset, string? Message)
{
    public bool IsFound => Status == OffsetDetectionStatus.Found;
}

/// <summary>
/// Exact read-offset detection (Requirements 15.4, 15.6, 15.7, 15.12). Pure function over interleaved
/// 16-bit stereo PCM (L,R,L,R...).
/// <para>
/// Convention: an offset <c>k</c> means <c>drive[i] == reference[i + k]</c> for every frame <c>i</c> where both
/// indexes are valid. Positive k: the drive output is ahead of the reference.
/// </para>
/// </summary>
public static class OffsetDetector
{
    public const int MaxOffset = 5000;
    public const int BlockFrames = 44_100;
    public const int MinTrackFrames = BlockFrames + 2 * MaxOffset + 0; // 54.100

    /// <summary>
    /// Takes the central <see cref="BlockFrames"/> frames of the drive track and looks for the k in
    /// [-5000, +5000] with 0 differing frames against the reference. Returns the match with the smallest |k|.
    /// </summary>
    public static OffsetDetectionResult Detect(ReadOnlySpan<short> reference, ReadOnlySpan<short> drive)
    {
        int frames = Math.Min(reference.Length, drive.Length) / 2;
        if (frames < MinTrackFrames)
        {
            return new OffsetDetectionResult(OffsetDetectionStatus.TrackTooShort, null,
                "Traccia troppo breve per il rilevamento automatico dell'offset.");
        }

        int start = (frames - BlockFrames) / 2; // >= MaxOffset, and frames - start - BlockFrames >= MaxOffset
        var block = drive.Slice(start * 2, BlockFrames * 2);

        for (int a = 0; a <= MaxOffset; a++)
        {
            if (Matches(reference, block, start + a)) return Found(a);
            if (a != 0 && Matches(reference, block, start - a)) return Found(-a);
        }

        return new OffsetDetectionResult(OffsetDetectionStatus.NotFound, null,
            "Offset non rilevato: inserire il valore manualmente (da -5000 a +5000 campioni).");
    }

    private static OffsetDetectionResult Found(int k) => new(OffsetDetectionStatus.Found, k, null);

    private static bool Matches(ReadOnlySpan<short> reference, ReadOnlySpan<short> block, int refStartFrame)
        => reference.Slice(refStartFrame * 2, block.Length).SequenceEqual(block);
}
