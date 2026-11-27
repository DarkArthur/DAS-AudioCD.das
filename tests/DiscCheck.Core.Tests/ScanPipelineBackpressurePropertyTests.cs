using System.Runtime.CompilerServices;
using DiscCheck.Core;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
namespace DiscCheck.Core.Tests;

public class ScanPipelineBackpressurePropertyTests
{
    private static readonly Toc Toc = new([new TocEntry(1, 0, true)], 75 * 5000);
    private static readonly ScanOptions Options = new('E', 1408, LiteOnMethod.New, Toc);

    /// <summary>Fast producer that counts how many samples it has published.</summary>
    private sealed class CountingScanner(int count) : IQualityScanner
    {
        private int _produced;
        public int Produced => Volatile.Read(ref _produced);
        public string Name => "Counting";
        public ScannerCapabilities Probe(DriveInfoEx drive) => new(true, true, true, true, null);
        public async IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions o, IProgress<ScanProgress> p,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            for (int i = 0; i < count; i++)
            {
                Interlocked.Increment(ref _produced);
                yield return new ScanSample(i * 75, i, null, i, 0, 8.0);
            }
        }
    }

    // Feature: disccheck, Property 13: Backpressure senza scarto
    // **Validates: Requirements 9.4, 9.9**
    [Property(MaxTest = 100)]
    public bool SlowConsumerNeverCausesDiscardAndProducerWaits(NonNegativeInt extra)
    {
        // Far more than the three bounded channels can hold (3 x 1.024), so the producer must block.
        int count = 3 * ScanPipeline.ChannelCapacity + 500 + extra.Get % 1500;
        var scanner = new CountingScanner(count);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<ScanSample>();

        var run = Task.Run(() => new ScanPipeline().RunAsync(scanner, Options, null,
            async s =>
            {
                if (seen.Count == 0) await gate.Task.ConfigureAwait(false); // consumer stalls on the first sample
                seen.Add(s);
            }, CancellationToken.None));

        // Wait until the producer stops advancing: it must be blocked by a full channel, not finished.
        int last = -1, stableFor = 0;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (stableFor < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            int now = scanner.Produced;
            stableFor = now == last ? stableFor + 1 : 0;
            last = now;
        }
        bool producerWaited = last >= ScanPipeline.ChannelCapacity && last < count;

        gate.SetResult();
        var result = run.GetAwaiter().GetResult();

        var expected = Enumerable.Range(0, count).ToArray();
        return producerWaited
            && result.Status == ScanRunStatus.Completed
            && scanner.Produced == count
            && result.Samples.Select(s => s.TimeSeconds).SequenceEqual(expected)
            && seen.Select(s => s.TimeSeconds).SequenceEqual(expected);
    }
}
