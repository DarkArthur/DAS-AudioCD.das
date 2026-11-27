using DiscCheck.Core;
namespace DiscCheck.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disccheck-settings-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(SettingsValidator.Validate(new Settings()));
        var d = new Settings();
        Assert.Equal(new Thresholds(1, 50, 220), d.Thresholds);
        Assert.Equal(8, d.DefaultSpeedX);
        Assert.Equal(LiteOnMethod.New, d.LiteOnMethod);
        Assert.False(d.DebugMode);
        Assert.Equal(SettingsTheme.Light, d.Theme);
        Assert.Equal((3, 30, 5), (d.RereadAttempts, d.ScsiTimeoutSeconds, d.RecheckYears));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(52, false)]
    [InlineData(53, true)]
    public void Speed_range_is_1_to_52(int speed, bool invalid) =>
        Assert.Equal(invalid, SettingsValidator.Validate(new Settings { DefaultSpeedX = speed }).Any(e => e.Field == "defaultSpeedX"));

    [Theory]
    [InlineData(0, true)] [InlineData(1, false)] [InlineData(10, false)] [InlineData(11, true)]
    public void Reread_attempts_range(int n, bool invalid) =>
        Assert.Equal(invalid, SettingsValidator.Validate(new Settings { RereadAttempts = n }).Any(e => e.Field == "rereadAttempts"));

    [Theory]
    [InlineData(0, true)] [InlineData(1, false)] [InlineData(300, false)] [InlineData(301, true)]
    public void Timeout_range(int n, bool invalid) =>
        Assert.Equal(invalid, SettingsValidator.Validate(new Settings { ScsiTimeoutSeconds = n }).Any(e => e.Field == "scsiTimeoutSeconds"));

    [Theory]
    [InlineData(4, true)] [InlineData(5, false)] [InlineData(10, false)] [InlineData(11, true)]
    public void Recheck_years_range(int n, bool invalid) =>
        Assert.Equal(invalid, SettingsValidator.Validate(new Settings { RecheckYears = n }).Any(e => e.Field == "recheckYears"));

    [Theory]
    [InlineData("")] [InlineData("   ")]
    public void Empty_database_path_is_rejected(string path) =>
        Assert.Contains(SettingsValidator.Validate(new Settings { DatabasePath = path }), e => e.Field == "databasePath");

    [Theory]
    [InlineData(1, 50, 50, true)]   // WARN == KO
    [InlineData(1, 60, 50, true)]   // WARN > KO
    [InlineData(-1, 50, 220, true)]
    [InlineData(1, -1, 220, true)]
    [InlineData(1, 50, double.NaN, true)]
    [InlineData(0, 0, 1, false)]
    public void Threshold_rules(double mean, double warn, double ko, bool invalid) =>
        Assert.Equal(invalid, SettingsValidator.Validate(new Settings { Thresholds = new Thresholds(mean, warn, ko) })
            .Any(e => e.Field.StartsWith("thresholds")));

    [Fact]
    public void Json_roundtrip_preserves_all_fields()
    {
        var s = new Settings
        {
            Thresholds = new Thresholds(2.5, 40, 300), DefaultSpeedX = 16, LiteOnMethod = LiteOnMethod.Old, DatabasePath = @"D:\x\y.db",
            DebugMode = true, Theme = SettingsTheme.Dark, RereadAttempts = 7, ScsiTimeoutSeconds = 120, RecheckYears = 9,
        };
        var json = SettingsJson.Serialize(s);
        Assert.Contains("\"liteOnMethod\": \"Old\"", json);
        Assert.Equal(s, SettingsJson.Deserialize(json));
    }

    [Fact]
    public void Missing_file_is_created_with_defaults()
    {
        var store = new SettingsStore(FilePath);
        var r = store.Load();
        Assert.True(r.FileCreated);
        Assert.Empty(r.Errors);
        Assert.Equal(new Settings(), r.Settings);
        Assert.True(File.Exists(FilePath));
        Assert.Equal(new Settings(), SettingsJson.Deserialize(File.ReadAllText(FilePath)));
    }

    [Fact]
    public void Valid_file_is_loaded()
    {
        var s = new Settings { DefaultSpeedX = 24, LiteOnMethod = LiteOnMethod.Old, Theme = SettingsTheme.Dark };
        var store = new SettingsStore(FilePath);
        Assert.True(store.Save(s).Success);
        var r = new SettingsStore(FilePath).Load();
        Assert.Empty(r.Errors);
        Assert.Equal(s, r.Settings);
    }

    [Fact]
    public void Invalid_thresholds_reset_all_thresholds_and_leave_file_unchanged()
    {
        Directory.CreateDirectory(_dir);
        const string json = """{ "thresholds": { "c1MeanWarn": 3, "c1MaxWarn": 100, "c1MaxKo": 100 }, "defaultSpeedX": 16 }""";
        File.WriteAllText(FilePath, json);
        var r = new SettingsStore(FilePath).Load();
        Assert.Equal(new Thresholds(), r.Settings.Thresholds);
        Assert.Equal(16, r.Settings.DefaultSpeedX);
        Assert.Contains(r.Errors, e => e.Field.StartsWith("thresholds"));
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Non_numeric_threshold_is_reported()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "thresholds": { "c1MeanWarn": "abc" } }""");
        var r = new SettingsStore(FilePath).Load();
        Assert.Equal(new Thresholds(), r.Settings.Thresholds);
        Assert.Contains(r.Errors, e => e.Field == "thresholds");
    }

    [Fact]
    public void Invalid_recheck_years_falls_back_to_5()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "recheckYears": 12 }""");
        var r = new SettingsStore(FilePath).Load();
        Assert.Equal(5, r.Settings.RecheckYears);
        Assert.Contains(r.Errors, e => e.Field == "recheckYears");
    }

    [Fact]
    public void Corrupt_file_gives_defaults_warning_and_is_untouched()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        var r = new SettingsStore(FilePath).Load();
        Assert.Equal(new Settings(), r.Settings);
        Assert.True(r.HasWarnings);
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Save_invalid_is_rejected_and_file_unchanged()
    {
        var store = new SettingsStore(FilePath);
        store.Load();
        var before = File.ReadAllText(FilePath);
        var r = store.Save(new Settings { DefaultSpeedX = 99, DatabasePath = "" });
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Field == "defaultSpeedX");
        Assert.Contains(r.Errors, e => e.Field == "databasePath");
        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Equal(new Settings(), store.Current);
    }

    [Fact]
    public void Save_completes_within_one_second()
    {
        var store = new SettingsStore(FilePath);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = store.Save(new Settings { DebugMode = true });
        sw.Stop();
        Assert.True(r.Success);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LiteOn_setting_follows_the_store()
    {
        var store = new SettingsStore(FilePath);
        ILiteOnMethodSetting setting = new StoreLiteOnMethodSetting(store);
        Assert.Equal(LiteOnMethod.New, setting.Method);
        store.Save(new Settings { LiteOnMethod = LiteOnMethod.Old });
        Assert.Equal(LiteOnMethod.Old, setting.Method);
    }
}
