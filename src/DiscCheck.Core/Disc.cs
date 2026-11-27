namespace DiscCheck.Core;

/// <summary>Outcome category of reading the inserted disc (Requirements 5.10, 5.12, 24.7).</summary>
public enum DiscStatus
{
    /// <summary>A CD with audio tracks was read; TOC is available.</summary>
    Audio,
    NoDisc,
    NotReady,
    /// <summary>A CD is present but has no audio track.</summary>
    NotAudio,
    /// <summary>DVD or BD: unsupported in v1 (Requirement 24.7).</summary>
    UnsupportedMedium,
    /// <summary>Command or parsing failure; <see cref="DiscReadResult.Message"/> carries the cause.</summary>
    ReadError,
}

/// <summary>Result of reading TOC and ATIP. Only <see cref="DiscStatus.Audio"/> carries disc data.</summary>
public sealed record DiscReadResult(
    DiscStatus Status,
    string Message,
    Toc? Toc = null,
    string? TocHash = null,
    AtipTime? MediaId = null,
    string? Manufacturer = null,
    string? MediaUnavailableReason = null)
{
    public bool HasDisc => Status == DiscStatus.Audio;
}

/// <summary>Reads TOC, ATIP and medium type of the disc in a drive. Never throws for device errors.</summary>
public interface IDiscReader
{
    DiscReadResult Read(char driveLetter);

    /// <summary>Ejects the tray. Returns null on success or the error message.</summary>
    string? Eject(char driveLetter);
}

/// <summary>Medium families derived from the GET CONFIGURATION current profile.</summary>
public enum MediumKind { Unknown, CdRom, CdR, CdRw, Dvd, Bd, Other }

public static class MediumProfiles
{
    public static MediumKind Classify(int profile) => profile switch
    {
        0x0008 => MediumKind.CdRom,
        0x0009 => MediumKind.CdR,
        0x000A => MediumKind.CdRw,
        >= 0x0010 and <= 0x002F => MediumKind.Dvd,
        >= 0x0040 and <= 0x004F => MediumKind.Bd,
        >= 0x0050 and <= 0x005F => MediumKind.Dvd, // HD DVD: treated as unsupported high-density medium
        0 => MediumKind.Unknown,
        _ => MediumKind.Other,
    };
}

/// <summary>
/// Links a disc (by <c>toc_hash</c>) to a <c>media</c> row, in one transaction (Requirement 5.9).
/// Implemented by the persistence layer in M4.
/// </summary>
public interface IDiscMediaAssociation
{
    void Associate(string tocHash, AtipTime mediaId, string manufacturer);
}
