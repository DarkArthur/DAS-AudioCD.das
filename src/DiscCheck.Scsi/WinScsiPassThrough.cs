using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Serilog;
using Serilog.Events;

namespace DiscCheck.Scsi;

/// <summary>
/// <see cref="IScsiTransport"/> over the Windows SCSI pass-through (<c>IOCTL_SCSI_PASS_THROUGH_DIRECT</c>).
/// Never throws to callers: SCSI status, Win32 errors and timeouts are returned as values in
/// <see cref="ScsiResult"/>. No automatic retry is ever performed (Requirement 2.14).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WinScsiPassThrough : IScsiTransport
{
    /// <summary>Default command timeout (Requirement 2.6).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for the scan-start commands (Requirement 2.6).</summary>
    public static readonly TimeSpan ScanStartTimeout = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(600);

    private const int SenseLength = 32;
    private const uint IoctlScsiPassThroughDirect = 0x4D014;
    private const uint IoctlScsiGetCapabilities = 0x41010;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;

    private const byte ScsiIoctlDataOut = 0;
    private const byte ScsiIoctlDataIn = 1;
    private const byte ScsiIoctlDataUnspecified = 2;

    private const int ErrorInvalidParameter = 87;
    private const int ErrorSemTimeout = 121;

    private readonly Func<TimeSpan>? _defaultTimeout;
    private readonly ILogger _log;
    private readonly char _driveLetter;
    private readonly SafeFileHandle? _handle;
    private readonly TransportError? _openError;
    private readonly nuint _alignment;
    private readonly Lock _gate = new();

    private SptdWithBuffers* _block;
    private byte* _dataBuffer;
    private nuint _dataCapacity;
    private bool _disposed;

    /// <summary>
    /// Opens <c>\\.\X:</c>. On failure no exception is thrown: <see cref="OpenError"/> is set and every
    /// <see cref="Execute"/> call returns it (Requirements 2.3, 2.9, 2.12).
    /// </summary>
    /// <param name="driveLetter">Drive letter, e.g. 'E'.</param>
    /// <param name="log">Logger; Verbose output is emitted only if the logger has Verbose enabled (Modalità_Debug).</param>
    /// <param name="defaultTimeout">
    /// Per-command timeout from the settings (Requirement 22.4). It replaces <see cref="DefaultTimeout"/> whenever a
    /// caller asks for that default; explicit longer timeouts (scan start, end of scan) are left unchanged.
    /// </param>
    public WinScsiPassThrough(char driveLetter, ILogger? log = null, Func<TimeSpan>? defaultTimeout = null)
    {
        _defaultTimeout = defaultTimeout;
        _log = log ?? Serilog.Log.Logger;
        _driveLetter = char.ToUpperInvariant(driveLetter);
        _alignment = 16;

        if (!char.IsAsciiLetter(_driveLetter))
        {
            _openError = new TransportError.OpenFailed(_driveLetter, ErrorInvalidParameter);
            return;
        }

        var handle = CreateFile($@"\\.\{_driveLetter}:", GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            handle.Dispose();
            _openError = new TransportError.OpenFailed(_driveLetter, code);
            _log.Warning("SCSI: cannot open drive {Drive}: Win32 error {Code}", _driveLetter, code);
            return;
        }

        _handle = handle;
        _alignment = QueryAlignment(handle);
        _block = (SptdWithBuffers*)NativeMemory.AlignedAlloc((nuint)sizeof(SptdWithBuffers), 16);
    }

    /// <summary>Error from opening the drive, or null if the drive was opened.</summary>
    public TransportError? OpenError => _openError;

    /// <summary>True when the Win32 error code means insufficient privileges (ERROR_ACCESS_DENIED).</summary>
    public bool OpenFailedForPrivileges => _openError is TransportError.OpenFailed { ErrorCode: 5 };

    public ScsiResult Execute(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout)
    {
        if (_openError is not null)
            return new ScsiResult(0, null, 0, _openError);

        lock (_gate)
        {
            if (_disposed || _handle is null)
                return new ScsiResult(0, null, 0, new TransportError.OpenFailed(_driveLetter, 6)); // ERROR_INVALID_HANDLE

            try
            {
                return ExecuteCore(cdb, data, direction, timeout);
            }
            catch (Exception ex)
            {
                // Contract: no exceptions towards callers.
                _log.Error(ex, "SCSI: unexpected failure executing command");
                return new ScsiResult(0, null, 0, new TransportError.WinError(Marshal.GetLastPInvokeError()));
            }
        }
    }

    private ScsiResult ExecuteCore(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout)
    {
        if (cdb.Length is not (6 or 10 or 12 or 16))
            return new ScsiResult(0, null, 0, new TransportError.WinError(ErrorInvalidParameter));

        var applied = ClampTimeout(ResolveTimeout(timeout, _defaultTimeout));
        var dataLength = direction == DataDirection.None ? 0 : data.Length;

        byte* buffer = null;
        if (dataLength > 0)
        {
            EnsureDataBuffer((nuint)dataLength);
            buffer = _dataBuffer;
            if (direction == DataDirection.Out)
                data[..dataLength].CopyTo(new Span<byte>(buffer, dataLength));
        }

        var block = _block;
        NativeMemory.Clear(block, (nuint)sizeof(SptdWithBuffers));
        var s = &block->Sptd;
        s->Length = (ushort)sizeof(ScsiPassThroughDirect);
        s->CdbLength = (byte)cdb.Length;
        s->SenseInfoLength = SenseLength;
        s->DataIn = direction switch
        {
            DataDirection.In => ScsiIoctlDataIn,
            DataDirection.Out => ScsiIoctlDataOut,
            _ => ScsiIoctlDataUnspecified,
        };
        s->DataTransferLength = (uint)dataLength;
        s->TimeOutValue = (uint)Math.Ceiling(applied.TotalSeconds);
        s->DataBuffer = buffer;
        s->SenseInfoOffset = (uint)((byte*)block->Sense - (byte*)block);
        cdb.CopyTo(new Span<byte>(s->Cdb, 16));

        var size = (uint)sizeof(SptdWithBuffers);
        var ok = DeviceIoControl(_handle!, IoctlScsiPassThroughDirect, block, size, block, size, out _, 0);
        var win32 = ok ? 0 : Marshal.GetLastPInvokeError();

        ScsiResult result;
        if (!ok)
        {
            TransportError error = win32 == ErrorSemTimeout
                ? new TransportError.Timeout(Convert.ToHexString(cdb), applied)
                : new TransportError.WinError(win32);
            result = new ScsiResult(0, null, 0, error);
        }
        else
        {
            var transferred = (int)Math.Clamp(s->DataTransferLength, 0u, (uint)dataLength);
            if (direction == DataDirection.In && transferred > 0)
                new ReadOnlySpan<byte>(buffer, transferred).CopyTo(data);

            var sense = ParseSense(new ReadOnlySpan<byte>(block->Sense, SenseLength), s->SenseInfoLength);
            result = new ScsiResult(s->ScsiStatus, sense, transferred, null);
        }

        if (_log.IsEnabled(LogEventLevel.Verbose))
            LogVerbose(cdb, result, data, direction);

        return result;
    }

    private void LogVerbose(ReadOnlySpan<byte> cdb, ScsiResult r, ReadOnlySpan<byte> data, DataDirection direction)
    {
        var shown = direction == DataDirection.In ? Math.Min(Math.Min(r.BytesTransferred, data.Length), 64) : 0;
        _log.Verbose("SCSI {Drive}: CDB={Cdb} Status=0x{Status:X2} Sense={Sense} Error={Error} Data[{Count}]={Data}",
            _driveLetter, Convert.ToHexString(cdb), r.ScsiStatus,
            r.Sense is { } se ? $"{se.Key:X}/{se.Asc:X2}/{se.Ascq:X2}" : "none",
            r.Error?.ToString() ?? "none", shown, Convert.ToHexString(data[..shown]));
    }

    /// <summary>
    /// Parses fixed (0x70/0x71) or descriptor (0x72/0x73) format sense data. Returns null if the drive
    /// reported no sense.
    /// </summary>
    internal static SenseData? ParseSense(ReadOnlySpan<byte> sense, int reportedLength)
    {
        if (reportedLength <= 0 || sense.Length < 4)
            return null;

        switch (sense[0] & 0x7F)
        {
            case 0x70 or 0x71 when sense.Length >= 14:
                return new SenseData((byte)(sense[2] & 0x0F), sense[12], sense[13]);
            case 0x72 or 0x73:
                return new SenseData((byte)(sense[1] & 0x0F), sense[2], sense[3]);
            default:
                return null;
        }
    }

    /// <summary>Substitutes the configured timeout when the caller asked for the default 30 s.</summary>
    internal static TimeSpan ResolveTimeout(TimeSpan requested, Func<TimeSpan>? configuredDefault) =>
        requested == DefaultTimeout && configuredDefault is not null ? configuredDefault() : requested;

    internal static TimeSpan ClampTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return DefaultTimeout;
        return timeout < MinTimeout ? MinTimeout : timeout > MaxTimeout ? MaxTimeout : timeout;
    }

    /// <summary>Reuses one aligned native buffer, growing it only when a larger transfer is requested.</summary>
    private void EnsureDataBuffer(nuint length)
    {
        if (_dataBuffer is not null && _dataCapacity >= length) return;
        if (_dataBuffer is not null) NativeMemory.AlignedFree(_dataBuffer);
        _dataBuffer = null;
        _dataCapacity = 0;
        _dataBuffer = (byte*)NativeMemory.AlignedAlloc(length, _alignment);
        _dataCapacity = length;
    }

    private nuint QueryAlignment(SafeFileHandle handle)
    {
        IoScsiCapabilities caps = default;
        var ok = DeviceIoControl(handle, IoctlScsiGetCapabilities, null, 0, &caps,
            (uint)sizeof(IoScsiCapabilities), out _, 0);
        if (!ok)
        {
            _log.Warning("SCSI: IOCTL_SCSI_GET_CAPABILITIES failed on {Drive} (Win32 {Code}); using 16-byte alignment",
                _driveLetter, Marshal.GetLastPInvokeError());
            return 16;
        }

        // AlignmentMask is (alignment - 1); round up to a power of two as required by AlignedAlloc.
        var alignment = (nuint)caps.AlignmentMask + 1;
        return nuint.Max(16, (nuint)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)alignment));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_dataBuffer is not null) NativeMemory.AlignedFree(_dataBuffer);
            if (_block is not null) NativeMemory.AlignedFree(_block);
            _dataBuffer = null;
            _block = null;
            _handle?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScsiPassThroughDirect
    {
        public ushort Length;
        public byte ScsiStatus;
        public byte PathId;
        public byte TargetId;
        public byte Lun;
        public byte CdbLength;
        public byte SenseInfoLength;
        public byte DataIn;
        public uint DataTransferLength;
        public uint TimeOutValue;
        public void* DataBuffer;
        public uint SenseInfoOffset;
        public fixed byte Cdb[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SptdWithBuffers
    {
        public ScsiPassThroughDirect Sptd;
        public uint Filler;
        public fixed byte Sense[SenseLength];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoScsiCapabilities
    {
        public uint Length;
        public uint MaximumTransferLength;
        public uint MaximumPhysicalPages;
        public uint SupportedAsynchronousEvents;
        public uint AlignmentMask;
        public byte TaggedQueuing;
        public byte AdapterScansDown;
        public byte AdapterUsesPio;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share,
        nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* inBuffer, uint inSize,
        void* outBuffer, uint outSize, out uint bytesReturned, nint overlapped);
}
