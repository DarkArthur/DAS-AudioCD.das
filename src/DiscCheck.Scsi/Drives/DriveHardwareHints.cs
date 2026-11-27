using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiscCheck.Core;
using Microsoft.Win32.SafeHandles;

namespace DiscCheck.Scsi.Drives;

/// <summary>
/// <see cref="IDriveHardwareHints"/> for Windows. The bus type comes from <c>IOCTL_STORAGE_QUERY_PROPERTY</c>
/// (StorageAdapterProperty); a failure yields "unknown" (null). The chipset is not exposed by INQUIRY or MMC, so
/// MediaTek is recognised only by the vendor/model list passed in (empty by default).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class DriveHardwareHints(IEnumerable<string>? mediaTekModels = null) : IDriveHardwareHints
{
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const uint StorageAdapterProperty = 1;
    private const uint PropertyStandardQuery = 0;
    private const byte BusTypeUsb = 7;
    private const uint FileShareReadWrite = 3;
    private const uint OpenExisting = 3;

    private readonly HashSet<string> _mediaTek =
        new((mediaTekModels ?? []).Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);

    public bool? IsUsbBus(char driveLetter)
    {
        if (!char.IsAsciiLetter(driveLetter)) return null;
        using var handle = CreateFile($@"\\.\{char.ToUpperInvariant(driveLetter)}:", 0, FileShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid) return null;

        uint* query = stackalloc uint[3]; // PropertyId, QueryType, AdditionalParameters[1]
        query[0] = StorageAdapterProperty;
        query[1] = PropertyStandardQuery;
        query[2] = 0;
        byte* output = stackalloc byte[64];
        if (!DeviceIoControl(handle, IoctlStorageQueryProperty, query, 12, output, 64, out uint returned, 0) || returned < 25)
            return null;
        return output[24] == BusTypeUsb; // STORAGE_ADAPTER_DESCRIPTOR.BusType
    }

    public bool IsMediaTek(DriveInfoEx drive) =>
        _mediaTek.Contains($"{drive.Vendor.Trim()} {drive.Model.Trim()}") || _mediaTek.Contains(drive.Model.Trim());

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* inBuffer, uint inSize,
        void* outBuffer, uint outSize, out uint bytesReturned, nint overlapped);
}
