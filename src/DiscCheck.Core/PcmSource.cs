namespace DiscCheck.Core;

/// <summary>PCM format of a Sorgente file.</summary>
public readonly record struct PcmFormat(int SampleRate, int Channels, int BitsPerSample)
{
    /// <summary>Red Book format required by Requirements 14.1 and 14.16: 16 bit, 44.1 kHz, stereo.</summary>
    public static readonly PcmFormat CdAudio = new(44100, 2, 16);

    public bool IsCdAudio => this == CdAudio;
}

/// <summary>Why a Sorgente file was rejected (Requirement 14.16).</summary>
public enum SourceFileFailure { Unreadable, NotDecodable, UnsupportedFormat }

/// <summary>Raised when a Sorgente file cannot be used. Carries the file and the reason (Requirement 14.16).</summary>
public sealed class SourceFileException : Exception
{
    public SourceFileException(string path, SourceFileFailure reason, string? detail = null, Exception? inner = null)
        : base($"{path}: {Describe(reason)}{(string.IsNullOrEmpty(detail) ? "" : $" ({detail})")}", inner)
    {
        Path = path;
        Reason = reason;
    }

    public string Path { get; }
    public SourceFileFailure Reason { get; }

    public static string Describe(SourceFileFailure reason) => reason switch
    {
        SourceFileFailure.Unreadable => "non leggibile",
        SourceFileFailure.NotDecodable => "non decodificabile",
        _ => "formato non supportato",
    };

    /// <summary>Throws <see cref="SourceFileFailure.UnsupportedFormat"/> unless <paramref name="format"/> is 16 bit / 44.1 kHz stereo.</summary>
    public static void RequireCdAudio(string path, PcmFormat format)
    {
        if (!format.IsCdAudio)
        {
            throw new SourceFileException(path, SourceFileFailure.UnsupportedFormat,
                $"{format.BitsPerSample} bit, {format.SampleRate} Hz, {format.Channels} canali");
        }
    }
}

/// <summary>Sequential decoded PCM of one Sorgente file. Always 16 bit / 44.1 kHz stereo once opened by a reader.</summary>
public abstract class PcmStream : IDisposable
{
    public abstract PcmFormat Format { get; }

    /// <summary>Total stereo sample frames (588 per sector, Requirement 14.3).</summary>
    public abstract long TotalFrames { get; }

    /// <summary>Reads up to <c>destination.Length / 2</c> frames as interleaved L,R 16-bit samples. Returns the frames read, 0 at the end.</summary>
    public abstract int Read(Span<short> destination);

    public virtual void Dispose() { }
}

/// <summary>Decodes FLAC files into PCM. The only entry point to the D1 decoder (Requirement 25.9).</summary>
public interface IFlacDecoder
{
    /// <exception cref="SourceFileException">File unreadable, not decodable or not 16 bit / 44.1 kHz stereo.</exception>
    PcmStream Decode(string path);
}
