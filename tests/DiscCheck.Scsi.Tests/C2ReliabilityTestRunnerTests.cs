using System.Buffers.Binary;
using DiscCheck.Core;
using DiscCheck.Scsi.Drives;
using DiscCheck.Verify;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.Scsi.Tests;

/// <summary>C2ReliabilityTestRunner with a synthetic transport (Requirements 13.3, 13.4, 13.6, 13.7, 13.8).</summary>
public class C2ReliabilityTestRunnerTests
{
    private const char Letter = 'D';
    private const int Count = C2ReliabilityAnalyzer.SectorsToRead;
    private const int Stride = C2ReliabilityAnalyzer.SectorStride;

    private static readonly Toc DiscToc = new([new TocEntry(1, 0, true)], 40000);
    private static readonly DriveRecord C2Drive = new(1, "LITE-ON", "iHAS124", "WL0L", C2Supported: true, C2Reliable: true);

    /// <summary>
    /// Serves READ CD. A pass starts with each C2 read of LBA 0. <see cref="Differing"/> sectors get different user data in
    /// pass 1; <see cref="Flagged"/> sectors report a C2 bit in pass 1.
    /// </summary>
    private sealed class FakeTransport : IScsiTransport
    {
        private int _pass = -1;
        public HashSet<int> Differing { get; } = [];
        public HashSet<int> Flagged { get; } = [];
        public HashSet<int> FailingLbas { get; } = [];
        public Action<int>? OnRead { get; set; }
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }

        public ScsiResult Execute(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout)
        {
            Reads++;
            int lba = BinaryPrimitives.ReadInt32BigEndian(cdb.Slice(2, 4));
            int count = (cdb[6] << 16) | (cdb[7] << 8) | cdb[8];
            bool withC2 = (cdb[9] & MmcCommands.ReadCdC2Pointers) != 0;
            OnRead?.Invoke(lba);
            if (FailingLbas.Contains(lba))
                return new ScsiResult(ScsiResult.StatusCheckCondition, new SenseData(0x03, 0x11, 0), 0, null);

            if (withC2 && lba == 0) _pass++;
            int stride = withC2 ? Stride : 2352;
            data.Clear();
            for (int s = 0; s < count; s++)
            {
                if (!withC2) continue;
                int sector = lba + s;
                if (_pass == 1 && Differing.Contains(sector)) data[s * stride + 100] = 0x55;
                if (_pass == 1 && Flagged.Contains(sector)) data[s * stride + 2352] = 0x01;
            }

            return new ScsiResult(ScsiResult.StatusGood, null, count * stride, null);
        }

        public void Dispose() => Disposed = true;
    }

    private static (C2ReliabilityTestRunner Runner, IDriveRepository Repo, IScsiTransportFactory Factory) Create(FakeTransport t)
    {
        var factory = Substitute.For<IScsiTransportFactory>();
        factory.Open(Letter).Returns(t);
        var repo = Substitute.For<IDriveRepository>();
        return (new C2ReliabilityTestRunner(factory, repo, Serilog.Core.Logger.None), repo, factory);
    }

    [Fact]
    public void Run_AllDifferingSectorsFlagged_IsReliableAndSaved()
    {
        var t = new FakeTransport();
        foreach (var s in new[] { 5, 3000, 7499 }) { t.Differing.Add(s); t.Flagged.Add(s); }
        var (runner, repo, _) = Create(t);

        var r = runner.Run(Letter, C2Drive with { C2Reliable = null }, DiscToc, null, CancellationToken.None);

        r.Status.Should().Be(C2TestStatus.Completed);
        r.Outcome!.Kind.Should().Be(C2ReliabilityKind.Reliable);
        r.Saved.Should().BeTrue();
        r.CacheDefeatLimited.Should().BeFalse();
        repo.Received(1).AddIfMissing(Arg.Is<DriveRecord>(d => d.C2Reliable == true));
        t.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Run_UncoveredDifferingSectors_IsUnreliableWithCountAndSavedFalse()
    {
        var t = new FakeTransport();
        foreach (var s in new[] { 1, 2, 3, 4 }) t.Differing.Add(s);
        t.Flagged.Add(1);
        var (runner, repo, _) = Create(t);

        var r = runner.Run(Letter, C2Drive, DiscToc, null, CancellationToken.None);

        r.Outcome!.Kind.Should().Be(C2ReliabilityKind.Unreliable);
        r.Outcome.UncoveredSectors.Should().Be(3);
        r.Message.Should().Contain("3");
        r.Saved.Should().BeTrue();
        repo.Received(1).AddIfMissing(Arg.Is<DriveRecord>(d => d.C2Reliable == false));
    }

    [Fact]
    public void Run_IdenticalReadings_IsInconclusiveAndDoesNotTouchC2Reliable()
    {
        var (runner, repo, _) = Create(new FakeTransport());

        var r = runner.Run(Letter, C2Drive, DiscToc, null, CancellationToken.None);

        r.Status.Should().Be(C2TestStatus.Completed);
        r.Outcome!.Kind.Should().Be(C2ReliabilityKind.Inconclusive);
        r.Message.Should().Be(C2ReliabilityTestRunner.InconclusiveMessage);
        r.Saved.Should().BeFalse();
        repo.DidNotReceiveWithAnyArgs().AddIfMissing(default!);
    }

    [Fact]
    public void Run_ReadError_ReturnsErrorWithoutSaving()
    {
        var t = new FakeTransport();
        t.FailingLbas.Add(26 * 10);
        var (runner, repo, _) = Create(t);

        var r = runner.Run(Letter, C2Drive, DiscToc, null, CancellationToken.None);

        r.Status.Should().Be(C2TestStatus.Error);
        r.Message.Should().Contain("non è stata modificata");
        r.Saved.Should().BeFalse();
        repo.DidNotReceiveWithAnyArgs().AddIfMissing(default!);
    }

    [Fact]
    public void Run_CancelledMidReading_ReturnsCancelledWithoutSaving()
    {
        var t = new FakeTransport();
        using var cts = new CancellationTokenSource();
        t.OnRead = lba => { if (lba >= 26 * 5) cts.Cancel(); };
        t.Differing.Add(1); t.Flagged.Add(1);
        var (runner, repo, _) = Create(t);

        var r = runner.Run(Letter, C2Drive, DiscToc, null, cts.Token);

        r.Status.Should().Be(C2TestStatus.Cancelled);
        r.Saved.Should().BeFalse();
        repo.DidNotReceiveWithAnyArgs().AddIfMissing(default!);
    }

    [Fact]
    public void Run_DriveWithoutC2_IsRejectedWithoutOpeningTransportOrReading()
    {
        var t = new FakeTransport();
        var (runner, repo, factory) = Create(t);

        foreach (var drive in new[] { C2Drive with { C2Supported = false }, C2Drive with { C2Supported = null } })
        {
            var r = runner.Run(Letter, drive, DiscToc, null, CancellationToken.None);

            r.Status.Should().Be(C2TestStatus.Unsupported);
            r.Message.Should().Be(C2ReliabilityTestRunner.UnsupportedMessage);
        }

        t.Reads.Should().Be(0);
        factory.DidNotReceiveWithAnyArgs().Open(default);
        repo.DidNotReceiveWithAnyArgs().AddIfMissing(default!);
    }

    [Fact]
    public void Run_DiscTooShort_ReturnsErrorWithoutReading()
    {
        var t = new FakeTransport();
        var (runner, repo, _) = Create(t);
        var shortToc = new Toc([new TocEntry(1, 0, true)], Count - 1);

        var r = runner.Run(Letter, C2Drive, shortToc, null, CancellationToken.None);

        r.Status.Should().Be(C2TestStatus.Error);
        t.Reads.Should().Be(0);
        repo.DidNotReceiveWithAnyArgs().AddIfMissing(default!);
    }
}
