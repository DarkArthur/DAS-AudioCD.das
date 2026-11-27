namespace DiscCheck.Core;

/// <summary>
/// Groups sectors into Scan_Samples of 75 sectors (one disc second) plus a final residual group of 1-74 sectors
/// (Requirements 7.6-7.8). C1 is null and CU is 0 (StandardC2Scanner).
/// </summary>
public sealed class SampleAggregator
{
    /// <summary>Sectors per disc second.</summary>
    public const int SectorsPerSample = 75;

    private int _startLba;
    private int _sectors;
    private int _c2;
    private double _seconds;
    private int _sampleIndex;

    /// <summary>
    /// Adds one sector. <paramref name="readSeconds"/> is the real time spent reading it (a block time is split
    /// evenly across its sectors by <see cref="AddBlock"/>). Returns a sample when the group reaches 75 sectors.
    /// </summary>
    public ScanSample? AddSector(int lba, int c2, double readSeconds)
    {
        if (_sectors == 0) _startLba = lba;
        _sectors++;
        _c2 += c2;
        _seconds += readSeconds;
        return _sectors == SectorsPerSample ? Emit() : null;
    }

    /// <summary>Adds a block of consecutive sectors read in <paramref name="elapsedSeconds"/>; returns the samples completed.</summary>
    public IReadOnlyList<ScanSample> AddBlock(int firstLba, ReadOnlySpan<int> sectorC2, double elapsedSeconds)
    {
        var result = new List<ScanSample>();
        if (sectorC2.Length == 0) return result;
        double perSector = elapsedSeconds / sectorC2.Length;
        for (int i = 0; i < sectorC2.Length; i++)
        {
            var s = AddSector(firstLba + i, sectorC2[i], perSector);
            if (s is not null) result.Add(s);
        }
        return result;
    }

    /// <summary>Publishes the residual group of 1-74 sectors, or null when there is none.</summary>
    public ScanSample? Flush() => _sectors > 0 ? Emit() : null;

    /// <summary>SpeedX = sectors / 75 / real seconds; null when no time elapsed.</summary>
    public static double? ComputeSpeedX(int sectors, double seconds) =>
        seconds > 0 ? Math.Round(sectors / (double)SectorsPerSample / seconds, 2) : null;

    private ScanSample Emit()
    {
        var sample = new ScanSample(_startLba, _sampleIndex, null, _c2, 0, ComputeSpeedX(_sectors, _seconds));
        _sampleIndex++;
        _sectors = 0;
        _c2 = 0;
        _seconds = 0;
        return sample;
    }
}
