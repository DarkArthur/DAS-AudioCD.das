using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
using DiscCheck.Codecs.Flake;
using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public sealed class DecoderRoundTripPropertyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("disccheck-rt-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Random 16-bit stereo PCM (interleaved), 1..4000 frames, full sample range.</summary>
    private static short[] Pcm(int seed, ushort frameSeed)
    {
        int frames = 1 + frameSeed % 4000;
        var rnd = new Random(seed);
        var s = new short[frames * 2];
        for (int i = 0; i < s.Length; i++) s[i] = (short)rnd.Next(short.MinValue, short.MaxValue + 1);
        return s;
    }

    private static short[] ReadAll(PcmStream s)
    {
        var all = new List<short>();
        var chunk = new short[1000];
        int n;
        while ((n = s.Read(chunk)) > 0) all.AddRange(chunk.AsSpan(0, n * 2).ToArray());
        return all.ToArray();
    }

    // Feature: disccheck, Property 9: Round-trip del decoder audio
    // **Validates: Requirements 14.14**
    [Property(MaxTest = 100)]
    public bool Wav_WriteThenRead_IsIdentity(int seed, ushort frameSeed)
    {
        var pcm = Pcm(seed, frameSeed);
        string path = Path.Combine(_dir, $"{Guid.NewGuid():N}.wav");
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int dataLen = pcm.Length * 2;
            w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(44100);
            w.Write(44100 * 4); w.Write((short)4); w.Write((short)16);
            w.Write("data"u8); w.Write(dataLen);
            foreach (short v in pcm) w.Write(v);
        }

        using var s = WavReader.Open(path);
        return s.TotalFrames == pcm.Length / 2 && ReadAll(s).SequenceEqual(pcm);
    }

    // Feature: disccheck, Property 9: Round-trip del decoder audio
    // **Validates: Requirements 14.14**
    [Property(MaxTest = 100)]
    public bool Flac_EncodeThenDecode_IsIdentity(int seed, ushort frameSeed)
    {
        var pcm = Pcm(seed, frameSeed);
        int frames = pcm.Length / 2;
        string path = Path.Combine(_dir, $"{Guid.NewGuid():N}.flac");
        var cfg = new AudioPCMConfig(16, 2, 44100);
        using (var fs = File.Create(path))
        {
            var writer = new FlakeWriter(path, fs, cfg) { CompressionLevel = 5, FinalSampleCount = frames };
            var buf = new AudioBuffer(cfg, frames);
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < 2; c++) buf.Samples[i, c] = pcm[i * 2 + c];
            buf.Length = frames;
            writer.Write(buf);
            writer.Close();
        }

        using var s = new FlakeDecoder().Decode(path);
        return s.TotalFrames == frames && ReadAll(s).SequenceEqual(pcm);
    }
}
