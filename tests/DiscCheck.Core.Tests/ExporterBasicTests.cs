using FluentAssertions;
using Xunit;

namespace DiscCheck.Core.Tests;

public sealed class ExporterBasicTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("exporter").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeDialogs(string? path, bool overwrite) : IExportDialogs
    {
        public int PickCalls;
        public Task<string?> PickSavePathAsync(ExportKind kind, string suggestedFileName) { PickCalls++; return Task.FromResult(path); }
        public Task<bool> ConfirmOverwriteAsync(string p) => Task.FromResult(overwrite);
    }

    private static readonly ScanSample[] Samples = [new(0, 0, 1, 2, 3, 4.0), new(1, 1, null, 0, null, null)];

    [Fact]
    public async Task NoSamples_CreatesNoFileAndAsksNothing()
    {
        string path = Path.Combine(_dir, "a.csv");
        var dialogs = new FakeDialogs(path, true);
        var r = await new Exporter(dialogs).ExportCsvAsync([], "a.csv");
        r.Outcome.Should().Be(ExportOutcome.NoData);
        dialogs.PickCalls.Should().Be(0);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task ExistingFile_DeclinedOverwrite_IsUntouched()
    {
        string path = Path.Combine(_dir, "a.csv");
        File.WriteAllText(path, "old");
        var r = await new Exporter(new FakeDialogs(path, false)).ExportCsvAsync(Samples, "a.csv");
        r.Outcome.Should().Be(ExportOutcome.Declined);
        File.ReadAllText(path).Should().Be("old");
    }

    [Fact]
    public async Task Csv_Success_ReportsFullPath()
    {
        string path = Path.Combine(_dir, "a.csv");
        var r = await new Exporter(new FakeDialogs(path, true)).ExportCsvAsync(Samples, "a.csv");
        r.IsSuccess.Should().BeTrue();
        r.Message.Should().Contain(Path.GetFullPath(path));
        File.ReadAllText(path).Should().StartWith(CsvExporter.Header);
    }

    [Fact]
    public async Task FailedWrite_LeavesNoPartialFile()
    {
        string path = Path.Combine(_dir, "a.png");
        var r = await new Exporter(new FakeDialogs(path, true)).ExportPngAsync(Samples, new ThrowingChart(), "a.png");
        r.Outcome.Should().Be(ExportOutcome.Failed);
        Directory.GetFiles(_dir).Should().BeEmpty();
    }

    private sealed class ThrowingChart : IChartImageSource
    {
        public void ExportPng(string path) { File.WriteAllText(path, "partial"); throw new IOException("disco pieno"); }
        public void ExportSvg(string path) => throw new IOException();
    }

    [Fact]
    public void Html_IsSelfContained_AndHasVerification()
    {
        var disc = new DiscRecord(1, "Disco <A>", null, null, null, null, null, "hash", 3, 1000, null, null);
        var drive = new DriveRecord(1, "V", "M", "F", ReadOffset: 6);
        var scan = new ScanRecord(1, 1, 1, DateTimeOffset.Now, 8, ScannerKind.StandardC2, Verdict.OK, null, null, null, 2, 2, null);
        var ver = new ReportVerification(DateTimeOffset.Now, VerificationResult.Identical, 6, 12,
            [new VerificationTrackRecord(1, true, 0, null, "00000000", "x", 40, null)]);
        string html = HtmlReportBuilder.Build(new HtmlReportData(disc, drive, scan, Samples, ver), [1, 2, 3]);
        html.Should().Contain("data:image/png;base64,").And.Contain("Combined_Offset").And.Contain("Disco &lt;A&gt;");
        html.Should().NotContain("http://").And.NotContain("https://").And.NotContain("<link").And.NotContain("<script");
    }
}
