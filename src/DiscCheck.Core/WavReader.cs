using System.Buffers.Binary;

namespace DiscCheck.Core;

/// <summary>Reads PCM WAV files (RIFF, PCM or WAVE_FORMAT_EXTENSIBLE with PCM subformat).</summary>
public static class WavReader
{
    /// <summary>Opens <paramref name="path"/> and validates the 16 bit / 44.1 kHz stereo format.</summary>
    /// <exception cref="SourceFileException">See Requirement 14.16.</exception>
    public static PcmStream Open(string path)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new SourceFileException(path, SourceFileFailure.Unreadable, inner: e);
        }

        try
        {
            return Parse(path, file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static PcmStream Parse(string path, FileStream file)
    {
        try
        {
            Span<byte> head = stackalloc byte[12];
            if (file.Read(head) < 12 || !head[..4].SequenceEqual("RIFF"u8) || !head[8..12].SequenceEqual("WAVE"u8))
            {
                throw new SourceFileException(path, SourceFileFailure.NotDecodable, "intestazione RIFF/WAVE assente");
            }

            PcmFormat? format = null;
            Span<byte> chunk = stackalloc byte[8];
            while (file.Read(chunk) == 8)
            {
                long size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                if (chunk[..4].SequenceEqual("fmt "u8))
                {
                    format = ReadFormat(path, file, size);
                    SourceFileException.RequireCdAudio(path, format.Value);
                    SkipPad(file, size);
                }
                else if (chunk[..4].SequenceEqual("data"u8))
                {
                    if (format is null)
                    {
                        throw new SourceFileException(path, SourceFileFailure.NotDecodable, "chunk fmt assente prima di data");
                    }

                    long available = file.Length - file.Position;
                    long length = Math.Min(size, available); // tolerate truncated or streamed (size 0xFFFFFFFF) files
                    return new WavPcmStream(file, format.Value, length / 4);
                }
                else
                {
                    file.Seek(size + (size & 1), SeekOrigin.Current);
                }
            }

            throw new SourceFileException(path, SourceFileFailure.NotDecodable, "chunk data assente");
        }
        catch (IOException e)
        {
            throw new SourceFileException(path, SourceFileFailure.Unreadable, inner: e);
        }
    }

    private static PcmFormat ReadFormat(string path, Stream s, long size)
    {
        if (size < 16)
        {
            throw new SourceFileException(path, SourceFileFailure.NotDecodable, "chunk fmt troncato");
        }

        Span<byte> f = stackalloc byte[16];
        s.ReadExactly(f);
        int tag = BinaryPrimitives.ReadUInt16LittleEndian(f);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(f[2..]);
        int rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(f[4..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(f[14..]);

        long consumed = 16;
        if (tag == 0xFFFE && size >= 40)
        {
            Span<byte> ext = stackalloc byte[24];
            s.ReadExactly(ext);
            consumed += 24;
            tag = BinaryPrimitives.ReadUInt16LittleEndian(ext[8..]); // first 2 bytes of the SubFormat GUID
        }

        s.Seek(size - consumed, SeekOrigin.Current);
        if (tag != 1)
        {
            throw new SourceFileException(path, SourceFileFailure.UnsupportedFormat, $"codifica WAV {tag} non PCM");
        }

        return new PcmFormat(rate, channels, bits);
    }

    private static void SkipPad(Stream s, long size)
    {
        if ((size & 1) != 0)
        {
            s.Seek(1, SeekOrigin.Current);
        }
    }

    private sealed class WavPcmStream(FileStream file, PcmFormat format, long totalFrames) : PcmStream
    {
        private long _remaining = totalFrames;
        private readonly long _total = totalFrames;

        public override PcmFormat Format => format;
        public override long TotalFrames => _total;

        public override int Read(Span<short> destination)
        {
            int frames = (int)Math.Min(destination.Length / 2, _remaining);
            if (frames == 0)
            {
                return 0;
            }

            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(destination[..(frames * 2)]);
            int got = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            int gotFrames = got / 4;
            if (!BitConverter.IsLittleEndian)
            {
                foreach (ref short v in destination[..(gotFrames * 2)])
                {
                    v = BinaryPrimitives.ReverseEndianness(v);
                }
            }

            _remaining = gotFrames < frames ? 0 : _remaining - gotFrames;
            return gotFrames;
        }

        public override void Dispose() => file.Dispose();
    }
}
