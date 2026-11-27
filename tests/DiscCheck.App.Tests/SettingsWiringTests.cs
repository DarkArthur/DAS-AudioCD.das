using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using DiscCheck.Verify;
using FluentAssertions;
using NSubstitute;

namespace DiscCheck.App.Tests;

/// <summary>Settings feed the services: thresholds, default speed, N of re-reads, Lite-On variant (Requirement 22).</summary>
public sealed class SettingsWiringTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dc-wiring-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;

    public SettingsWiringTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        _store.Load();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private ScanViewModel CreateScan()
    {
        var drives = new DriveViewModel(Substitute.For<IDriveEnumerator>(), Substitute.For<IDriveResetService>());
        var disc = new DiscViewModel(Substitute.For<IDiscReader>(), drives, Substitute.For<IDiscMediaAssociation>());
        return new ScanViewModel(drives, disc, new ScannerSelector([]), Substitute.For<IScanSpeedService>(), new ScanPipeline(),
                                 new StartupInfo(true, true, null), null, null, null, _store);
    }

    [Fact]
    public void Scan_uses_stored_thresholds_and_follows_changes()
    {
        var thresholds = new Thresholds(2, 80, 300);
        _store.Save(_store.Current with { Thresholds = thresholds }).Success.Should().BeTrue();
        var vm = CreateScan();

        vm.ShowThresholds = false;
        vm.ShowThresholds = true; // forces the chart thresholds to be recomputed
        vm.ChartThresholds!.C1MaxWarn.Should().Be(80);
        vm.ChartThresholds.C1MaxKo.Should().Be(300);

        _store.Save(_store.Current with { Thresholds = new Thresholds(1, 40, 100) });
        vm.ChartThresholds!.C1MaxWarn.Should().Be(40);
        vm.ChartThresholds.C1MaxKo.Should().Be(100);
    }

    [Fact]
    public void Scan_preselects_the_default_speed_from_the_settings()
    {
        _store.Save(_store.Current with { DefaultSpeedX = 16 });
        var vm = CreateScan();

        vm.SelectedSpeed!.KBps.Should().Be(ScanSpeeds.ToKBps(16));
    }

    [Fact]
    public void Scan_adds_a_non_standard_default_speed_and_updates_it_when_settings_change()
    {
        var vm = CreateScan();
        vm.SelectedSpeed!.KBps.Should().Be(ScanSpeeds.ToKBps(8));

        _store.Save(_store.Current with { DefaultSpeedX = 12 });

        vm.SelectedSpeed!.KBps.Should().Be(ScanSpeeds.ToKBps(12));
        vm.Speeds.Should().Contain(o => o.KBps == ScanSpeeds.ToKBps(12));
        vm.Speeds.Last().KBps.Should().BeNull(); // "massima" stays last
    }

    [Fact]
    public void Verify_uses_the_stored_number_of_rereads_and_follows_changes()
    {
        _store.Save(_store.Current with { RereadAttempts = 5 });
        var transports = Substitute.For<DiscCheck.Scsi.Drives.IScsiTransportFactory>();
        var drives = new DriveViewModel(Substitute.For<IDriveEnumerator>(), Substitute.For<IDriveResetService>());
        var disc = new DiscViewModel(Substitute.For<IDiscReader>(), drives, Substitute.For<IDiscMediaAssociation>());
        var repo = Substitute.For<IDriveRepository>();
        var source = new SourceReader(Substitute.For<IFlacDecoder>());
        var offsets = new OffsetService(repo);
        var vm = new VerifyViewModel(drives, disc, new Verifier(transports, source), offsets,
                                     new OffsetAutoDetector(transports, source, offsets),
                                     new CacheDetectorRunner(transports, repo), new StartupInfo(true, true, null),
                                     null, _store);

        vm.MaxRereadCount.Should().Be(5);

        _store.Save(_store.Current with { RereadAttempts = 7 });
        vm.MaxRereadCount.Should().Be(7);
    }

    [Fact]
    public void LiteOn_variant_reports_the_stored_value()
    {
        var setting = new StoreLiteOnMethodSetting(_store);
        setting.Method.Should().Be(LiteOnMethod.New);

        _store.Save(_store.Current with { LiteOnMethod = LiteOnMethod.Old });

        setting.Method.Should().Be(LiteOnMethod.Old);
    }
}
