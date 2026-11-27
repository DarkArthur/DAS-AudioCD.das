using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.App.Tests;

/// <summary>Lite-On outcomes in the App (Requirements 8.8-8.10, 11.7, 26.3, 26.4).</summary>
public class ScanViewModelLiteOnTests
{
    private static readonly Toc SampleToc = new(
        [new TocEntry(1, 0, true), new TocEntry(2, 15000, true)], 30000);

    private sealed class NamedScanner(string name, Func<IAsyncEnumerable<ScanSample>> scan) : IQualityScanner
    {
        public int Scans { get; private set; }
        public ScanOptions? LastOptions { get; private set; }
        public string Name => name;
        public ScannerCapabilities Probe(DriveInfoEx drive) => new(true, true, true, true, null);

        public IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions options, IProgress<ScanProgress> progress,
                                                     CancellationToken ct)
        {
            Scans++;
            LastOptions = options;
            return scan();
        }
    }

    private static async IAsyncEnumerable<ScanSample> InitFails()
    {
        await Task.Yield();
        throw new ScanInitFailedException("Inizializzazione della scansione Lite-On non riuscita: timeout.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<ScanSample> Samples()
    {
        for (int i = 0; i < 3; i++)
        {
            yield return new ScanSample(i * 75, i, 0, 0, null, 8.0);
            await Task.Yield();
        }
    }

    private static async Task<(ScanViewModel Vm, NamedScanner LiteOn, NamedScanner Standard)> CreateAsync(
        Func<IAsyncEnumerable<ScanSample>> liteOnScan, IDriveHardwareHints? hints = null,
        ILiteOnMethodSetting? setting = null)
    {
        var drives = new DriveViewModel(Substitute.For<IDriveEnumerator>(), Substitute.For<IDriveResetService>());
        var info = new DriveInfoEx('E', "LITE-ON", "Drive", "1.0", true, [], 0, null, null, null, null);
        drives.SelectedDrive = new DriveItemViewModel(new DriveListItem('E', info, null));
        var disc = new DiscViewModel(Substitute.For<IDiscReader>(), drives, Substitute.For<IDiscMediaAssociation>());
        disc.Apply(new DiscReadResult(DiscStatus.Audio, string.Empty, SampleToc, "abc", null, null, null));

        var lite = new NamedScanner(ScannerSelector.LiteOnName, liteOnScan);
        var std = new NamedScanner(ScannerSelector.StandardC2Name, Samples);
        var speed = Substitute.For<IScanSpeedService>();
        speed.SetSpeed(default, default).ReturnsForAnyArgs((string?)null);
        var vm = new ScanViewModel(drives, disc, new ScannerSelector([lite, std]), speed, new ScanPipeline(),
                                   new StartupInfo(true, true, null), null, setting, hints);
        await vm.UpdateScannerAsync();
        return (vm, lite, std);
    }

    [Fact]
    public async Task InitFailure_ShowsFirmwareMessage_AndStandardStartsOnlyAfterConfirm()
    {
        var (vm, lite, std) = await CreateAsync(InitFails);

        await vm.StartAsync();

        vm.StatusText.Should().Be(ScanViewModel.StatusError);
        vm.ErrorMessage.Should().Contain(ScanViewModel.FirmwareIncompatibleMessage);
        vm.OfferStandardFallback.Should().BeTrue();
        std.Scans.Should().Be(0);

        await vm.AcceptFallbackAsync();

        std.Scans.Should().Be(1);
        vm.ScannerName.Should().Be(ScannerSelector.StandardC2Name);
        vm.StatusText.Should().Be(ScanViewModel.StatusCompleted);
        vm.OfferStandardFallback.Should().BeFalse();
        lite.Scans.Should().Be(1);
    }

    [Fact]
    public async Task InitFailure_DeclineFallback_DoesNotStartStandard()
    {
        var (vm, _, std) = await CreateAsync(InitFails);
        await vm.StartAsync();

        vm.DeclineFallback();

        vm.OfferStandardFallback.Should().BeFalse();
        std.Scans.Should().Be(0);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public async Task InitFailure_UsbBridgeMessageOnlyOnUsb(bool? usb, bool expected)
    {
        var hints = Substitute.For<IDriveHardwareHints>();
        hints.IsUsbBus('E').Returns(usb);
        var (vm, _, _) = await CreateAsync(InitFails, hints);

        await vm.StartAsync();

        vm.ErrorMessage!.Contains(ScanViewModel.UsbBridgeMessage).Should().Be(expected);
    }

    [Fact]
    public async Task EndCommandFailure_ShowsResetDriveHint()
    {
        static async IAsyncEnumerable<ScanSample> EndFails()
        {
            yield return new ScanSample(0, 0, 0, 0, null, 8.0);
            await Task.Yield();
            throw new TestModeEndFailedException("fine scansione fallita", "DFA301", "timeout");
        }
        var (vm, _, _) = await CreateAsync(EndFails);

        await vm.StartAsync();

        vm.ShowResetDriveHint.Should().BeTrue();
        vm.ResetHintText.Should().Contain("Reset drive");
    }

    [Fact]
    public async Task MediaTekWarning_ShownWithLiteOnResultOnly()
    {
        var hints = Substitute.For<IDriveHardwareHints>();
        hints.IsMediaTek(Arg.Any<DriveInfoEx>()).Returns(true);
        var (vm, _, _) = await CreateAsync(Samples, hints);

        vm.ShowMediaTekWarning.Should().BeFalse("no result yet");
        await vm.StartAsync();

        vm.ShowMediaTekWarning.Should().BeTrue();
        vm.MediaTekWarningText.Should().Contain("C1").And.Contain("C2").And.Contain("CU");
    }

    [Fact]
    public async Task Start_UsesTheLiteOnMethodSetting()
    {
        var setting = new LiteOnMethodSetting(LiteOnMethod.Old);
        var (vm, lite, _) = await CreateAsync(Samples, setting: setting);

        await vm.StartAsync();

        lite.LastOptions!.Method.Should().Be(LiteOnMethod.Old);
    }

    [Fact]
    public async Task NewMethod_CuIsNotAvailable()
    {
        var (vm, _, _) = await CreateAsync(Samples);

        await vm.StartAsync();

        vm.CuTotal.Should().Be(ScanViewModel.NotAvailable);
    }
}
