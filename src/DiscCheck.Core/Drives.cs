namespace DiscCheck.Core;

/// <summary>Identified optical drive (Requirement 4.2-4.3). Null/unknown values mean "non determinato" or "non eseguito".</summary>
public record DriveInfoEx(char Letter, string Vendor, string Model, string Firmware,
                          bool C2Supported, IReadOnlyList<int> SupportedSpeedsKBps,
                          int MaxReadSpeedKBps, int? ReadOffset, bool? CachesAudio, int? CacheKb, bool? C2Reliable);

/// <summary>
/// One enumerated CD-ROM unit: either identified (<see cref="Info"/> set) or "non identificabile"
/// (<see cref="UnidentifiableReason"/> set, Requirement 4.6).
/// </summary>
public sealed record DriveListItem(char Letter, DriveInfoEx? Info, string? UnidentifiableReason)
{
    public bool IsIdentified => Info is not null;
}

/// <summary>Result of an enumeration. <see cref="Message"/> is set when there is nothing to list (Requirement 4.7).</summary>
public sealed record DriveEnumerationResult(IReadOnlyList<DriveListItem> Items, string? Message);

/// <summary>Lists the optical drives (Requirement 4.1).</summary>
public interface IDriveEnumerator
{
    DriveEnumerationResult Enumerate();
}

/// <summary>Outcome of "Reset drive" (Requirements 4.9, 4.10, 26.5, 26.6).</summary>
public sealed record DriveResetResult(bool Success, string Message);

/// <summary>Ends a residual Test_Mode and checks that the drive is ready again.</summary>
public interface IDriveResetService
{
    /// <summary>Sends the end-of-scan command, then TEST UNIT READY. Never throws for device errors.</summary>
    DriveResetResult Reset(char driveLetter);
}
