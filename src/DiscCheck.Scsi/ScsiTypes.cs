namespace DiscCheck.Scsi;

/// <summary>Data transfer direction of a SCSI command.</summary>
public enum DataDirection { None, In, Out }

/// <summary>Fixed-format sense data: sense key, additional sense code and qualifier.</summary>
public readonly record struct SenseData(byte Key, byte Asc, byte Ascq);

/// <summary>Transport-level failure (not a SCSI status). Errors are values, never exceptions.</summary>
public abstract record TransportError
{
    private TransportError() { }

    /// <summary>The command did not complete within the timeout. Carries the CDB (hex) and applied timeout when known.</summary>
    public sealed record Timeout(string? Cdb = null, TimeSpan? Applied = null) : TransportError;

    /// <summary>A Win32 error was returned by the OS.</summary>
    public sealed record WinError(int Code) : TransportError;

    /// <summary>The device could not be opened. Carries the drive letter and Win32 error code when known.</summary>
    public sealed record OpenFailed(char? Drive = null, int ErrorCode = 0) : TransportError;
}

/// <summary>Outcome of a SCSI command execution.</summary>
public readonly record struct ScsiResult(
    byte ScsiStatus,
    SenseData? Sense,
    int BytesTransferred,
    TransportError? Error)
{
    public const byte StatusGood = 0x00;
    public const byte StatusCheckCondition = 0x02;

    /// <summary>True when there is no transport error and the SCSI status is GOOD.</summary>
    public bool IsGood => Error is null && ScsiStatus == StatusGood;
}

/// <summary>Abstraction over the SCSI pass-through used by all hardware access.</summary>
public interface IScsiTransport : IDisposable
{
    ScsiResult Execute(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout);
}
