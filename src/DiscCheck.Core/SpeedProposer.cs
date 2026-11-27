namespace DiscCheck.Core;

/// <summary>Speeds proposed by the "ricerca velocità ottimale" wizard; <see cref="Message"/> is set when there are none.</summary>
public sealed record SpeedProposal(IReadOnlyList<double> SpeedsX, string? Message);

/// <summary>Pure logic of the speed wizard (Requirements 19.6, 19.7).</summary>
public static class SpeedProposer
{
    public const double UpperX = 12;
    public const double LowerRangeX = 4;
    public const string UndeterminableMessage =
        "Impossibile determinare le velocità supportate dal drive: nessuna velocità proposta, la selezione resta invariata.";

    /// <summary>
    /// Supported speeds between the minimum supported and 12x (inclusive); when none falls in 4x-12x,
    /// only the minimum supported speed. An empty list yields no proposal and a message.
    /// </summary>
    public static SpeedProposal Propose(IReadOnlyList<int> supportedKBps)
    {
        ArgumentNullException.ThrowIfNull(supportedKBps);
        var speeds = supportedKBps.Where(k => k > 0).Distinct().OrderBy(k => k).Select(ScanSpeeds.ToX).ToList();
        if (speeds.Count == 0)
            return new SpeedProposal([], UndeterminableMessage);

        double min = speeds[0];
        var inRange = speeds.Where(x => x >= min && x <= UpperX).ToList();
        if (!inRange.Any(x => x >= LowerRangeX))
            return new SpeedProposal([min], null);
        return new SpeedProposal(inRange, null);
    }
}
