using System.Globalization;
using DiscCheck.Core;

namespace DiscCheck.Verify;

/// <summary>Outcome of a manual offset entry.</summary>
public sealed record ManualOffsetResult(bool Accepted, int? Offset, string? Error);

/// <summary>
/// Automatic detection and manual entry of the Read_Offset, with persistence in <c>drives.read_offset</c>
/// (Requirements 15.1-15.7).
/// </summary>
public sealed class OffsetService
{
    private readonly IDriveRepository _drives;

    public OffsetService(IDriveRepository drives) => _drives = drives;

    public static string RangeMessage =>
        $"Il valore deve essere un intero compreso tra -{OffsetDetector.MaxOffset} e +{OffsetDetector.MaxOffset} campioni.";

    /// <summary>Accepts only integers in [-5000, +5000] (Requirements 15.2, 15.3). Does not save.</summary>
    public static ManualOffsetResult ValidateManual(string? text)
    {
        if (int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int k)
            && Math.Abs(k) <= OffsetDetector.MaxOffset)
        {
            return new ManualOffsetResult(true, k, null);
        }
        return new ManualOffsetResult(false, null, RangeMessage);
    }

    /// <summary>Runs the detection on the first track; on success saves the offset, otherwise leaves the drive untouched.</summary>
    public OffsetDetectionResult DetectAndSave(DriveRecord drive, ReadOnlySpan<short> reference, ReadOnlySpan<short> driveAudio)
    {
        var result = OffsetDetector.Detect(reference, driveAudio);
        if (result.IsFound) Save(drive, result.Offset!.Value);
        return result;
    }

    /// <summary>Validates and saves a manually entered offset; on rejection the previous value is kept.</summary>
    public ManualOffsetResult SetManual(DriveRecord drive, string? text)
    {
        var r = ValidateManual(text);
        if (r.Accepted) Save(drive, r.Offset!.Value);
        return r;
    }

    private void Save(DriveRecord drive, int offset) => _drives.AddIfMissing(drive with { ReadOffset = offset });
}
