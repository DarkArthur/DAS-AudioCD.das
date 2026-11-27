namespace DiscCheck.Core;

/// <summary>
/// Outcome of a scanner selection. <see cref="Scanner"/> is null when no scan can start;
/// <see cref="Message"/> then explains why (Requirements 6.4, 6.6).
/// </summary>
public sealed record ScannerSelection(IQualityScanner? Scanner, string? Message)
{
    public bool CanScan => Scanner is not null;
}

/// <summary>
/// Chooses the quality scanner for a drive (Requirement 6). Probes in fixed order (LiteOn, then StandardC2,
/// then any other registered scanner), each with a timeout, treating exceptions and timeouts as "not supported".
/// New scanners are added only by registering <see cref="IQualityScanner"/> in DI.
/// </summary>
public sealed class ScannerSelector
{
    public const string LiteOnName = "LiteOn";
    public const string StandardC2Name = "StandardC2";
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IQualityScanner> _ordered;
    private readonly Action<string> _log;
    private readonly TimeSpan _timeout;

    public ScannerSelector(IEnumerable<IQualityScanner> scanners, Action<string>? log = null, TimeSpan? probeTimeout = null)
    {
        var list = scanners.ToList();
        _ordered = list
            .Select((s, i) => (s, i))
            .OrderBy(x => Rank(x.s.Name))
            .ThenBy(x => x.i)
            .Select(x => x.s)
            .ToList();
        _log = log ?? (_ => { });
        _timeout = probeTimeout ?? DefaultProbeTimeout;
    }

    /// <summary>Scanners in probe order.</summary>
    public IReadOnlyList<IQualityScanner> Scanners => _ordered;

    /// <summary>
    /// Selects a scanner. With <paramref name="forceStandard"/> only StandardC2 is probed
    /// (Requirements 6.3, 6.4); otherwise the first supporting scanner in order wins and later ones are not probed (6.2).
    /// </summary>
    public ScannerSelection Select(DriveInfoEx drive, bool forceStandard = false)
    {
        if (forceStandard)
        {
            var standard = _ordered.FirstOrDefault(s => s.Name == StandardC2Name);
            if (standard is null)
                return new ScannerSelection(null, "Lo scanner standard non è disponibile.");
            var caps = SafeProbe(standard, drive);
            return caps.Supported
                ? new ScannerSelection(standard, null)
                : new ScannerSelection(null, caps.Reason ?? "Lo scanner standard non supporta questo drive.");
        }

        foreach (var scanner in _ordered)
        {
            if (SafeProbe(scanner, drive).Supported)
                return new ScannerSelection(scanner, null);
        }
        return new ScannerSelection(null, "Il drive non supporta la scansione di qualità.");
    }

    private ScannerCapabilities SafeProbe(IQualityScanner scanner, DriveInfoEx drive)
    {
        try
        {
            var task = Task.Run(() => scanner.Probe(drive));
            if (!task.Wait(_timeout))
            {
                _log($"Probe of scanner '{scanner.Name}' timed out after {_timeout.TotalSeconds:0.#} s.");
                return Unsupported("Probe non completato entro il timeout.");
            }
            return task.Result;
        }
        catch (AggregateException ex)
        {
            var inner = ex.InnerException ?? ex;
            _log($"Probe of scanner '{scanner.Name}' failed: {inner}");
            return Unsupported($"Probe fallito: {inner.Message}");
        }
        catch (Exception ex)
        {
            _log($"Probe of scanner '{scanner.Name}' failed: {ex}");
            return Unsupported($"Probe fallito: {ex.Message}");
        }
    }

    private static ScannerCapabilities Unsupported(string reason) => new(false, false, false, false, reason);

    private static int Rank(string name) => name switch
    {
        LiteOnName => 0,
        StandardC2Name => 1,
        _ => 2,
    };
}
