using System.Text.RegularExpressions;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

/// <summary>
/// Static analysis of the Views and ViewModels of DiscCheck.App (.axaml and .cs): every chart must go
/// through <c>DasChartView</c>, never through <c>AvaPlot</c> or <c>ScottPlot.Plot</c> (Requirements 37.12, 38.15).
/// </summary>
public static partial class ChartViewUsageAnalyzer
{
    /// <summary>The views that must show a chart: Scansione, Storico, Confronto, Archivio/Degrado.</summary>
    public static readonly string[] ChartViews =
        ["ScanView.axaml", "HistoryView.axaml", "ComparisonView.axaml", "DegradationView.axaml"];

    /// <summary>Source files of the App to analyze: *.axaml and *.cs of Views and ViewModels.</summary>
    public static IEnumerable<string> SourceFiles(string appDir) =>
        new[] { "Views", "ViewModels" }
            .Select(d => Path.Combine(appDir, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>Blanks comments (XML for .axaml, // and /* */ for .cs), keeping newlines so line numbers stay valid.</summary>
    public static string StripComments(string file, string content)
    {
        static string Blank(string s) => Regex.Replace(s, @"[^\r\n]", " ");
        if (file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            return XmlCommentRegex().Replace(content, m => Blank(m.Value));
        // String literals are matched first (and blanked) so that "//" or "new AvaPlot()" inside a string is ignored.
        return CsTokenRegex().Replace(content, m => Blank(m.Value));
    }

    /// <summary>Reports each place where a view or view model instantiates AvaPlot or ScottPlot.Plot.</summary>
    public static IEnumerable<AxamlAnalyzer.Finding> FindDirectPlotUsage(string file, string content)
    {
        var text = StripComments(file, content);
        var isAxaml = file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);

        if (isAxaml)
        {
            foreach (Match m in AxamlPlotElementRegex().Matches(text))
                yield return new(file, AxamlAnalyzer.LineOf(text, m.Index), m.Value.TrimStart('<'), "AvaPlot/ScottPlot.Plot element: use DasChartView");
            yield break;
        }

        foreach (Match m in NewPlotRegex().Matches(text))
            yield return new(file, AxamlAnalyzer.LineOf(text, m.Index), "new " + m.Groups[1].Value, "direct AvaPlot/ScottPlot.Plot instantiation: use DasChartView");

        foreach (Match m in TargetTypedNewRegex().Matches(text))
            yield return new(file, AxamlAnalyzer.LineOf(text, m.Index), m.Groups[1].Value, "target-typed AvaPlot/ScottPlot.Plot instantiation: use DasChartView");

        // "new Plot(" is ambiguous without the namespace, so it counts only where ScottPlot is imported.
        if (UsingScottPlotRegex().IsMatch(text))
            foreach (Match m in NewBarePlotRegex().Matches(text))
                yield return new(file, AxamlAnalyzer.LineOf(text, m.Index), "new Plot", "direct ScottPlot.Plot instantiation: use DasChartView");
    }

    public static bool ContainsDasChartView(string file, string content) =>
        DasChartViewElementRegex().IsMatch(StripComments(file, content));

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlCommentRegex();

    [GeneratedRegex(@"""(?:\\.|[^""\\\r\n])*""|//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CsTokenRegex();

    [GeneratedRegex(@"<(?:[\w.]+:)?(?:AvaPlot|Plot)\b")]
    private static partial Regex AxamlPlotElementRegex();

    [GeneratedRegex(@"\bnew\s+((?:ScottPlot\s*\.\s*)?AvaPlot|ScottPlot\s*\.\s*Plot)\b")]
    private static partial Regex NewPlotRegex();

    [GeneratedRegex(@"\b((?:ScottPlot\s*\.\s*)?AvaPlot|ScottPlot\s*\.\s*Plot)\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex TargetTypedNewRegex();

    [GeneratedRegex(@"\busing\s+ScottPlot\b")]
    private static partial Regex UsingScottPlotRegex();

    [GeneratedRegex(@"\bnew\s+Plot\s*\(")]
    private static partial Regex NewBarePlotRegex();

    [GeneratedRegex(@"<(?:[\w.]+:)?DasChartView\b")]
    private static partial Regex DasChartViewElementRegex();
}

public class ChartViewUsageStaticAnalysisTests
{
    private static readonly string Root = AxamlAnalyzer.FindRepoRoot();
    private static string AppDir => Path.Combine(Root, "src", "DiscCheck.App");

    private static string Rel(string file) => Path.GetRelativePath(Root, file);

    // Requirements 37.12, 38.15
    [Fact]
    public void App_views_and_viewmodels_never_instantiate_AvaPlot_or_ScottPlot_Plot()
    {
        var files = ChartViewUsageAnalyzer.SourceFiles(AppDir).ToList();
        files.Should().NotBeEmpty("the Views/ViewModels sources must be found");

        var findings = files
            .SelectMany(f => ChartViewUsageAnalyzer.FindDirectPlotUsage(Rel(f), File.ReadAllText(f)))
            .ToList();

        findings.Should().BeEmpty("charts must go through DasChartView; found:{0}", AxamlAnalyzer.Format(findings));
    }

    // Requirements 37.12, 38.15
    [Fact]
    public void Scan_History_Comparison_and_Degradation_views_contain_a_DasChartView()
    {
        var viewsDir = Path.Combine(AppDir, "Views");
        var missing = ChartViewUsageAnalyzer.ChartViews
            .Select(v => Path.Combine(viewsDir, v))
            .Select(f => File.Exists(f)
                ? (ChartViewUsageAnalyzer.ContainsDasChartView(f, File.ReadAllText(f))
                    ? null
                    : new AxamlAnalyzer.Finding(Rel(f), 1, "DasChartView", "view has no DasChartView"))
                : new AxamlAnalyzer.Finding(Rel(f), 0, "DasChartView", "view file not found"))
            .OfType<AxamlAnalyzer.Finding>()
            .ToList();

        missing.Should().BeEmpty("each chart view must host a DasChartView; found:{0}", AxamlAnalyzer.Format(missing));
    }

    // Proof that the analyzer fails on offending sources (synthetic, in memory).
    [Fact]
    public void Direct_plot_instantiation_in_cs_is_reported_with_file_and_line()
    {
        const string cs =
            "using ScottPlot;\n" +                                   // 1
            "// new AvaPlot() in a comment is ignored\n" +           // 2
            "class V\n{\n" +                                         // 3-4
            "  var a = new AvaPlot();\n" +                           // 5
            "  var b = new ScottPlot.Plot();\n" +                    // 6
            "  AvaPlot c = new();\n" +                               // 7
            "  var d = new Plot();\n" +                              // 8
            "  var s = \"new AvaPlot()\";\n" +                       // 9 (string, ignored)
            "}\n";
        var findings = ChartViewUsageAnalyzer.FindDirectPlotUsage("Views/V.axaml.cs", cs).ToList();

        findings.Select(f => f.Line).Should().Equal(5, 6, 7, 8);
        findings.Should().OnlyContain(f => f.File == "Views/V.axaml.cs");
    }

    [Fact]
    public void AvaPlot_element_in_axaml_is_reported_with_file_and_line()
    {
        const string xaml = "<UserControl>\n  <!-- <sp:AvaPlot /> -->\n  <sp:AvaPlot x:Name=\"P\" />\n</UserControl>";
        var findings = ChartViewUsageAnalyzer.FindDirectPlotUsage("Views/V.axaml", xaml).ToList();

        findings.Should().ContainSingle();
        findings[0].File.Should().Be("Views/V.axaml");
        findings[0].Line.Should().Be(3);
    }

    [Fact]
    public void View_without_DasChartView_is_detected_and_one_with_it_is_accepted()
    {
        const string without = "<UserControl>\n  <!-- <charts:DasChartView /> -->\n  <TextBlock />\n</UserControl>";
        const string with = "<UserControl>\n  <charts:DasChartView MinHeight=\"280\" />\n</UserControl>";

        ChartViewUsageAnalyzer.ContainsDasChartView("Views/V.axaml", without).Should().BeFalse();
        ChartViewUsageAnalyzer.ContainsDasChartView("Views/V.axaml", with).Should().BeTrue();
    }

    [Fact]
    public void Clean_sources_produce_no_findings()
    {
        const string cs = "class V { void M() { var x = new System.Text.StringBuilder(); } }";
        ChartViewUsageAnalyzer.FindDirectPlotUsage("V.cs", cs).Should().BeEmpty();
    }
}
