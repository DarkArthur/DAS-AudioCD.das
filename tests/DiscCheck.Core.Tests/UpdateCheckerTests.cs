using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("upd-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeReader(Func<string, string> read) : IUpdateManifestReader
    {
        public string? LastSource;
        public Task<string> ReadAsync(string source, CancellationToken ct)
        {
            LastSource = source;
            return Task.FromResult(read(source));
        }
    }

    private static UpdateChecker With(string json) => new(new FakeReader(_ => json));

    [Fact]
    public async Task Newer_version_is_reported_with_http_url_and_notes()
    {
        var r = await With("""{"version":"1.2.0","url":"https://example.org/r/1.2.0","notes":"Correzioni"}""")
            .CheckAsync("https://example.org/version.json", "1.1.0");
        r.Status.Should().Be(UpdateStatus.UpdateAvailable);
        r.LatestVersion.Should().Be("1.2.0");
        r.Url.Should().Be("https://example.org/r/1.2.0");
        r.Notes.Should().Be("Correzioni");
    }

    [Theory]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("1.0.9", "1.1.0")]
    [InlineData("v1.1.0", "1.1.0+abc123")]
    public async Task Same_or_older_version_is_up_to_date(string published, string current)
    {
        var r = await With($$"""{"version":"{{published}}"}""").CheckAsync("x.json", current);
        r.Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0")]   // numeric, not alphabetical comparison
    [InlineData("2.0", "1.99.99")]
    [InlineData("v1.1.1", "1.1.0-rc.1")]
    public async Task Version_comparison_is_numeric_and_accepts_prefix_and_suffix(string published, string current)
    {
        var r = await With($$"""{"version":"{{published}}"}""").CheckAsync("x.json", current);
        r.IsUpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_source_disables_the_check_without_reading_anything()
    {
        var reader = new FakeReader(_ => throw new InvalidOperationException("must not be called"));
        (await new UpdateChecker(reader).CheckAsync("  ", "1.0.0")).Status.Should().Be(UpdateStatus.Disabled);
        (await new UpdateChecker(reader).CheckAsync(null, "1.0.0")).Status.Should().Be(UpdateStatus.Disabled);
        reader.LastSource.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"version":"abc"}""")]
    [InlineData("""{"version":""}""")]
    [InlineData("null")]
    public async Task Invalid_manifest_is_a_failure_not_an_exception(string json)
    {
        var r = await With(json).CheckAsync("x.json", "1.0.0");
        r.Status.Should().Be(UpdateStatus.Failed);
        r.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Reader_errors_are_reported_as_failure()
    {
        var checker = new UpdateChecker(new FakeReader(_ => throw new HttpRequestException("rete assente")));
        var r = await checker.CheckAsync("https://example.org/v.json", "1.0.0");
        r.Status.Should().Be(UpdateStatus.Failed);
        r.Message.Should().Contain("rete assente");
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("ms-msdt:/id")]
    public async Task Only_http_urls_are_offered_for_opening(string url)
    {
        var r = await With($$"""{"version":"9.0.0","url":{{System.Text.Json.JsonSerializer.Serialize(url)}}}""").CheckAsync("x.json", "1.0.0");
        r.IsUpdateAvailable.Should().BeTrue();
        r.Url.Should().BeNull();
    }

    [Fact]
    public async Task Default_reader_reads_a_local_or_share_file()
    {
        string path = Path.Combine(_dir, "version.json");
        await File.WriteAllTextAsync(path, """{"version":"3.0.0"}""");
        var r = await new UpdateChecker(new UpdateManifestReader()).CheckAsync(path, "1.0.0");
        r.IsUpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Default_reader_reports_missing_file_and_oversized_file_as_failure()
    {
        var checker = new UpdateChecker(new UpdateManifestReader());
        (await checker.CheckAsync(Path.Combine(_dir, "nope.json"), "1.0.0")).Status.Should().Be(UpdateStatus.Failed);

        string big = Path.Combine(_dir, "big.json");
        await File.WriteAllTextAsync(big, new string(' ', 100_000) + """{"version":"3.0.0"}""");
        (await checker.CheckAsync(big, "1.0.0")).Status.Should().Be(UpdateStatus.Failed);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_into_a_failure_result()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var checker = new UpdateChecker(new FakeReader(_ => throw new OperationCanceledException(cts.Token)));
        var act = () => checker.CheckAsync("x.json", "1.0.0", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("https://intranet/discheck/version.json", true)]
    [InlineData("\\\\server\\software\\DiscCheck\\version.json", true)]
    [InlineData("D:\\share\\version.json", true)]
    [InlineData("ftp://server/version.json", false)]
    [InlineData("version.json", false)]
    [InlineData("file:///x", false)]
    public void Update_source_validation(string source, bool valid)
    {
        var errors = SettingsValidator.Validate(new Settings { UpdateSource = source });
        errors.Any(e => e.Field == "updateSource").Should().Be(!valid);
    }

    [Fact]
    public void Update_source_round_trips_and_old_settings_files_without_it_still_load()
    {
        var s = new Settings { UpdateSource = "https://intranet/v.json" };
        SettingsJson.Deserialize(SettingsJson.Serialize(s)).UpdateSource.Should().Be("https://intranet/v.json");

        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, SettingsJson.Serialize(new Settings()).Replace("\"updateSource\": \"\",", string.Empty)
            .Replace(",\n  \"updateSource\": \"\"", string.Empty).Replace(",\r\n  \"updateSource\": \"\"", string.Empty));
        var store = new SettingsStore(path);
        var load = store.Load();
        load.Errors.Should().BeEmpty();
        load.Settings.UpdateSource.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_update_source_in_file_falls_back_to_disabled_and_is_reported()
    {
        string path = Path.Combine(_dir, "settings2.json");
        File.WriteAllText(path, SettingsJson.Serialize(new Settings()).Replace("\"updateSource\": \"\"", "\"updateSource\": \"ftp://x\""));
        var load = new SettingsStore(path).Load();
        load.Settings.UpdateSource.Should().BeEmpty();
        load.Errors.Should().Contain(e => e.Field == "updateSource");
    }
}
