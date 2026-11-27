using System.Runtime.CompilerServices;
using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.App.Tests;

public class ScanViewModelTests
{
    private static readonly Toc SampleToc = new(
        [new TocEntry(1, 0, true), new TocEntry(2, 15000, true)], 30000);

    private sealed class FakeScanner(Func<CancellationToken, IAsyncEnumerable<ScanSample>> scan) : IQualityScanner
    {
        public string Name => ScannerSelector.StandardC2Name;

        public ScannerCapabilities Probe(DriveInfoEx drive) => new(true, false, true, false, null);

        public IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions options, IProgress<ScanProgress> progress,
                                                     CancellationToken ct) => scan(ct);
    }

    private sealed record Fixture(ScanViewModel Vm, DiscViewModel Disc, IScanSpeedService Speed);

    private static async IAsyncEnumerable<ScanSample> Samples(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new ScanSample(i * 75, i, null, 0, 0, 8.0);
            await Task.Yield();
        }
    }

    private static async Task<Fixture> CreateAsync(
        Func<CancellationToken, IAsyncEnumerable<ScanSample>>? scan = null,
        bool withDisc = true,
        IReadOnlyList<int>? supportedSpeeds = null,
        string? speedError = null)
    {
        var drives = new DriveViewModel(Substitute.For<IDriveEnumerator>(), Substitute.For<IDriveResetService>());
        var info = new DriveInfoEx('E', "TEST", "Drive", "1.0", true, supportedSpeeds ?? [], 0, null, null, null, null);
        drives.SelectedDrive = new DriveItemViewModel(new DriveListItem('E', info, null));

        var disc = new DiscViewModel(Substitute.For<IDiscReader>(), drives, Substitute.For<IDiscMediaAssociation>());
        if (withDisc)
            disc.Apply(new DiscReadResult(DiscStatus.Audio, string.Empty, SampleToc, "abc", null, null, null));

        var scanner = new FakeScanner(scan ?? (_ => Samples(3)));
        var speed = Substitute.For<IScanSpeedService>();
        speed.SetSpeed(default, default).ReturnsForAnyArgs(speedError);

        var vm = new ScanViewModel(drives, disc, new ScannerSelector([scanner]), speed, new ScanPipeline(),
                                   new StartupInfo(true, true, null));
        await vm.UpdateScannerAsync();
        return new Fixture(vm, disc, speed);
    }

    [Fact]
    public async Task Start_RunsToCompletion_ReturnsToIdleWithSummary()
    {
        var f = await CreateAsync();
        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();

        await f.Vm.StartAsync();

        f.Vm.State.Should().Be(ScanState.Idle);
        f.Vm.StatusText.Should().Be(ScanViewModel.StatusCompleted);
        f.Vm.ErrorMessage.Should().BeNull();
        f.Vm.HasSummary.Should().BeTrue();
        f.Vm.Samples.Should().HaveCount(3);
        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();
        f.Speed.Received(1).SetSpeed('E', ScanSpeeds.ToKBps(8));
    }

    [Fact]
    public async Task Start_WhileRunning_StartDisabledAndCancelEnabled()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var f = await CreateAsync(ct => Blocking(running, ct));

        f.Vm.CancelCommand.CanExecute(null).Should().BeFalse();
        var start = f.Vm.StartAsync();
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10));

        f.Vm.State.Should().Be(ScanState.Running);
        f.Vm.StatusText.Should().Be(ScanViewModel.StatusRunning);
        f.Vm.StartCommand.CanExecute(null).Should().BeFalse();
        f.Vm.CancelCommand.CanExecute(null).Should().BeTrue();

        f.Vm.Cancel();
        await start.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cancel_EndsAsCancelled_WithoutVerdictOrError()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var f = await CreateAsync(ct => Blocking(running, ct));

        var start = f.Vm.StartAsync();
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10));
        f.Vm.Cancel();
        await start.WaitAsync(TimeSpan.FromSeconds(10));

        f.Vm.State.Should().Be(ScanState.Idle);
        f.Vm.StatusText.Should().Be(ScanViewModel.StatusCancelled);
        f.Vm.HasVerdict.Should().BeFalse();
        f.Vm.HasSummary.Should().BeFalse();
        f.Vm.ErrorMessage.Should().BeNull();
        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();
        f.Vm.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ScannerFailure_ShowsErrorAndReturnsToIdle()
    {
        var f = await CreateAsync(_ => Failing());

        await f.Vm.StartAsync();

        f.Vm.State.Should().Be(ScanState.Idle);
        f.Vm.StatusText.Should().Be(ScanViewModel.StatusError);
        f.Vm.ErrorMessage.Should().Contain("lettura fallita");
        f.Vm.HasVerdict.Should().BeFalse();
        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SetSpeedError_ShowsErrorWithoutScanning()
    {
        var f = await CreateAsync(speedError: "il drive rifiuta la velocità");

        await f.Vm.StartAsync();

        f.Vm.State.Should().Be(ScanState.Idle);
        f.Vm.StatusText.Should().Be(ScanViewModel.StatusError);
        f.Vm.ErrorMessage.Should().Be("il drive rifiuta la velocità");
        f.Vm.Samples.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_DisabledWithoutDisc_ShowsMessage()
    {
        var f = await CreateAsync(withDisc: false);

        f.Vm.StartCommand.CanExecute(null).Should().BeFalse();
        f.Vm.AvailabilityMessage.Should().StartWith(ScanViewModel.NoDiscMessage);
    }

    [Fact]
    public async Task Start_EnabledAgainWhenDiscIsInserted()
    {
        var f = await CreateAsync(withDisc: false);

        f.Disc.Apply(new DiscReadResult(DiscStatus.Audio, string.Empty, SampleToc, "abc", null, null, null));

        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();
        f.Vm.AvailabilityMessage.Should().BeNull();
    }

    [Fact]
    public async Task UnsupportedSpeed_AsksConfirmationAndBlocksStart()
    {
        var f = await CreateAsync(supportedSpeeds: [ScanSpeeds.ToKBps(8), ScanSpeeds.ToKBps(16)]);

        f.Vm.SelectedSpeed = ScanViewModel.SpeedOptions[0]; // 4x, not supported

        f.Vm.HasSpeedConfirm.Should().BeTrue();
        f.Vm.SpeedConfirmMessage.Should().Contain("4x").And.Contain("8x");
        f.Vm.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task UnsupportedSpeed_Confirm_ScansAtNearestSupportedSpeed()
    {
        var f = await CreateAsync(supportedSpeeds: [ScanSpeeds.ToKBps(8), ScanSpeeds.ToKBps(16)]);
        f.Vm.SelectedSpeed = ScanViewModel.SpeedOptions[0];

        f.Vm.ConfirmSpeed();

        f.Vm.HasSpeedConfirm.Should().BeFalse();
        f.Vm.HasSpeedNote.Should().BeTrue();
        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();

        await f.Vm.StartAsync();

        f.Speed.Received(1).SetSpeed('E', ScanSpeeds.ToKBps(8));
    }

    [Fact]
    public async Task UnsupportedSpeed_Cancel_RevertsSelectionAndEnablesStart()
    {
        var f = await CreateAsync(supportedSpeeds: [ScanSpeeds.ToKBps(8), ScanSpeeds.ToKBps(16)]);
        var previous = f.Vm.SelectedSpeed;
        f.Vm.SelectedSpeed = ScanViewModel.SpeedOptions[0];

        f.Vm.CancelSpeed();

        f.Vm.SelectedSpeed.Should().Be(previous);
        f.Vm.HasSpeedConfirm.Should().BeFalse();
        f.Vm.StartCommand.CanExecute(null).Should().BeTrue();

        await f.Vm.StartAsync();

        f.Speed.Received(1).SetSpeed('E', ScanSpeeds.ToKBps(8));
    }

    private static async IAsyncEnumerable<ScanSample> Blocking(
        TaskCompletionSource running, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ScanSample(0, 0, null, 0, 0, 8.0);
        running.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }

    private static async IAsyncEnumerable<ScanSample> Failing()
    {
        yield return new ScanSample(0, 0, null, 0, 0, 8.0);
        await Task.Yield();
        throw new ScanFailedException("lettura fallita", 75);
    }
}
