using DiscCheck.Core;

namespace DiscCheck.Verify;

/// <summary>
/// Maps disc tracks to Sorgente files and decodes them to PCM (Requirements 14.2, 14.5, 14.15, 14.16).
/// FLAC goes through <see cref="IFlacDecoder"/> only: this assembly has no reference to the decoder implementation (Req. 25.9).
/// </summary>
public sealed class SourceReader
{
    private static readonly string[] AudioExtensions = [".flac", ".wav"];

    private readonly IFlacDecoder _flac;

    public SourceReader(IFlacDecoder flac) => _flac = flac;

    /// <summary>
    /// Ordered Sorgente files: FILE order of a CUE sheet, or ascending alphabetical order of the file names
    /// (.flac and .wav) when <paramref name="sourcePath"/> is a folder (Requirement 14.2).
    /// </summary>
    /// <exception cref="SourceFileException">The folder or CUE file cannot be read.</exception>
    public IReadOnlyList<string> ResolveFiles(string sourcePath)
    {
        if (Directory.Exists(sourcePath))
        {
            try
            {
                return Directory.EnumerateFiles(sourcePath)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SourceFileException(sourcePath, SourceFileFailure.Unreadable, inner: e);
            }
        }

        if (File.Exists(sourcePath) && string.Equals(Path.GetExtension(sourcePath), ".cue", StringComparison.OrdinalIgnoreCase))
            return ParseCue(sourcePath);

        throw new SourceFileException(sourcePath, SourceFileFailure.Unreadable, "cartella o file CUE inesistente");
    }

    /// <summary>File names of the <c>FILE</c> commands of a CUE sheet in order of appearance, resolved against the CUE folder.</summary>
    public static IReadOnlyList<string> ParseCue(string cuePath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(cuePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new SourceFileException(cuePath, SourceFileFailure.Unreadable, inner: e);
        }

        string dir = Path.GetDirectoryName(Path.GetFullPath(cuePath)) ?? string.Empty;
        var files = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (!line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase) || line.Length < 6 || !char.IsWhiteSpace(line[4]))
                continue;

            var rest = line[5..].TrimStart();
            string name;
            if (rest.StartsWith('"'))
            {
                int end = rest.IndexOf('"', 1);
                if (end < 0) continue;
                name = rest[1..end];
            }
            else
            {
                int end = rest.IndexOfAny([' ', '\t']);
                name = end < 0 ? rest : rest[..end];
            }

            if (name.Length > 0) files.Add(Path.Combine(dir, name));
        }

        return files;
    }

    /// <summary>
    /// Opens a Sorgente file as PCM and checks it is 16 bit / 44.1 kHz stereo.
    /// </summary>
    /// <exception cref="SourceFileException">Unreadable, not decodable or unsupported format (Requirement 14.16).</exception>
    public PcmStream Open(string path)
    {
        if (!File.Exists(path))
            throw new SourceFileException(path, SourceFileFailure.Unreadable, "file inesistente");

        PcmStream stream = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".flac" => _flac.Decode(path),
            ".wav" => WavReader.Open(path),
            _ => throw new SourceFileException(path, SourceFileFailure.UnsupportedFormat, "estensione non supportata"),
        };

        try
        {
            SourceFileException.RequireCdAudio(path, stream.Format);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Decodes the whole file into interleaved L,R 16-bit samples.</summary>
    public short[] ReadAll(string path)
    {
        using var stream = Open(path);
        long total = stream.TotalFrames;
        if (total < 0 || total * 2 > Array.MaxLength)
            throw new SourceFileException(path, SourceFileFailure.NotDecodable, "durata non supportata");

        var pcm = new short[total * 2];
        int filled = 0;
        while (filled < pcm.Length)
        {
            int frames = stream.Read(pcm.AsSpan(filled));
            if (frames <= 0) break;
            filled += frames * 2;
        }

        if (filled < pcm.Length)
            Array.Resize(ref pcm, filled);
        return pcm;
    }
}
