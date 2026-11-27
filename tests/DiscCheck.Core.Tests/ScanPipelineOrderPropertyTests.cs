using System.Runtime.CompilerServices;
using DiscCheck.Core;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class ScanPipelineOrderPropertyTests
{
    private static readonly Toc Toc = new([new TocEntry(1, 0, true)], 75 * 5000);
    private static readonly ScanOptions Options = new('E', 1408, LiteOnMethod.New, Toc);

    private sealed class SequenceScanner(int count) : IQualityScanner
    {
        public string Name => "Sequence";
        public ScannerCapabilities Probe(DriveInfoEx drive) => new(true, true, true, true, null);

        public async IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions o, IProgress<ScanProgress> p,
            [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return new ScanSample(i * 75, i, null, i, 0, 8.0);
            }
        }
    }

    private sealed class CollectingProgress(List<ScanProgress> sink) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) { lock (sink) sink.Add(value); }
    }

    // Feature: disccheck, Property 12: Ordine e completezza dei consumer della pipeline
    // **Validates: Requirements 9.5, 9.9, 9.10**
    [Property(MaxTest = 100)]
    public bool BothConsumersReceiveAllSamplesInOrder(NonNegativeInt n, byte slowEvery, byte delayMs)
    {
        int count = n.Get % 1500;                  // up to > 1 channel capacity (1.024) to hit backpressure
        int every = 1 + slowEvery % 200;           // UI consumer stalls every `every` samples
        int delay = delayMs % 3;                   // 0-2 ms stall

        var seen = new List<ScanSample>();
        var reports = new List<ScanProgress>();

        var result = new ScanPipeline().RunAsync(new SequenceScanner(count), Options, new CollectingProgress(reports),
            async s =>
            {
                if (delay > 0 && s.TimeSeconds % every == 0) await Task.Delay(delay);
                seen.Add(s);
            }, CancellationToken.None).GetAwaiter().GetResult();

        var expected = Enumerable.Range(0, count).ToArray();
        return result.Status == ScanRunStatus.Completed
            && result.Samples.Select(s => s.TimeSeconds).SequenceEqual(expected)
            && seen.Select(s => s.TimeSeconds).SequenceEqual(expected)
            && reports.Count == count;
    }
}
