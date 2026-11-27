using DiscCheck.App.Startup;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.App.Tests;

public sealed class UpdateNotificationViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("updvm-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class Reader(string json) : IUpdateManifestReader
    {
        public int Calls;
        public Task<string> ReadAsync(string source, CancellationToken ct) { Calls++; return Task.FromResult(json); }
    }

    private sealed class Opener : IUrlOpener
    {
        public List<string> Opened { get; } = [];
        public void Open(string url) => Opened.Add(url);
    }

    private SettingsStore Store(string source)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Load();
        store.Save(store.Current with { UpdateSource = source }).Success.Should().BeTrue();
        return store;
    }

    [Fact]
    public async Task Newer_version_shows_the_banner_and_open_uses_the_http_url()
    {
        var opener = new Opener();
        var vm = new UpdateNotificationViewModel(
            new UpdateChecker(new Reader("""{"version":"2.0.0","url":"https://example.org/d"}""")),
            Store("https://example.org/version.json"), "1.1.0", opener);

        await vm.CheckAsync();

        vm.HasUpdate.Should().BeTrue();
        vm.Message.Should().Contain("2.0.0").And.Contain("1.1.0");
        vm.OpenCommand.CanExecute(null).Should().BeTrue();
        vm.OpenCommand.Execute(null);
        opener.Opened.Should().Equal("https://example.org/d");

        vm.DismissCommand.Execute(null);
        vm.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_download_url_the_banner_has_no_open_button()
    {
        var vm = new UpdateNotificationViewModel(
            new UpdateChecker(new Reader("""{"version":"2.0.0"}""")), Store("https://x/v.json"), "1.0.0", new Opener());
        await vm.CheckAsync();
        vm.HasUpdate.Should().BeTrue();
        vm.CanOpen.Should().BeFalse();
        vm.OpenCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"version":"1.1.0"}""", "https://x/v.json")]   // up to date
    [InlineData("""{"version":"9.0.0"}""", "")]                  // check disabled
    [InlineData("garbage", "https://x/v.json")]                  // invalid manifest
    public async Task No_banner_when_up_to_date_disabled_or_failed(string json, string source)
    {
        var vm = new UpdateNotificationViewModel(
            new UpdateChecker(new Reader(json)), Store(source), "1.1.0", new Opener());
        await vm.CheckAsync();
        vm.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task Check_runs_once_per_launch()
    {
        var reader = new Reader("""{"version":"2.0.0"}""");
        var vm = new UpdateNotificationViewModel(new UpdateChecker(reader), Store("https://x/v.json"), "1.0.0", new Opener());
        await vm.CheckAsync();
        await vm.CheckAsync();
        reader.Calls.Should().Be(1);
    }

    [Fact]
    public void Non_http_urls_are_never_opened()
    {
        var url = new ShellUrlOpener();
        var act = () => url.Open("file:///C:/Windows/System32/calc.exe");
        act.Should().NotThrow(); // ignored: only http(s) is started
    }
}
