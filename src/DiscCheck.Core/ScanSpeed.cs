namespace DiscCheck.Core;

/// <summary>
/// Sets the read speed of a drive with SET CD SPEED. The scanners do not send it: the caller must do it
/// before the pipeline starts (Requirement 10.2).
/// </summary>
public interface IScanSpeedService
{
    /// <summary>Returns null on success or a user-facing message when the drive refuses or does not answer (10.7).</summary>
    string? SetSpeed(char driveLetter, int speedKBps);
}

/// <summary>Pure helpers for the scan speed selector (Requirements 10.1, 10.6).</summary>
public static class ScanSpeeds
{
    /// <summary>kB/s of 1x for audio CD.</summary>
    public const int KBpsPerX = 176;

    /// <summary>SET CD SPEED value meaning "maximum".</summary>
    public const int MaximumKBps = 0xFFFF;

    public static int ToKBps(int x) => x * KBpsPerX;

    public static double ToX(int kBps) => kBps / (double)KBpsPerX;

    /// <summary>An empty supported list means "not declared": every speed is accepted.</summary>
    public static bool IsSupported(IReadOnlyList<int> supportedKBps, int requestedKBps) =>
        supportedKBps.Count == 0 || supportedKBps.Contains(requestedKBps);

    /// <summary>Nearest supported speed; at equal distance the lower one. Null when the list is empty.</summary>
    public static int? Nearest(IReadOnlyList<int> supportedKBps, int requestedKBps)
    {
        int? best = null;
        foreach (var s in supportedKBps)
        {
            if (best is null) { best = s; continue; }
            int d = Math.Abs(s - requestedKBps);
            int bd = Math.Abs(best.Value - requestedKBps);
            if (d < bd || (d == bd && s < best.Value)) best = s;
        }
        return best;
    }
}
