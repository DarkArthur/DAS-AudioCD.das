using Avalonia;
using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.App.Views;
using DiscCheck.Codecs.Flake;
using DiscCheck.Core;
using DiscCheck.Data;
using DiscCheck.Scanners;
using DiscCheck.Scsi.Drives;
using DiscCheck.Verify;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;

namespace DiscCheck.App;

public static class Program
{
    private const string AppName = "DiscCheck";

    /// <summary>Host built before Avalonia starts; resolved by <see cref="App"/>.</summary>
    internal static IHost? Host { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var levelSwitch = new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);
        var logger = LoggingSetup.Create(levelSwitch, LoggingSetup.DefaultLogDirectory, out var fileLogAvailable);
        Log.Logger = logger;

        try
        {
            // Settings are loaded before the host and the main view exist (Requirement 22.3); a missing file is created.
            var settingsStore = new SettingsStore(SettingsStore.DefaultPath());
            var settingsLoad = settingsStore.Load();
            new LogLevelController(levelSwitch).SetDebugMode(settingsStore.Current.DebugMode);

            var startup = new StartupInfo(
                StartupInfo.DetectAdministrator(),
                fileLogAvailable,
                fileLogAvailable ? LoggingSetup.DefaultLogDirectory : null);

            if (!fileLogAvailable)
            {
                Log.Warning("File logging is not available; logging to console only");
            }

            if (!startup.IsAdministrator)
            {
                Log.Warning("Application is not running as administrator; scans and verification are disabled");
            }

            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseSerilog(logger, dispose: false)
                .ConfigureServices(services =>
                {
                    services.AddSingleton(levelSwitch);
                    services.AddSingleton(settingsStore);
                    services.AddSingleton(settingsLoad);
                    services.AddSingleton<IThemeApplier, AvaloniaThemeApplier>();
                    services.AddSingleton<LogLevelController>();
                    services.AddSingleton(startup);
                    services.AddSingleton<IScsiTransportFactory>(_ =>
                        OperatingSystem.IsWindows()
                            ? new WinScsiTransportFactory(null, () => TimeSpan.FromSeconds(settingsStore.Current.ScsiTimeoutSeconds))
                            : throw new PlatformNotSupportedException("DiscCheck requires Windows."));
                    services.AddSingleton<IDriveEnumerator>(sp =>
                        new DriveEnumerator(sp.GetRequiredService<IScsiTransportFactory>()));
                    // The vendor end-of-scan command is registered in M6 (ported from QPxTool_Source).
                    services.AddSingleton<IDriveResetService>(sp =>
                        new DriveResetService(sp.GetRequiredService<IScsiTransportFactory>()));
                    services.AddSingleton(_ => AtipTable.LoadEmbedded(msg => Log.Error("{Message}", msg)));
                    services.AddSingleton<IDiscReader>(sp =>
                        new DiscReader(sp.GetRequiredService<IScsiTransportFactory>(), sp.GetRequiredService<AtipTable>()));
                    // IDiscMediaAssociation is registered by the persistence layer in M4.
                    // Quality scanners are added by registering IQualityScanner implementations here (Requirement 6.7).
                    services.AddSingleton<IQualityScanner>(sp => new LiteOnScanner(sp.GetRequiredService<IScsiTransportFactory>()));
                    services.AddSingleton<IQualityScanner>(sp => new StandardC2Scanner(sp.GetRequiredService<IScsiTransportFactory>()));
                    // "Variante Lite-On" setting: always the value currently stored in settings.json.
                    services.AddSingleton<ILiteOnMethodSetting>(sp => new StoreLiteOnMethodSetting(sp.GetRequiredService<SettingsStore>()));
                    services.AddSingleton<IDriveHardwareHints>(_ => OperatingSystem.IsWindows()
                        ? new DriveHardwareHints()
                        : throw new PlatformNotSupportedException("DiscCheck requires Windows."));
                    services.AddSingleton(sp => new ScannerSelector(
                        sp.GetServices<IQualityScanner>(), msg => Log.Warning("{Message}", msg)));
                    services.AddSingleton<IScanSpeedService>(sp =>
                        new ScanSpeedService(sp.GetRequiredService<IScsiTransportFactory>()));
                    // Database at the configured path; opening errors are shown in the main window (Requirement 16.10).
                    // The archive can be moved to another file at runtime from the Impostazioni view (Requirement 22.6).
                    services.AddSingleton(_ => ArchiveOpener.Open(SettingsViewModel.ExpandPath(settingsStore.Current.DatabasePath)));
                    services.AddSingleton(sp => new SwitchableArchive(
                        sp.GetRequiredService<ArchiveOpenResult>(), SettingsViewModel.ExpandPath(settingsStore.Current.DatabasePath)));
                    services.AddSingleton<IArchive>(sp => sp.GetRequiredService<SwitchableArchive>());
                    services.AddSingleton<IArchiveSwitcher>(sp => sp.GetRequiredService<SwitchableArchive>());
                    services.AddSingleton<ScanPipeline>();
                    // Export: file dialogs on the main window. The D5 attribution (Requirement 36.6) is supplied by
                    // AssetAttribution.CurrentText, which stays null while D5 is open (resolved in task 17.6).
                    services.AddSingleton<IExportDialogs, AvaloniaExportDialogs>();
                    services.AddSingleton(sp => new Exporter(sp.GetRequiredService<IExportDialogs>(), () => AssetAttribution.CurrentText));
                    // Verification: the FLAC decoder implementation is registered here only (Requirement 25.9).
                    services.AddSingleton<IFlacDecoder, FlakeDecoder>();
                    services.AddSingleton<SourceReader>();
                    services.AddSingleton(sp => new Verifier(sp.GetRequiredService<IScsiTransportFactory>(), sp.GetRequiredService<SourceReader>()));
                    services.AddSingleton<IDriveRepository>(sp => new ArchiveDriveRepository(sp.GetRequiredService<IArchive>()));
                    services.AddSingleton<OffsetService>();
                    services.AddSingleton<OffsetAutoDetector>();
                    services.AddSingleton<CacheDetectorRunner>();
                    services.AddSingleton<C2ReliabilityTestRunner>();
                    services.AddSingleton<DriveViewModel>();
                    services.AddSingleton<DiscViewModel>();
                    services.AddSingleton(sp => new C2ReliabilityViewModel(
                        sp.GetRequiredService<DriveViewModel>(), sp.GetRequiredService<DiscViewModel>(),
                        sp.GetRequiredService<C2ReliabilityTestRunner>(), startup.ScansEnabled));
                    services.AddSingleton<ScanViewModel>();
                    services.AddSingleton<VerifyViewModel>();
                    services.AddSingleton<HistoryViewModel>();
                    services.AddSingleton<ComparisonViewModel>();
                    services.AddSingleton(sp => new DegradationViewModel(
                        sp.GetRequiredService<IArchive>(), sp.GetRequiredService<SettingsStore>()));
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<IUrlOpener, ShellUrlOpener>();
                    services.AddSingleton(new UpdateChecker(new UpdateManifestReader()));
                    services.AddSingleton(sp => new UpdateNotificationViewModel(
                        sp.GetRequiredService<UpdateChecker>(), sp.GetRequiredService<SettingsStore>(),
                        AboutViewModel.CurrentAppVersion(), sp.GetRequiredService<IUrlOpener>()));
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            Log.Information("{App} starting (administrator: {Admin})", AppName, startup.IsAdministrator);

            // Resolve the view model eagerly so DI errors surface here, before the UI loop.
            _ = Host.Services.GetRequiredService<MainWindowViewModel>();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "{App} failed to start", AppName);
            NativeMessageBox.ShowError(AppName, $"Impossibile avviare {AppName}.\n\n{ex.Message}");
            return 1;
        }
        finally
        {
            Host?.Dispose();
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

