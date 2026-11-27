namespace DiscCheck.Core;

/// <summary>Lite-On vendor command method (Requirement 8.3).</summary>
public enum LiteOnMethod { New, Old }

/// <summary>What a scanner can measure on a drive (design: Core interfaces).</summary>
public record ScannerCapabilities(bool Supported, bool C1, bool C2, bool CU, string? Reason);

/// <summary>Options of one scan. <see cref="SpeedKBps"/> is in kB/s (1x = 176).</summary>
public record ScanOptions(char Drive, int SpeedKBps, LiteOnMethod Method, Toc Toc);

/// <summary>Progress notification of a running scan.</summary>
public record ScanProgress(int Lba, double Percent, double? SpeedX);

/// <summary>A quality scanner (Lite-On vendor commands, standard C2, ...). Added to the app through DI only.</summary>
public interface IQualityScanner
{
    string Name { get; }
    ScannerCapabilities Probe(DriveInfoEx drive);
    IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken ct);
}

/// <summary>
/// A scan stopped by a failed command. Samples already published are kept by the consumer.
/// <see cref="Message"/> is user-facing and reports the LBA and the Sense_Data (Key, ASC, ASCQ) when available.
/// </summary>
public class ScanFailedException(string message, int lba, byte? senseKey = null, byte? asc = null, byte? ascq = null)
    : Exception(message)
{
    public int Lba { get; } = lba;
    public byte? SenseKey { get; } = senseKey;
    public byte? Asc { get; } = asc;
    public byte? Ascq { get; } = ascq;
}

/// <summary>
/// The scanner's init was rejected or timed out (Lite-On: both methods failed, Requirements 8.8, 26.3). No sample was
/// produced and the drive never entered Test_Mode. The App proposes the StandardC2 fallback, to be started only on confirm.
/// </summary>
public sealed class ScanInitFailedException(string message, byte? senseKey = null, byte? asc = null, byte? ascq = null)
    : ScanFailedException(message, 0, senseKey, asc, ascq);

/// <summary>User setting "variante Lite-On" (Requirement 8.4-8.5). Default <see cref="LiteOnMethod.New"/>; replaced by the Settings_Store (15.4).</summary>
public interface ILiteOnMethodSetting
{
    LiteOnMethod Method { get; }
}

/// <summary>Minimal in-memory <see cref="ILiteOnMethodSetting"/> until the settings store exists.</summary>
public sealed class LiteOnMethodSetting(LiteOnMethod method = LiteOnMethod.New) : ILiteOnMethodSetting
{
    public LiteOnMethod Method { get; set; } = method;
}

/// <summary>Hardware facts the App needs to word its messages (Requirements 8.9, 26.4).</summary>
public interface IDriveHardwareHints
{
    /// <summary>True when the drive is attached through a USB bridge; false when not; null when it cannot be determined.</summary>
    bool? IsUsbBus(char driveLetter);

    /// <summary>True when the drive is known to use a MediaTek chipset.</summary>
    bool IsMediaTek(DriveInfoEx drive);
}