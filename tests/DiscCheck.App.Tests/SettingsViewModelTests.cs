using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using DiscCheck.Data;
using FluentAssertions;
using NSubstitute;
using Serilog.Core;
using Serilog.Events;

namespace DiscCheck.App.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dc-settings-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;
    private readonly LoggingLevelSwitch _level = new(LogEventLevel.Information);
    private readonly IThemeApplier _theme = Substitute.For<IThemeApplier>();
    private readonly SwitchableArchive _archive;
    private readonly string _dbA;
    private readonly string _dbB;

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _dbA = Path.Combine(_dir, "a.db");
        _dbB = Path.Combine(_dir, "sub", "b.db");
        _store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        _store.Load();
        _store.Save(_store.Current with { DatabasePath = _dbA });
        _archive = new SwitchableArchive(ArchiveOpener.Open(_dbA), _dbA);
    }

    public void Dispose()
    {
        _archive.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private SettingsViewModel Create() => new(_store, new LogLevelController(_level), _theme, _archive);

    private string FileText => File.ReadAllText(_store.Path);

    [Fact]
    public void Save_applies_debug_mode_to_the_level_switch_and_theme_without_restart()
    {
        var vm = Create();
        vm.DebugMode = true;
        vm.Theme = SettingsTheme.Dark;

        vm.SaveCommand.Execute(null);

        _level.MinimumLevel.Should().Be(LogEventLevel.Verbose);
        _theme.Received(1).Apply(SettingsTheme.Dark);
        vm.StatusMessage.Should().Be(SettingsViewModel.SavedMessage);
        _store.Current.DebugMode.Should().BeTrue();

        vm.DebugMode = false;
        vm.SaveCommand.Execute(null);
        _level.MinimumLevel.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void Invalid_values_are_rejected_with_field_and_constraint_and_file_is_unchanged()
    {
        var vm = Create();
        string before = FileText;
        vm.DefaultSpeedText = "60";
        vm.ScsiTimeoutText = "abc";
        vm.DebugMode = true;

        vm.SaveCommand.Execute(null);

        vm.Errors.Should().Contain(e => e.StartsWith("defaultSpeedX:") && e.Contains("1 e 52"));
        vm.Errors.Should().Contain(e => e.StartsWith("scsiTimeoutSeconds:"));
        FileText.Should().Be(before);
        _level.MinimumLevel.Should().Be(LogEventLevel.Information);
        _theme.DidNotReceiveWithAnyArgs().Apply(default);
    }

    [Fact]
    public void Thresholds_with_warn_not_below_ko_are_rejected()
    {
        var vm = Create();
        vm.C1MaxWarnText = "500";
        vm.C1MaxKoText = "100";

        vm.SaveCommand.Execute(null);

        vm.HasErrors.Should().BeTrue();
        _store.Current.Thresholds.Should().Be(new Thresholds());
    }

    [Fact]
    public void Changing_the_database_path_asks_for_confirmation_and_changes_nothing_yet()
    {
        var vm = Create();
        string before = FileText;
        vm.DatabasePath = _dbB;

        vm.SaveCommand.Execute(null);

        vm.AwaitingDatabaseConfirmation.Should().BeTrue();
        _archive.CurrentPath.Should().Be(_dbA);
        File.Exists(_dbB).Should().BeFalse();
        FileText.Should().Be(before);
    }

    [Fact]
    public void Confirming_reopens_on_the_new_path_creating_the_database_and_saves()
    {
        var vm = Create();
        vm.DatabasePath = _dbB;
        vm.SaveCommand.Execute(null);

        vm.ConfirmDatabaseChangeCommand.Execute(null);

        _archive.CurrentPath.Should().Be(_dbB);
        File.Exists(_dbB).Should().BeTrue();
        _store.Current.DatabasePath.Should().Be(_dbB);
        vm.AwaitingDatabaseConfirmation.Should().BeFalse();
        vm.StatusMessage.Should().Be(SettingsViewModel.SavedMessage);
        _archive.Execute(r => r.Discs.Should().NotBeNull()); // new Database is usable
    }

    [Fact]
    public void Cancelling_keeps_the_previous_path()
    {
        var vm = Create();
        vm.DatabasePath = _dbB;
        vm.SaveCommand.Execute(null);

        vm.CancelDatabaseChangeCommand.Execute(null);

        vm.AwaitingDatabaseConfirmation.Should().BeFalse();
        vm.DatabasePath.Should().Be(_dbA);
        _archive.CurrentPath.Should().Be(_dbA);
        _store.Current.DatabasePath.Should().Be(_dbA);
    }

    [Fact]
    public void Unopenable_new_database_keeps_old_access_and_does_not_touch_the_file()
    {
        var vm = Create();
        string before = FileText;
        // A path whose parent is an existing file cannot be created.
        string blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        vm.DatabasePath = Path.Combine(blocker, "x.db");
        vm.DebugMode = true;
        vm.SaveCommand.Execute(null);

        vm.ConfirmDatabaseChangeCommand.Execute(null);

        vm.HasErrors.Should().BeTrue();
        vm.Errors[0].Should().Contain("Database");
        _archive.CurrentPath.Should().Be(_dbA);
        _archive.Execute(r => r.Discs).Should().NotBeNull();
        FileText.Should().Be(before);
        _level.MinimumLevel.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void Store_changed_event_is_raised_on_successful_save_only()
    {
        int raised = 0;
        _store.Changed += _ => raised++;

        _store.Save(_store.Current with { DefaultSpeedX = 0 }).Success.Should().BeFalse();
        raised.Should().Be(0);

        _store.Save(_store.Current with { DefaultSpeedX = 16 }).Success.Should().BeTrue();
        raised.Should().Be(1);
    }

    [Fact]
    public void Theme_variant_mapping_follows_the_setting()
    {
        AvaloniaThemeApplier.ToVariant(SettingsTheme.Dark).Should().Be(Avalonia.Styling.ThemeVariant.Dark);
        AvaloniaThemeApplier.ToVariant(SettingsTheme.Light).Should().Be(Avalonia.Styling.ThemeVariant.Light);
    }
}
