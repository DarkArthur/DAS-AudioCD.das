using System.Buffers.Binary;
using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using DiscCheck.Verify;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.Scsi.Tests;

/// <summary>
/// Verifier, AudioExtractor and SourceReader with a synthetic disc transport and synthetic PCM
/// (Requirements 14.3, 14.4, 14.11, 14.12, 14.16-14.18).
/// </summary>
public sealed class VerifierTests : IDisposable
{
    private const char Letter = 'D';
    private const int Frames = AudioExtractor.FramesPerSector;
    private const int Track1Sectors = 30;
    private const int Track2Sectors = 30;
    private const int Stride = 2352 + 294; // user data + C2 pointers

    private readonly string _dir = Directory.CreateTempSubdirectory("disccheck-verify-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ---------- synthetic disc ----------

    private static readonly Toc TwoTrackToc =
        new([new TocEntry(1, 0, true), new TocEntry(2, Track1Sectors, true)], Track1Sectors + Track2Sectors);

    private static readonly DriveRecord C2Drive = new(1, "LITE-ON", "iHAS124", "WL0L", C2Supported: true, C2Reliable: true);

    private static short[] SyntheticPcm(int sectors, int seed)
    {
        var rnd = new Random(seed);
        var pcm = new short[sectors * Frames * 2];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)rnd.Next(short.MinValue, short.MaxValue + 1);
        return pcm;
    }

    /// <summary>Serves READ CD (0xBE) from a PCM array indexed by LBA, with scriptable C2 and failures.</summary>
    private sealed class SyntheticDiscTransport(short[] disc) : IScsiTransport
    {
        private readonly Dictionary<int, int> _reads = new();

        /// <summary>LBAs (block starts) whose reads report C2: the value is how many reads report it (int.MaxValue = always).</summary>
        public Dictionary<int, int> C2Reads { get; } = new();

        public HashSet<int> FailingLbas { get; } = [];
        public Action<int>? OnRead { get; set; }
        public int ReadCount(int lba) => _reads.GetValueOrDefault(lba);

        public ScsiResult Execute(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout)
        {
            if (cdb[0] != 0xBE)
                return new ScsiResult(ScsiResult.StatusCheckCondition, new SenseData(0x05, 0x20, 0), 0, null);

            int lba = BinaryPrimitives.ReadInt32BigEndian(cdb.Slice(2, 4));
            int count = (cdb[6] << 16) | (cdb[7] << 8) | cdb[8];
            bool withC2 = (cdb[9] & MmcCommands.ReadCdC2Pointers) != 0;
            int stride = withC2 ? Stride : 2352;

            _reads[lba] = _reads.GetValueOrDefault(lba) + 1;
            OnRead?.Invoke(lba);

            if (FailingLbas.Contains(lba) || (lba + count) * Frames * 2 > disc.Length)
                return new ScsiResult(ScsiResult.StatusCheckCondition, new SenseData(0x03, 0x11, 0), 0, null);

            data.Clear();
            for (int s = 0; s < count; s++)
            {
                var src = System.Runtime.InteropServices.MemoryMarshal.AsBytes(disc.AsSpan((lba + s) * Frames * 2, Frames * 2));
                src.CopyTo(data[(s * stride)..]);
            }

            if (withC2 && C2Reads.TryGetValue(lba, out int left) && left > 0)
            {
                data[2352] = 0x01;
                if (left != int.MaxValue) C2Reads[lba] = left - 1;
            }

            return new ScsiResult(ScsiResult.StatusGood, null, count * stride, null);
        }

        public void Dispose() { }
    }

    private sealed class FakeFlacDecoder(PcmFormat format, short[] pcm) : IFlacDecoder
    {
        public PcmStream Decode(string path) => new MemoryPcmStream(format, pcm);
    }

    private sealed class MemoryPcmStream(PcmFormat format, short[] pcm) : PcmStream
    {
        private int _pos;
        public override PcmFormat Format => format;
        public override long TotalFrames => pcm.Length / 2;

        public override int Read(Span<short> destination)
        {
            int n = Math.Min(destination.Length, pcm.Length - _pos) / 2 * 2;
            pcm.AsSpan(_pos, n).CopyTo(destination);
            _pos += n;
            return n / 2;
        }
    }

    private static void WriteWav(string path, short[] pcm, int sampleRate = 44100, int channels = 2)
    {
        int dataBytes = pcm.Length * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in pcm) w.Write(s);
    }

    /// <summary>Disc PCM and a Sorgente folder with one WAV per track; <paramref name="mutate"/> alters the source copy of a track.</summary>
    private (short[] Disc, string Source) MakeDiscAndSource()
    {
        var disc = SyntheticPcm(Track1Sectors + Track2Sectors, 7);
        string source = Path.Combine(_dir, "source");
        Directory.CreateDirectory(source);
        int t1 = Track1Sectors * Frames * 2;
        WriteWav(Path.Combine(source, "01.wav"), disc[..t1]);
        WriteWav(Path.Combine(source, "02.wav"), disc[t1..]);
        return (disc, source);
    }

    private static Verifier MakeVerifier(IScsiTransport transport, IFlacDecoder? flac = null)
    {
        var factory = Substitute.For<IScsiTransportFactory>();
        factory.Open(Letter).Returns(transport);
        return new Verifier(factory, new SourceReader(flac ?? Substitute.For<IFlacDecoder>()), Serilog.Core.Logger.None);
    }

    private static VerifyOptions Options(string source, int offset = 0, int rereads = 2, DriveRecord? drive = null, Toc? toc = null) =>
        new(Letter, drive ?? C2Drive, toc ?? TwoTrackToc, source, offset, rereads);

    // ---------- Verifier ----------

    [Fact]
    public void Run_MatchingSource_IsIdentical()
    {
        var (disc, source) = MakeDiscAndSource();
        var report = MakeVerifier(new SyntheticDiscTransport(disc)).Run(Options(source), null, CancellationToken.None);

        report.Status.Should().Be(VerifyStatus.Completed);
        report.IsDiscIdentical.Should().BeTrue();
        report.Tracks.Should().HaveCount(2).And.OnlyContain(t => t.Identical && t.CrcRead == t.CrcSource && t.Sha256Read == t.Sha256Source);
    }

    [Fact]
    public void Run_OneDifferentSample_IsDifferentAndReportsPosition()
    {
        var (disc, source) = MakeDiscAndSource();
        var drive = (short[])disc.Clone();
        int idx = (Track1Sectors + 3) * Frames * 2 + 10;
        drive[idx] ^= 0x4000;

        var report = MakeVerifier(new SyntheticDiscTransport(drive)).Run(Options(source), null, CancellationToken.None);

        report.Result.Should().Be(VerificationResult.Different);
        report.Tracks[0].Identical.Should().BeTrue();
        report.Tracks[1].Identical.Should().BeFalse();
        report.Tracks[1].DifferentSamples.Should().Be(1);
        report.Tracks[1].Sha256Read.Should().NotBe(report.Tracks[1].Sha256Source);
    }

    [Fact]
    public void Run_DifferentTrackCount_FailsPreCheckWithoutReadingTheDisc()
    {
        var (disc, source) = MakeDiscAndSource();
        File.Delete(Path.Combine(source, "02.wav"));
        var transport = new SyntheticDiscTransport(disc);

        var report = MakeVerifier(transport).Run(Options(source), null, CancellationToken.None);

        report.Status.Should().Be(VerifyStatus.PreCheckFailed);
        report.PreCheck.Should().Be(PreCheckFailure.TrackCountMismatch);
        report.Message.Should().Contain("2").And.Contain("1");
        report.Result.Should().BeNull();
        report.Tracks.Should().BeEmpty();
        transport.ReadCount(0).Should().Be(0);
    }

    [Fact]
    public void Run_WavWithInvalidFormat_FailsPreCheckNamingTheFile()
    {
        var (disc, source) = MakeDiscAndSource();
        WriteWav(Path.Combine(source, "02.wav"), new short[Track2Sectors * Frames], sampleRate: 48000, channels: 1);

        var report = MakeVerifier(new SyntheticDiscTransport(disc)).Run(Options(source), null, CancellationToken.None);

        report.Status.Should().Be(VerifyStatus.PreCheckFailed);
        report.PreCheck.Should().Be(PreCheckFailure.SourceFile);
        report.Message.Should().Contain("02.wav");
    }

    [Fact]
    public void Run_FlacWithInvalidFormat_FailsPreCheck()
    {
        var (disc, source) = MakeDiscAndSource();
        File.Delete(Path.Combine(source, "02.wav"));
        File.WriteAllBytes(Path.Combine(source, "02.flac"), [0]);
        var flac = new FakeFlacDecoder(new PcmFormat(44100, 2, 24), new short[Track2Sectors * Frames * 2]);

        var report = MakeVerifier(new SyntheticDiscTransport(disc), flac).Run(Options(source), null, CancellationToken.None);

        report.PreCheck.Should().Be(PreCheckFailure.SourceFile);
        report.Message.Should().Contain("02.flac").And.Contain("formato non supportato");
    }

    [Fact]
    public void Run_FlacWithValidFormat_IsVerifiedThroughTheDecoder()
    {
        var (disc, source) = MakeDiscAndSource();
        File.Delete(Path.Combine(source, "02.wav"));
        File.WriteAllBytes(Path.Combine(source, "02.flac"), [0]);
        var flac = new FakeFlacDecoder(PcmFormat.CdAudio, disc[(Track1Sectors * Frames * 2)..]);

        var report = MakeVerifier(new SyntheticDiscTransport(disc), flac).Run(Options(source), null, CancellationToken.None);

        report.IsDiscIdentical.Should().BeTrue();
    }

    [Fact]
    public void Run_IncompatibleDuration_FailsPreCheck()
    {
        var (disc, source) = MakeDiscAndSource();
        var longer = SyntheticPcm(Track1Sectors + 2, 9);
        WriteWav(Path.Combine(source, "01.wav"), longer);

        var report = MakeVerifier(new SyntheticDiscTransport(disc)).Run(Options(source), null, CancellationToken.None);

        report.Status.Should().Be(VerifyStatus.PreCheckFailed);
        report.PreCheck.Should().Be(PreCheckFailure.DurationMismatch);
        report.Message.Should().Contain("Traccia 1");
    }

    [Fact]
    public void Run_DurationDifferenceWithinOffset_PassesPreCheck()
    {
        var (disc, source) = MakeDiscAndSource();
        // 100 frames shorter than the disc track, offset 100 allows it.
        int t1 = Track1Sectors * Frames * 2;
        WriteWav(Path.Combine(source, "01.wav"), disc[..(t1 - 200)]);

        var report = MakeVerifier(new SyntheticDiscTransport(disc)).Run(Options(source, offset: 100), null, CancellationToken.None);

        report.Status.Should().Be(VerifyStatus.Completed);
    }

    [Fact]
    public void Run_ReadCdErrorOnSecondTrack_ReturnsErrorKeepingFirstTrack()
    {
        var (disc, source) = MakeDiscAndSource();
        var transport = new SyntheticDiscTransport(disc);
        transport.FailingLbas.Add(Track1Sectors);

        var report = MakeVerifier(transport).Run(Options(source), null, CancellationToken.None);

        report.Status.Should().Be(VerifyStatus.Error);
        report.Result.Should().Be(VerificationResult.Error);
        report.IsDiscIdentical.Should().BeFalse();
        report.Message.Should().Contain("traccia 2").And.Contain($"LBA {Track1Sectors}");
        report.Tracks.Should().ContainSingle().Which.TrackNo.Should().Be(1);
        transport.ReadCount(Track1Sectors).Should().Be(1, "a failed READ CD is not retried");
    }

    [Fact]
    public void Run_ResidualC2AfterNRereads_FlagsBlockAsUnreliable()
    {
        var (disc, source) = MakeDiscAndSource();
        var transport = new SyntheticDiscTransport(disc);
        transport.C2Reads[0] = int.MaxValue;

        var report = MakeVerifier(transport).Run(Options(source, rereads: 3), null, CancellationToken.None);

        transport.ReadCount(0).Should().Be(1 + 3);
        report.Tracks[0].UnreliableLbas.Should().Equal(0);
        report.Tracks[1].UnreliableLbas.Should().BeEmpty();
        report.ToTrackRecords()[0].UnreliableLbas.Should().Be("0");
        report.CacheDefeatLimited.Should().BeTrue("the tiny disc has no far area for Cache_Defeat");
    }

    [Fact]
    public void Run_C2ClearedByReread_BlockIsReliable()
    {
        var (disc, source) = MakeDiscAndSource();
        var transport = new SyntheticDiscTransport(disc);
        transport.C2Reads[26] = 1;

        var report = MakeVerifier(transport).Run(Options(source, rereads: 3), null, CancellationToken.None);

        transport.ReadCount(26).Should().Be(2);
        report.Tracks[0].UnreliableLbas.Should().BeEmpty();
    }

    [Fact]
    public void Run_UnreliableC2Drive_IgnoresC2AndNeverRereads()
    {
        var (disc, source) = MakeDiscAndSource();
        var transport = new SyntheticDiscTransport(disc);
        transport.C2Reads[0] = int.MaxValue;
        var drive = C2Drive with { C2Reliable = false };

        var report = MakeVerifier(transport).Run(Options(source, drive: drive), null, CancellationToken.None);

        transport.ReadCount(0).Should().Be(1);
        report.Tracks[0].UnreliableLbas.Should().BeEmpty();
        report.IsDiscIdentical.Should().BeTrue();
    }

    [Fact]
    public void Run_CancelledMidTrack_IsCancelledNeverIdentical()
    {
        var (disc, source) = MakeDiscAndSource();
        using var cts = new CancellationTokenSource();
        var transport = new SyntheticDiscTransport(disc) { OnRead = _ => cts.Cancel() };

        var report = MakeVerifier(transport).Run(Options(source), null, cts.Token);

        report.Status.Should().Be(VerifyStatus.Cancelled);
        report.Result.Should().BeNull();
        report.IsDiscIdentical.Should().BeFalse();
    }

    [Fact]
    public void Run_CancelledBeforeStart_ReadsNothing()
    {
        var (disc, source) = MakeDiscAndSource();
        var transport = new SyntheticDiscTransport(disc);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = MakeVerifier(transport).Run(Options(source), null, cts.Token);

        report.Status.Should().Be(VerifyStatus.Cancelled);
        transport.ReadCount(0).Should().Be(0);
    }

    [Fact]
    public void Run_OffsetOutOfRange_Throws()
    {
        var (disc, source) = MakeDiscAndSource();
        var verifier = MakeVerifier(new SyntheticDiscTransport(disc));

        var act = () => verifier.Run(Options(source, offset: 5001), null, CancellationToken.None);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------- AudioExtractor ----------

    [Fact]
    public void ExtractTrack_ReturnsUserDataAndReportsProgress()
    {
        var disc = SyntheticPcm(40, 3);
        var extractor = new AudioExtractor(new SyntheticDiscTransport(disc), Serilog.Core.Logger.None);
        var progress = new List<double>();

        var track = extractor.ExtractTrack(1, 5, 30, new ExtractOptions(true, 2, null, 40), progress.Add, CancellationToken.None);

        track.Pcm.Should().Equal(disc[(5 * Frames * 2)..(35 * Frames * 2)]);
        track.UnreliableLbas.Should().BeEmpty();
        progress.Should().BeInAscendingOrder().And.EndWith(1.0);
    }

    [Fact]
    public void ExtractTrack_ReadError_ThrowsReadCdExceptionWithTrackAndLba()
    {
        var transport = new SyntheticDiscTransport(SyntheticPcm(40, 3));
        transport.FailingLbas.Add(5);
        var extractor = new AudioExtractor(transport, Serilog.Core.Logger.None);

        var act = () => extractor.ExtractTrack(4, 5, 30, new ExtractOptions(true, 2, null, 40), null, CancellationToken.None);

        act.Should().Throw<ReadCdException>().Where(e => e.TrackNo == 4 && e.Lba == 5);
    }

    // ---------- SourceReader ----------

    [Fact]
    public void ResolveFiles_Folder_IsAlphabeticalAndIgnoresOtherFiles()
    {
        foreach (var n in new[] { "10.wav", "02.flac", "01.wav", "notes.txt" })
            File.WriteAllBytes(Path.Combine(_dir, n), [0]);

        var files = new SourceReader(Substitute.For<IFlacDecoder>()).ResolveFiles(_dir);

        files.Select(Path.GetFileName).Should().Equal("01.wav", "02.flac", "10.wav");
    }

    [Fact]
    public void ResolveFiles_Cue_FollowsFileOrderOfTheSheet()
    {
        string cue = Path.Combine(_dir, "album.cue");
        File.WriteAllLines(cue, ["FILE \"b track.wav\" WAVE", "  TRACK 01 AUDIO", "FILE a.flac WAVE", "  TRACK 02 AUDIO"]);

        var files = new SourceReader(Substitute.For<IFlacDecoder>()).ResolveFiles(cue);

        files.Select(Path.GetFileName).Should().Equal("b track.wav", "a.flac");
    }

    [Fact]
    public void ResolveFiles_MissingPath_ThrowsUnreadable()
    {
        var act = () => new SourceReader(Substitute.For<IFlacDecoder>()).ResolveFiles(Path.Combine(_dir, "nope"));

        act.Should().Throw<SourceFileException>().Where(e => e.Reason == SourceFileFailure.Unreadable);
    }

    [Fact]
    public void ReadAll_Wav_ReturnsTheSamples()
    {
        var pcm = SyntheticPcm(2, 5);
        string path = Path.Combine(_dir, "t.wav");
        WriteWav(path, pcm);

        new SourceReader(Substitute.For<IFlacDecoder>()).ReadAll(path).Should().Equal(pcm);
    }

    [Fact]
    public void Open_UnsupportedExtension_Throws()
    {
        string path = Path.Combine(_dir, "t.mp3");
        File.WriteAllBytes(path, [0]);

        var act = () => new SourceReader(Substitute.For<IFlacDecoder>()).Open(path);

        act.Should().Throw<SourceFileException>().Where(e => e.Reason == SourceFileFailure.UnsupportedFormat);
    }
}
