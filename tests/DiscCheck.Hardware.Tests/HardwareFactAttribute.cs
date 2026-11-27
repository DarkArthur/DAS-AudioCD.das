using System.Runtime.Versioning;
using System.Security.Principal;
using DiscCheck.Core;
using DiscCheck.Scsi;
using DiscCheck.Scsi.Drives;

namespace DiscCheck.Hardware.Tests;

/// <summary>
/// <c>[Fact]</c> for tests that need a Lite-On drive. The skip reason is computed at discovery, so a missing
/// drive, missing administrator rights or (optionally) a missing disc skips the test instead of failing it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiteOnHardwareFactAttribute : FactAttribute
{
    public LiteOnHardwareFactAttribute(bool requireDisc = false)
    {
        Skip = LiteOnDrive.SkipReason(requireDisc);
    }
}

/// <summary>Locates the Lite-On iHAS124 (via ICY BOX bridge) used by the M1 hardware tests.</summary>
public static class LiteOnDrive
{
    public const string VendorPrefix = "LITE-ON";
    public const string ModelPrefix = "iHAS124";

    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return IsAdministratorWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministratorWindows()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>The first identified drive with vendor "LITE-ON" and model starting with "iHAS124", or null.</summary>
    public static DriveInfoEx? Find()
    {
        if (!OperatingSystem.IsWindows() || !IsAdministrator()) return null;
        try
        {
            var result = new DriveEnumerator(new WinScsiTransportFactory()).Enumerate();
            return result.Items
                .Select(i => i.Info)
                .FirstOrDefault(i => i is not null
                    && i.Vendor.Trim().StartsWith(VendorPrefix, StringComparison.OrdinalIgnoreCase)
                    && i.Model.Trim().StartsWith(ModelPrefix, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? SkipReason(bool requireDisc)
    {
        if (!OperatingSystem.IsWindows()) return "Hardware test: richiede Windows.";
        if (!IsAdministrator()) return "Hardware test: richiede privilegi di amministratore.";
        var drive = Find();
        if (drive is null) return $"Hardware test: nessun drive {VendorPrefix} {ModelPrefix}* trovato.";
        if (!requireDisc) return null;

        using var transport = new WinScsiPassThrough(drive.Letter);
        var tur = MmcCommands.TestUnitReady(transport);
        return tur.IsSuccess ? null : $"Hardware test: nessun disco pronto in {drive.Letter}:.";
    }
}
