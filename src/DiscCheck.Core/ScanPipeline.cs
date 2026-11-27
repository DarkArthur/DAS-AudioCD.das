using System.Threading.Channels;

namespace DiscCheck.Core;

/// <summary>Final state of a scan run.</summary>
public enum ScanRunStatus { Completed, Cancelled, Failed }

/// <summary>
/// Outcome of <see cref="ScanPipeline.RunAsync"/>. <see cref="Samples"/> holds every sample published before the
/// scan ended (also after a failure or cancellation); a cancelled scan must not be saved (Requirement 9.8).
/// </summary>
public sealed record ScanRunResult(ScanRunStatus Status, IReadOnlyList<ScanSample> Samples, Exception? Error);

/// <summary>
/// Producer/consumer scan pipeline (Requirement 9). A dedicated Scan_Thread drives the scanner and publishes
/// samples to a bounded Scan_Channel (1.024, <see cref="BoundedChannelFullMode.Wait"/>). A fan-out copies every
/// sample, in order, to two independent consumers: the UI consumer (progress and chart callback) and the
/// accumulator that builds the list for the Repository. A failure closes the channel with the error only after
/// the already published samples have been delivered. Closing the Test_Mode is the scanner's duty in the
/// <c>finally</c> of <c>ScanAsync</c>; the pipeline guarantees that the enumerator is always disposed on the
/// Scan_Thread (also on cancellation and error).
/// </summary>
public sealed class ScanPipeline
{
    public const int ChannelCapacity = 1024;

    private readonly int _capacity;

    public ScanPipeline() : this(ChannelCapacity) { }

    /// <summary>Capacity override for tests; production code uses <see cref="ChannelCapacity"/>.</summary>
    internal ScanPipeline(int capacity) => _capacity = capacity;

    /// <param name="onSample">Called by the UI consumer for every sample, in publication order.</param>
    public async Task<ScanRunResult> RunAsync(
        IQualityScanner scanner,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        Func<ScanSample, ValueTask>? onSample,
        CancellationToken ct)
    {
        var scanChannel = Channel.CreateBounded<ScanSample>(ChannelOptions());
        var uiChannel = Channel.CreateBounded<ScanSample>(ChannelOptions());
        var accChannel = Channel.CreateBounded<ScanSample>(ChannelOptions());

        // A faulting consumer cancels the producer so that nothing blocks on a channel nobody reads.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = stop.Token;

        // Scan_Thread: a single dedicated thread, distinct from the UI thread (Requirement 9.1).
        var producerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => ProduceOnScanThread(scanner, options, scanChannel.Writer, token, producerDone))
        {
            IsBackground = true,
            Name = "DiscCheck.ScanThread",
        };
        thread.Start();

        int leadOut = options.Toc.LeadOutLba;
        var samples = new List<ScanSample>();

        var fanOutTask = FanOutAsync(scanChannel.Reader, uiChannel.Writer, accChannel.Writer, token);

        var uiTask = Guard(async () =>
        {
            await foreach (var s in uiChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                progress?.Report(ToProgress(s, leadOut));
                if (onSample is not null)
                    await onSample(s).ConfigureAwait(false);
            }
        }, stop);

        var accTask = Guard(async () =>
        {
            await foreach (var s in accChannel.Reader.ReadAllAsync().ConfigureAwait(false))
                samples.Add(s);
        }, stop);

        Exception? consumerError = null;
        Exception? streamError = null;
        foreach (var task in new[] { uiTask, accTask })
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception ex)
            {
                // The fan-out forwards the producer error; a different exception is a consumer failure.
                if (ex is OperationCanceledException or ScanFailedException)
                    streamError ??= ex;
                else
                    consumerError ??= ex;
            }
        }
        await fanOutTask.ConfigureAwait(false);
        await producerDone.Task.ConfigureAwait(false);
        thread.Join();

        if (consumerError is not null)
            return new ScanRunResult(ScanRunStatus.Failed, samples, consumerError);
        if (streamError is OperationCanceledException)
            return new ScanRunResult(ScanRunStatus.Cancelled, samples, null);
        if (streamError is not null)
            return new ScanRunResult(ScanRunStatus.Failed, samples, streamError);
        return new ScanRunResult(ScanRunStatus.Completed, samples, null);

        BoundedChannelOptions ChannelOptions() => new(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        };
    }

    private static async Task Guard(Func<Task> body, CancellationTokenSource stop)
    {
        try
        {
            await Task.Run(body).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ScanFailedException)
        {
            stop.Cancel();
            throw;
        }
    }

    private static ScanProgress ToProgress(ScanSample s, int leadOut)
    {
        int reached = Math.Min(leadOut, s.Lba + SampleAggregator.SectorsPerSample);
        double percent = leadOut > 0 ? Math.Clamp(100.0 * reached / leadOut, 0, 100) : 0;
        return new ScanProgress(s.Lba, percent, s.SpeedX);
    }

    private static void ProduceOnScanThread(IQualityScanner scanner, ScanOptions options,
        ChannelWriter<ScanSample> writer, CancellationToken ct, TaskCompletionSource done)
    {
        Exception? failure = null;
        try
        {
            // Blocking on the dedicated thread is intended: the SCSI loop is synchronous.
            var enumerator = scanner.ScanAsync(options, new Progress<ScanProgress>(_ => { }), ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested(); // stop before the next SCSI command (Requirement 9.7)
                    if (!enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                        break;
                    var sample = enumerator.Current;
                    if (!writer.TryWrite(sample))
                        writer.WriteAsync(sample, ct).AsTask().GetAwaiter().GetResult(); // waits while full (9.4)
                }
            }
            finally
            {
                // Runs the scanner's finally block (end-of-scan command closing the Test_Mode) on this thread.
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            writer.TryComplete(failure);
            done.TrySetResult();
        }
    }

    private static async Task FanOutAsync(ChannelReader<ScanSample> source, ChannelWriter<ScanSample> a,
        ChannelWriter<ScanSample> b, CancellationToken ct)
    {
        Exception? failure = null;
        try
        {
            // No token on the read: samples already published are always delivered before the error (9.11).
            await foreach (var s in source.ReadAllAsync().ConfigureAwait(false))
            {
                await a.WriteAsync(s, ct).ConfigureAwait(false);
                await b.WriteAsync(s, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            a.TryComplete(failure);
            b.TryComplete(failure);
        }
    }
}
