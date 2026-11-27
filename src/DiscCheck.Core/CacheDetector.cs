using System.Globalization;

namespace DiscCheck.Core;

/// <summary>Outcome of one block read performed by the injected read delegate. <see cref="Error"/> is null on GOOD status.</summary>
/// <param name="Error">User-facing description (with sense data where available) when the read did not end with GOOD.</param>
public readonly record struct CacheReadResult(string? Error)
{
    public bool IsSuccess => Error is null;
    public static CacheReadResult Ok { get; } = new(null);
    public static CacheReadResult Fail(string error) => new(error);
}

/// <summary>Reads <paramref name="sectors"/> CD-DA sectors starting at <paramref name="lba"/> with READ CD.</summary>
public delegate CacheReadResult CacheBlockReader(int lba, int sectors);

/// <summary>
/// Result of a cache detection run. When <see cref="Completed"/> is false, <see cref="CachesAudio"/> and
/// <see cref="CacheKb"/> are null and nothing must be saved (Requirement 12.5).
/// </summary>
public sealed record CacheDetectionResult(
    bool Completed,
    bool? CachesAudio,
    int? CacheKb,
    string? AbortReason,
    IReadOnlyList<string> Warnings,
    double MedianT1Ms = 0,
    double MedianT2Ms = 0)
{
    public static CacheDetectionResult Aborted(string reason) => new(false, null, null, reason, []);
}

/// <summary>
/// Cache detection logic (Requirements 12.1-12.5, 12.10). All I/O is injected: a block reader and a millisecond clock.
/// </summary>
public static class CacheDetector
{
    public const int BlockSectors = 27;
    public const int Pairs = 3;
    public const int MinDiscSectors = 28_500;
    public const double Ratio = 0.5;
    public const int BytesPerSector = 2352;
    public const int MaxReadChunkSectors = 27;

    /// <summary>Probe area sizes, ascending (Requirement 12.3).</summary>
    public static readonly IReadOnlyList<int> AreaSizesKb = [256, 512, 1024, 2048, 4096, 8192];

    public const int Ihas124ReferenceKb = 2048;
    public const int Ihas124MinKb = 1536;
    public const int Ihas124MaxKb = 2560;

    /// <summary>Median of a non-empty sample (mean of the two central values for an even count).</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        var s = values.OrderBy(v => v).ToArray();
        int n = s.Length;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
    }

    /// <summary>Cached when median(T2) &lt;= 50% of median(T1) (Requirement 12.2).</summary>
    public static bool ClassifyFromMedians(double medianT1, double medianT2) => medianT2 <= Ratio * medianT1;

    /// <summary>Classifies from the raw T1/T2 samples by their medians (Requirement 12.2).</summary>
    public static bool Classify(IReadOnlyList<double> t1Samples, IReadOnlyList<double> t2Samples) =>
        ClassifyFromMedians(Median(t1Samples), Median(t2Samples));

    /// <summary>Sectors needed to cover <paramref name="kb"/> kilobytes of CD-DA data.</summary>
    public static int SectorsForKb(int kb) => (int)Math.Ceiling(kb * 1024.0 / BytesPerSector);

    /// <summary>Warning for the iHAS124 when the estimate is outside 1.536-2.560 KB (Requirement 12.10); null otherwise.</summary>
    public static string? Ihas124Warning(int cacheKb) =>
        cacheKb is >= Ihas124MinKb and <= Ihas124MaxKb
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"iHAS124: dimensione cache stimata {cacheKb} KB fuori dall'intervallo atteso {Ihas124MinKb}-{Ihas124MaxKb} KB (valore di riferimento {Ihas124ReferenceKb} KB).");

    /// <param name="toc">TOC of the inserted disc.</param>
    /// <param name="read">Block reader (READ CD).</param>
    /// <param name="nowMs">Monotonic clock in milliseconds.</param>
    /// <param name="isIhas124">Enables the reference-size warning.</param>
    public static CacheDetectionResult Detect(Toc toc, CacheBlockReader read, Func<double> nowMs, bool isIhas124 = false)
    {
        if (toc.Tracks.Count == 0 || !toc.Tracks[0].IsAudio)
            return CacheDetectionResult.Aborted("Il disco inserito non è un CD audio.");
        if (toc.TotalSectors < MinDiscSectors)
            return CacheDetectionResult.Aborted(string.Create(CultureInfo.InvariantCulture,
                $"Il disco ha {toc.TotalSectors} settori: ne servono almeno {MinDiscSectors} (circa 64 MB) per rilevare la cache."));

        int trackStart = toc.Tracks[0].StartLba;
        int trackLen = toc.TrackSectors(0);
        if (trackLen < BlockSectors)
            return CacheDetectionResult.Aborted("La prima traccia è troppo corta per il rilevamento della cache.");

        int x = trackStart + (trackLen - BlockSectors) / 2;

        string? failure = null;
        bool Read(int lba, int count)
        {
            var r = read(lba, count);
            if (r.IsSuccess) return true;
            failure = string.Create(CultureInfo.InvariantCulture, $"Lettura non riuscita a LBA {lba}: {r.Error}");
            return false;
        }

        int? AreaStart(int kb)
        {
            int sectors = SectorsForKb(kb);
            int after = x + BlockSectors;
            if (after + sectors <= toc.LeadOutLba) return after;
            int before = x - sectors;
            return before >= trackStart ? before : null;
        }

        bool ReadArea(int kb)
        {
            int? start = AreaStart(kb);
            if (start is null)
            {
                failure = "Area di lettura per il rilevamento della cache non disponibile sul disco.";
                return false;
            }

            int remaining = SectorsForKb(kb);
            int lba = start.Value;
            while (remaining > 0)
            {
                int n = Math.Min(MaxReadChunkSectors, remaining);
                if (!Read(lba, n)) return false;
                lba += n;
                remaining -= n;
            }

            return true;
        }

        double TimedRead()
        {
            double t0 = nowMs();
            bool ok = Read(x, BlockSectors);
            double dt = nowMs() - t0;
            return ok ? dt : double.NaN;
        }

        var t1 = new List<double>(Pairs);
        var t2 = new List<double>(Pairs);
        int flushKb = AreaSizesKb[^1];
        for (int i = 0; i < Pairs; i++)
        {
            // Flush between pairs so that every T1 is a read of an uncached block.
            if (i > 0 && !ReadArea(flushKb)) return CacheDetectionResult.Aborted(failure!);

            double a = TimedRead();
            if (double.IsNaN(a)) return CacheDetectionResult.Aborted(failure!);
            double b = TimedRead();
            if (double.IsNaN(b)) return CacheDetectionResult.Aborted(failure!);
            t1.Add(a);
            t2.Add(b);
        }

        double m1 = Median(t1), m2 = Median(t2);
        bool caches = ClassifyFromMedians(m1, m2);
        int cacheKb = 0;

        if (caches)
        {
            // Largest area for which the re-read is still fast; lower bound is the smallest probe area.
            cacheKb = AreaSizesKb[0];
            foreach (int kb in AreaSizesKb)
            {
                if (!Read(x, BlockSectors)) return CacheDetectionResult.Aborted(failure!);
                if (!ReadArea(kb)) return CacheDetectionResult.Aborted(failure!);
                double t = TimedRead();
                if (double.IsNaN(t)) return CacheDetectionResult.Aborted(failure!);
                if (!ClassifyFromMedians(m1, t)) break;
                cacheKb = kb;
            }
        }

        var warnings = new List<string>();
        if (isIhas124 && Ihas124Warning(cacheKb) is { } w) warnings.Add(w);
        return new CacheDetectionResult(true, caches, cacheKb, null, warnings, m1, m2);
    }
}
