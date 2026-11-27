using System.Globalization;
using System.Text;
using System.Xml;

namespace DAS.Design.Charts;

/// <summary>
/// PNG and SVG export of a <see cref="DasChartView"/> through ScottPlot 5, with the data, zoom, visible series and
/// Chart_Style of the active theme variant; numbers are written with the invariant culture (Req. 21.2, 21.11, 21.12, 38.9).
/// </summary>
public static class ChartExporter
{
    /// <summary>PNG scale factor.</summary>
    public const int PngScale = 2;
    public const int MinLogicalWidth = 1200;
    public const int MinLogicalHeight = 600;

    /// <summary>Logical export size: the size of the plot area, at least 1.200 × 600.</summary>
    public static (int Width, int Height) LogicalSize(DasChartView view)
    {
        var b = view.InnerPlot.Bounds;
        return (Math.Max(MinLogicalWidth, (int)Math.Ceiling(b.Width)),
                Math.Max(MinLogicalHeight, (int)Math.Ceiling(b.Height)));
    }

    /// <summary>Writes the chart as PNG at 2x scale (≥ 2.400 × 1.200 px). A partial file is deleted on error.</summary>
    public static void ExportPng(DasChartView view, string path)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var (w, h) = LogicalSize(view);
        byte[] bytes;
        using (view.BeginExport())
        {
            var plot = view.InnerPlot.Plot;
            double previousScale = plot.ScaleFactor;
            try
            {
                plot.ScaleFactor = PngScale;
                using var image = plot.GetImage(w * PngScale, h * PngScale);
                bytes = image.GetImageBytes(ScottPlot.ImageFormat.Png);
            }
            finally
            {
                plot.ScaleFactor = previousScale;
            }
        }
        WriteFile(path, bytes);
    }

    /// <summary>Writes the chart as vector SVG (valid XML, texts as text, no raster for the data).</summary>
    public static void ExportSvg(DasChartView view, string path)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var (w, h) = LogicalSize(view);
        string xml;
        using (view.BeginExport())
        {
            var plot = view.InnerPlot.Plot;
            double previousScale = plot.ScaleFactor;
            try
            {
                plot.ScaleFactor = 1;
                xml = plot.GetSvgXml(w, h);
            }
            finally
            {
                plot.ScaleFactor = previousScale;
            }
        }
        // fail before writing if the XML is not well formed
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        WriteFile(path, new UTF8Encoding(false).GetBytes(xml));
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(path, bytes);
        }
        catch
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
