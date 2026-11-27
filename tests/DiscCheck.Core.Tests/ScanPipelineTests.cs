using System.Runtime.CompilerServices;
using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

public class ScanPipelineTests
{
    private static readonly Toc Toc = new([new TocEntry(1, 0, true)], 75 * 5000);
    private static readonly ScanOptions Options = new('E', 1408, LiteOnMethod.New, Toc);

    private sealed class FakeScanner(int count, Exception? failAfter = null, bool waitForCancel = false) : IQualityScanner
    {
        public int ScanThreadId;
        public volatile bool Closed;
        public int Produced;
        public string Name => "Fake";
        public ScannerCapabilities Probe(DriveInfoEx drive) => new(true, true, true, true, null);

        public async IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions o, IProgress<ScanProgress> p,
            [EnumeratorCancellation] CancellationToken ct)
        {
            ScanThreadId = Environment.CurrentManagedThreadId;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    Produced++;
                    yield return new ScanSample(i * 75, i, null, i, 0, 8.0);
                }
                if (failAfter is not null) throw failAfter;
                if (waitForCancel) await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                Closed = true; // end-of-scan command closing the Test_Mode
            }
        }
    }

    private sealed class SyncProgress(List<ScanProgress> sink) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) { lock (sink) sink.Add(value); }
    }

    [Fact]
    public async Task SlowConsumer_ReceivesAllSamplesInOrder_AcrossBothConsumers()
    {
        var scanner = new FakeScanner(3000);
        var seen = new List<ScanSample>();
        var reports = new List<ScanProgress>();

        var result = await new ScanPipeline().RunAsync(scanner, Options, new SyncProgress(reports),
            async s => { if (s.TimeSeconds % 500 == 0) await Task.Delay(20); seen.Add(s); }, CancellationToken.None);

        result.Status.Should().Be(ScanRunStatus.Completed);
        result.Samples.Select(s => s.TimeSeconds).Should().Equal(Enumerable.Range(0, 3000));
        seen.Select(s => s.TimeSeconds).Should().Equal(Enumerable.Range(0, 3000));
        reports.Should().HaveCount(3000);
        reports.Should().OnlyContain(r => r.Percent >= 0 && r.Percent <= 100);
        scanner.Closed.Should().BeTrue();
        scanner.ScanThreadId.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task ScanFailure_DeliversPublishedSamplesThenReportsError()
    {
        var error = new ScanFailedException("Lettura non riuscita", 123);
        var scanner = new FakeScanner(10, error);

        var result = await new ScanPipeline().RunAsync(scanner, Options, null, null, CancellationToken.None);

        result.Status.Should().Be(ScanRunStatus.Failed);
        result.Error.Should().BeSameAs(error);
        result.Samples.Should().HaveCount(10);
        scanner.Closed.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_StopsScanAndClosesTestMode()
    {
        var scanner = new FakeScanner(10, waitForCancel: true);
        using var cts = new CancellationTokenSource();

        var run = new ScanPipeline().RunAsync(scanner, Options, null, s =>
        {
            if (s.TimeSeconds == 5) cts.Cancel();
            return ValueTask.CompletedTask;
        }, cts.Token);

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ScanRunStatus.Cancelled);
        scanner.Closed.Should().BeTrue();
    }

    [Fact]
    public async Task ConsumerFault_DoesNotHang_AndIsReported()
    {
        var scanner = new FakeScanner(5000);
        var result = await new ScanPipeline().RunAsync(scanner, Options, null,
            s => s.TimeSeconds == 3 ? throw new InvalidOperationException("ui") : ValueTask.CompletedTask,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        result.Status.Should().Be(ScanRunStatus.Failed);
        result.Error.Should().BeOfType<InvalidOperationException>();
        scanner.Closed.Should().BeTrue();
    }
}
