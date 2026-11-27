using System.Globalization;
using FluentAssertions;
using Xunit;

namespace DiscCheck.Core.Tests;

/// <summary>Exporter tests: overwrite, failed write, no data, self-contained HTML. Validates: Requirements 21.6, 21.7, 21.8.</summary>
public sealed class ExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("exporter-full").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class Dialogs(string? path, bool overwrite) : IExportDialogs
    {
        public int PickCalls;
        public int ConfirmCalls;
        public Task<string?> PickSavePathAsync(ExportKind kind, string suggestedFileName) { PickCalls++; return Task.FromResult(path); }
        public Task<bool> ConfirmOverwriteAsync(string p) { ConfirmCalls++; return Task.FromResult(overwrite); }
    }

    private sealed class FakeChart(byte[]? png = null) : IChartImageSource
    {
        public void ExportPng(string path) => File.WriteAllBytes(path, png ?? [0x89, 0x50, 0x4E, 0x47]);
        public void ExportSvg(string path) => File.WriteAllText(path, "<svg/>");
    }

    private static readonly ScanSample[] Samples = [new(0, 0, 1, 2, 3, 4.0), new(1, 1, null, 1, null, null)];

    [Fact]
    public async Task ExistingFile_ConfirmedOverwrite_ReplacesContentAndLeavesNoTemp()
    {
        string path = Path.Combine(_dir, "a.svg");
        File.WriteAllText(path, "old");
        var dialogs = new Dialogs(path, true);
        var r = await new Exporter(dialogs).ExportSvgAsync(Samples, new FakeChart(), "a.svg");
        r.IsSuccess.Should().BeTrue();
        dialogs.ConfirmCalls.Should().Be(1);
        File.ReadAllText(path).Should().Be("<svg/>");
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task NewFile_DoesNotAskOverwriteConfirmation()
    {
        var dialogs = new Dialogs(Path.Combine(_dir, "n.csv"), false);
        var r = await new Exporter(dialogs).ExportCsvAsync(Samples, "n.csv");
        r.IsSuccess.Should().BeTrue();
        dialogs.ConfirmCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExistingFile_DeclinedOverwrite_NeverInvokesWriterAndLeavesNoTemp()
    {
        string path = Path.Combine(_dir, "a.png");
        File.WriteAllText(path, "old");
        var r = await new Exporter(new Dialogs(path, false)).ExportPngAsync(Samples, new FakeChart(), "a.png");
        r.Outcome.Should().Be(ExportOutcome.Declined);
        File.ReadAllText(path).Should().Be("old");
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelled_ProducesNoFile()
    {
        var r = await new Exporter(new Dialogs(null, true)).ExportCsvAsync(Samples, "a.csv");
        r.Outcome.Should().Be(ExportOutcome.Cancelled);
        Directory.GetFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task MissingDirectory_FailsWithMessageContainingPath_AndCreatesNothing()
    {
        string path = Path.Combine(_dir, "missing", "a.csv");
        var r = await new Exporter(new Dialogs(path, true)).ExportCsvAsync(Samples, "a.csv");
        r.Outcome.Should().Be(ExportOutcome.Failed);
        r.Message.Should().Contain(path);
        Directory.Exists(Path.Combine(_dir, "missing")).Should().BeFalse();
        Directory.GetFileSystemEntries(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task LockedTarget_Fails_OriginalSurvives_NoTempLeft()
    {
        string path = Path.Combine(_dir, "locked.csv");
        File.WriteAllText(path, "old");
        ExportResult r;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            r = await new Exporter(new Dialogs(path, true)).ExportCsvAsync(Samples, "locked.csv");
        r.Outcome.Should().Be(ExportOutcome.Failed);
        File.ReadAllText(path).Should().Be("old");
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task Html_FailedChart_LeavesNoPartialHtmlOrImageTemp()
    {
        string path = Path.Combine(_dir, "r.html");
        var r = await new Exporter(new Dialogs(path, true)).ExportHtmlAsync(Data(), new ThrowingChart(), "r.html");
        r.Outcome.Should().Be(ExportOutcome.Failed);
        Directory.GetFiles(_dir).Should().BeEmpty();
    }

    private sealed class ThrowingChart : IChartImageSource
    {
        public void ExportPng(string path) { File.WriteAllText(path, "partial"); throw new IOException("boom"); }
        public void ExportSvg(string path) => throw new IOException("boom");
    }

    [Fact]
    public async Task NoSamples_AllKinds_ReturnNoDataWithoutDialogsOrFiles()
    {
        var dialogs = new Dialogs(Path.Combine(_dir, "x"), true);
        var exporter = new Exporter(dialogs);
        var chart = new FakeChart();
        var results = new[]
        {
            await exporter.ExportCsvAsync([], "x"),
            await exporter.ExportPngAsync([], chart, "x"),
            await exporter.ExportSvgAsync([], chart, "x"),
            await exporter.ExportHtmlAsync(Data() with { Samples = [] }, chart, "x"),
        };
        results.Should().OnlyContain(r => r.Outcome == ExportOutcome.NoData && r.Message == Exporter.NoDataMessage);
        dialogs.PickCalls.Should().Be(0);
        dialogs.ConfirmCalls.Should().Be(0);
        Directory.GetFileSystemEntries(_dir).Should().BeEmpty();
    }

    private static HtmlReportData Data(string label = "Disco", string? attribution = null)
    {
        var disc = new DiscRecord(1, label, null, null, null, null, null, "hash", 3, 1000, null, null);
        var drive = new DriveRecord(1, "V", "M", "F", ReadOffset: 6);
        var scan = new ScanRecord(1, 1, 1, DateTimeOffset.Now, 8.5, ScannerKind.StandardC2, Verdict.OK, null, null, null, 2, 2, null);
        var ver = new ReportVerification(DateTimeOffset.Now, VerificationResult.Identical, 6, 12,
            [new VerificationTrackRecord(1, true, 0, null, "00000000", "x", 40, null)]);
        return new HtmlReportData(disc, drive, scan, Samples, ver, attribution);
    }

    [Fact]
    public async Task Html_Export_WritesSelfContainedFileWithEmbeddedImage()
    {
        string path = Path.Combine(_dir, "r.html");
        var r = await new Exporter(new Dialogs(path, true)).ExportHtmlAsync(Data(), new FakeChart(), "r.html");
        r.IsSuccess.Should().BeTrue();
        string html = File.ReadAllText(path);
        html.Should().Contain("data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]));
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public void Html_HasNoExternalReferences_AndInlineCss()
    {
        string html = HtmlReportBuilder.Build(Data(), [1, 2, 3]);
        html.Should().Contain("<style>");
        System.Text.RegularExpressions.Regex.IsMatch(html, "(src|href)\\s*=\\s*[\"']?\\s*(https?:)?//", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Should().BeFalse();
        html.Should().NotContain("<link").And.NotContain("<script").And.NotContain("@import").And.NotContain("url(http");
        System.Text.RegularExpressions.Regex.Matches(html, "src=\"([^\"]*)\"").Select(m => m.Groups[1].Value)
            .Should().OnlyContain(s => s.StartsWith("data:image/png;base64,"));
    }

    [Fact]
    public void Html_EncodesTextFromDiscAndAttribution()
    {
        string html = HtmlReportBuilder.Build(Data("<b>\"x\" & y</b>", "© <i>Autore</i>"), null);
        html.Should().NotContain("<b>").And.NotContain("<i>");
        html.Should().Contain("&lt;b&gt;").And.Contain("&amp;").And.Contain("&lt;i&gt;Autore&lt;/i&gt;");
        html.Should().Contain("Grafico non disponibile");
    }

    [Fact]
    public void Html_NumbersUseInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            string html = HtmlReportBuilder.Build(Data(), null);
            html.Should().Contain("8.5x").And.Contain("1.50");
            html.Should().NotContain("8,5x").And.NotContain("1,50");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Html_VerificationSection_ShowsOffsetsAndExcludedSamples()
    {
        string html = HtmlReportBuilder.Build(Data(), null);
        html.Should().Contain("<h2>Verifica</h2>").And.Contain("Read_Offset").And.Contain("Combined_Offset")
            .And.Contain("Campioni di bordo esclusi").And.Contain("<td>12</td>").And.Contain("<td>40</td>");
        html.Should().Contain("<h2>Disco</h2>").And.Contain("<h2>Drive</h2>").And.Contain("<h2>Verdetto</h2>")
            .And.Contain("<h2>Riepilogo</h2>").And.Contain("<h2>Grafico</h2>");
    }

    [Fact]
    public void Html_WithoutVerification_OmitsVerificationSection()
    {
        string html = HtmlReportBuilder.Build(Data() with { Verification = null }, null);
        html.Should().NotContain("<h2>Verifica</h2>").And.NotContain("Combined_Offset");
    }
}
