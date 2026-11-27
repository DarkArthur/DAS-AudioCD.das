using DiscCheck.Core;
using Serilog;

namespace DiscCheck.Scsi.Drives;

/// <summary>Opens an <see cref="IScsiTransport"/> for a drive letter. Never throws: open errors surface from Execute.</summary>
public interface IScsiTransportFactory
{
    IScsiTransport Open(char driveLetter);
}

/// <summary>Production factory over <see cref="WinScsiPassThrough"/>.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WinScsiTransportFactory(ILogger? log = null, Func<TimeSpan>? defaultTimeout = null) : IScsiTransportFactory
{
    public IScsiTransport Open(char driveLetter) => new WinScsiPassThrough(driveLetter, log, defaultTimeout);
}

/// <summary>
/// Lists CD-ROM units (<c>DriveType.CDRom</c>) and identifies each with INQUIRY and MODE SENSE 0x2A.
/// All hardware access goes through <see cref="IScsiTransport"/> (Requirement 4.1-4.4, 4.6, 4.7).
/// </summary>
public sealed class DriveEnumerator : IDriveEnumerator
{
    public const string NoDrivesMessage = "Nessun drive ottico rilevato.";

    private readonly IScsiTransportFactory _transports;
    private readonly Func<IReadOnlyList<char>> _cdRomLetters;
    private readonly ILogger _log;

    public DriveEnumerator(IScsiTransportFactory transports, Func<IReadOnlyList<char>>? cdRomLetters = null, ILogger? log = null)
    {
        _transports = transports;
        _cdRomLetters = cdRomLetters ?? SystemCdRomLetters;
        _log = log ?? Log.Logger;
    }

    /// <summary>Letters of all <c>DriveType.CDRom</c> units, with or without a disc inserted.</summary>
    public static IReadOnlyList<char> SystemCdRomLetters() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.CDRom)
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .ToArray();

    public DriveEnumerationResult Enumerate()
    {
        IReadOnlyList<char> letters;
        try
        {
            letters = _cdRomLetters();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Drive enumeration failed");
            return new DriveEnumerationResult([], $"Impossibile elencare le unità: {ex.Message}");
        }

        if (letters.Count == 0)
            return new DriveEnumerationResult([], NoDrivesMessage);

        var items = new List<DriveListItem>(letters.Count);
        foreach (var letter in letters)
            items.Add(Identify(letter));
        return new DriveEnumerationResult(items, null);
    }

    private DriveListItem Identify(char letter)
    {
        using var transport = _transports.Open(letter);

        var inquiry = MmcCommands.Inquiry(transport);
        if (!inquiry.TryGetValue(out var inq))
        {
            var reason = ScsiErrorMessages.Describe(inquiry.Error!);
            _log.Warning("INQUIRY failed on {Drive}: {Reason}", letter, reason);
            return new DriveListItem(letter, null, reason);
        }

        // MODE SENSE failure does not make the drive unidentifiable: C2 support is reported as "non supportati".
        var page = MmcCommands.ModeSense10Page2A(transport);
        bool c2 = false;
        IReadOnlyList<int> speeds = [];
        int maxRead = 0;
        if (page.TryGetValue(out var p))
        {
            c2 = p.C2PointersSupported;
            speeds = p.SupportedSpeedsKBps;
            maxRead = p.MaxReadSpeedKBps;
        }
        else
        {
            _log.Warning("MODE SENSE 0x2A failed on {Drive}: {Reason}", letter, ScsiErrorMessages.Describe(page.Error!));
        }

        var info = new DriveInfoEx(letter, inq.Vendor, inq.Product, inq.Revision, c2, speeds, maxRead,
            ReadOffset: null, CachesAudio: null, CacheKb: null, C2Reliable: null);
        return new DriveListItem(letter, info, null);
    }
}
