using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
using DiscCheck.Codecs.Flake;
using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

public sealed class SourceDecodingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("disccheck-src-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static short[] Pcm(int frames)
    {
        var rnd = new Random(42);
        var s = new short[frames * 2];
        for (int i = 0; i < s.Length; i++) s[i] = (short)rnd.Next(short.MinValue, short.MaxValue + 1);
        return s;
    }

    private string WriteWav(string name, short[] samples, int rate = 44100, int channels = 2, int bits = 16, int tag = 1)
    {
        string path = Path.Combine(_dir, name);
        using var w = new BinaryWriter(File.Create(path));
        int dataLen = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)tag); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        w.Write("data"u8); w.Write(dataLen);
        foreach (short v in samples) w.Write(v);
        return path;
    }

    private string WriteFlac(string name, short[] samples, int rate = 44100, int channels = 2, int bits = 16)
    {
        string path = Path.Combine(_dir, name);
        var pcm = new AudioPCMConfig(bits, channels, rate);
        int frames = samples.Length / channels;
        using (var fs = File.Create(path))
        {
            var writer = new FlakeWriter(path, fs, pcm) { CompressionLevel = 5, FinalSampleCount = frames };
            var buf = new AudioBuffer(pcm, frames);
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < channels; c++) buf.Samples[i, c] = samples[i * channels + c];
            buf.Length = frames;
            writer.Write(buf);
            writer.Close();
        }
        return path;
    }

    private static short[] ReadAll(PcmStream s)
    {
        var all = new List<short>();
        var chunk = new short[1000]; // 500 frames, not a divisor of the test lengths
        int n;
        while ((n = s.Read(chunk)) > 0) all.AddRange(chunk.AsSpan(0, n * 2).ToArray());
        return all.ToArray();
    }

    [Fact]
    public void Wav_decodes_samples_unchanged()
    {
        var pcm = Pcm(1234);
        using var s = WavReader.Open(WriteWav("a.wav", pcm));
        s.Format.Should().Be(PcmFormat.CdAudio);
        s.TotalFrames.Should().Be(1234);
        ReadAll(s).Should().Equal(pcm);
    }

    [Fact]
    public void Flac_decodes_samples_unchanged()
    {
        var pcm = Pcm(10_000);
        using var s = new FlakeDecoder().Decode(WriteFlac("a.flac", pcm));
        s.Format.Should().Be(PcmFormat.CdAudio);
        s.TotalFrames.Should().Be(10_000);
        ReadAll(s).Should().Equal(pcm);
    }

    [Theory]
    [InlineData(48000, 2, 16)]
    [InlineData(44100, 1, 16)]
    [InlineData(44100, 2, 8)]
    public void Wav_with_other_format_is_unsupported(int rate, int channels, int bits)
    {
        string path = WriteWav("x.wav", Pcm(100), rate, channels, bits);
        var act = () => WavReader.Open(path);
        act.Should().Throw<SourceFileException>().Which.Reason.Should().Be(SourceFileFailure.UnsupportedFormat);
    }

    [Fact]
    public void Wav_with_float_encoding_is_unsupported()
    {
        string path = WriteWav("f.wav", Pcm(100), tag: 3);
        var act = () => WavReader.Open(path);
        act.Should().Throw<SourceFileException>().Which.Reason.Should().Be(SourceFileFailure.UnsupportedFormat);
    }

    [Fact]
    public void Flac_with_other_sample_rate_is_unsupported()
    {
        string path = WriteFlac("x.flac", Pcm(5000), rate: 48000);
        var act = () => new FlakeDecoder().Decode(path);
        act.Should().Throw<SourceFileException>().Which.Reason.Should().Be(SourceFileFailure.UnsupportedFormat);
    }

    [Fact]
    public void Missing_file_is_unreadable_and_names_the_file()
    {
        string path = Path.Combine(_dir, "missing.flac");
        foreach (var act in new Action[] { () => WavReader.Open(path), () => new FlakeDecoder().Decode(path) })
        {
            var ex = act.Should().Throw<SourceFileException>().Which;
            ex.Reason.Should().Be(SourceFileFailure.Unreadable);
            ex.Path.Should().Be(path);
            ex.Message.Should().Contain(path).And.Contain("non leggibile");
        }
    }

    [Fact]
    public void Garbage_is_not_decodable()
    {
        string path = Path.Combine(_dir, "junk.bin");
        File.WriteAllBytes(path, new byte[2048]);
        new Action(() => WavReader.Open(path)).Should().Throw<SourceFileException>()
            .Which.Reason.Should().Be(SourceFileFailure.NotDecodable);
        new Action(() => new FlakeDecoder().Decode(path)).Should().Throw<SourceFileException>()
            .Which.Reason.Should().Be(SourceFileFailure.NotDecodable);
    }
}
