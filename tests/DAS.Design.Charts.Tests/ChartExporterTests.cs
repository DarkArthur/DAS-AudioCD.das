using System.Globalization;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using FluentAssertions;

namespace DAS.Design.Charts.Tests;

public class ChartExporterTests
{
    private static DasChartView Prepare(ThemeVariant? variant = null)
    {
        var window = new Window();
        if (variant is not null) window.RequestedThemeVariant = variant;
        var view = new DasChartView { Width = 800, Height = 400 };
        window.Content = view;
        window.Show();
        view.AxisSpec = new ChartAxisSpec("Tempo (mm:ss)", "Errori (n)", "Velocità (x)");
        view.Series = new[]
        {
            new ChartSeries("Scansione",
                Enumerable.Range(0, 120).Select(i => new ChartSample(i, i % 4, i % 30 == 0 ? 1 : 0, 0, 8.5)).ToList(),
                ChartSeriesKind.ScanBars, 0),
        };
        return view;
    }

    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), $"das-chart-{Guid.NewGuid():N}{ext}");

    [AvaloniaFact]
    public void ExportPng_WritesPngAtLeast2400x1200()
    {
        var view = Prepare();
        var path = Temp(".png");
        try
        {
            view.ExportPng(path);
            var bytes = File.ReadAllBytes(path);
            bytes.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            width.Should().BeGreaterThanOrEqualTo(2400);
            height.Should().BeGreaterThanOrEqualTo(1200);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ExportSvg_IsValidXmlWithTextAndNoRasterImages(string variant)
    {
        var view = Prepare(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var path = Temp(".svg");
        try
        {
            view.ExportSvg(path);
            var doc = XDocument.Load(path);
            doc.Root!.Name.LocalName.Should().Be("svg");
            doc.Descendants().Should().NotContain(e => e.Name.LocalName == "image");
            doc.Descendants().Should().Contain(e => e.Name.LocalName == "text" || e.Name.LocalName == "tspan");
            string text = string.Concat(doc.Descendants().Where(e => e.Name.LocalName is "text" or "tspan").Select(e => e.Value));
            text.Should().Contain("Tempo (mm:ss)");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void ExportSvg_UsesInvariantCulture_AndRestoresCurrentCulture()
    {
        var view = Prepare();
        var original = CultureInfo.CurrentCulture;
        var path = Temp(".svg");
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            view.Series = new[]
            {
                new ChartSeries("Scansione",
                    new List<ChartSample> { new(0, 1500.5, 0, 0, 8), new(1, 2500.5, 0, 0, 8) },
                    ChartSeriesKind.ScanBars, 0),
            };
            view.ExportSvg(path);
            CultureInfo.CurrentCulture.Name.Should().Be("it-IT");
            var svg = File.ReadAllText(path);
            svg.Should().NotContain("1.500,5").And.NotContain("1500,5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ExportPng_ToInvalidPath_ThrowsAndLeavesNoFile()
    {
        var view = Prepare();
        var path = Path.Combine(Path.GetTempPath(), $"no-such-dir-{Guid.NewGuid():N}", "x.png");
        var act = () => view.ExportPng(path);
        act.Should().Throw<IOException>();
        File.Exists(path).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Export_EmptyChart_DoesNotThrow()
    {
        var window = new Window();
        var view = new DasChartView { Width = 800, Height = 400 };
        window.Content = view;
        window.Show();
        var path = Temp(".svg");
        try
        {
            view.ExportSvg(path);
            File.Exists(path).Should().BeTrue();
        }
        finally { File.Delete(path); }
    }
}
