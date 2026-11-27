using System.Text.RegularExpressions;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

/// <summary>
/// Static analysis of the .axaml files in src/** (Requirements 35.2, 35.3, 37.1, 37.2).
/// </summary>
public static partial class AxamlAnalyzer
{
    public sealed record Finding(string File, int Line, string Key, string Message)
    {
        public override string ToString() => $"{File}:{Line}: [{Key}] {Message}";
    }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DiscCheck.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("DiscCheck.sln not found above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<string> AxamlFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => s is "bin" or "obj"))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>Replaces XML comments with spaces, preserving newlines so line numbers stay valid.</summary>
    public static string StripComments(string content) =>
        CommentRegex().Replace(content, m => Regex.Replace(m.Value, @"[^\r\n]", " "));

    public static int LineOf(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    public static IEnumerable<string> DefinedKeys(string content) =>
        KeyDefRegex().Matches(StripComments(content)).Select(m => m.Groups[1].Value);

    public static IEnumerable<Finding> FindUnknownTokenReferences(string file, string content, ISet<string> definedKeys)
    {
        var text = StripComments(content);
        foreach (Match m in ResourceRefRegex().Matches(text))
        {
            var key = m.Groups[2].Value;
            if ((key.StartsWith("Das.", StringComparison.Ordinal) || key.StartsWith("DiscCheck.", StringComparison.Ordinal))
                && !definedKeys.Contains(key))
                yield return new Finding(file, LineOf(text, m.Index), key, "Token_Key not defined in DAS.Design or Accent_Override");
        }
    }

    public static IEnumerable<Finding> FindLiteralStyleValues(string file, string content)
    {
        var text = StripComments(content);

        foreach (Match m in HexColorRegex().Matches(text))
            yield return new Finding(file, LineOf(text, m.Index), m.Groups[1].Value, "literal color");

        foreach (Match m in SolidColorBrushRegex().Matches(text))
            yield return new Finding(file, LineOf(text, m.Index), "SolidColorBrush", "literal SolidColorBrush");

        foreach (Match m in ColorAttrRegex().Matches(text))
            yield return new Finding(file, LineOf(text, m.Index), m.Groups[1].Value, "literal Color value");

        foreach (Match m in BrushAttrRegex().Matches(text))
            yield return new Finding(file, LineOf(text, m.Index), m.Groups[2].Value, $"literal color name in {m.Groups[1].Value}");

        foreach (Match m in FontFamilyRegex().Matches(text))
            yield return new Finding(file, LineOf(text, m.Index), m.Groups[1].Value, "literal FontFamily");
    }

    public static string Format(IEnumerable<Finding> findings) =>
        Environment.NewLine + string.Join(Environment.NewLine, findings.Select(f => f.ToString()));

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"x:Key\s*=\s*""([^""]+)""")]
    private static partial Regex KeyDefRegex();

    [GeneratedRegex(@"\{\s*(DynamicResource|StaticResource)\s+([^\s}]+)\s*\}")]
    private static partial Regex ResourceRefRegex();

    [GeneratedRegex(@"=\s*""(#[0-9A-Fa-f]{3,8})""")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"<SolidColorBrush\b")]
    private static partial Regex SolidColorBrushRegex();

    [GeneratedRegex(@"(?<![\w.])Color\s*=\s*""([^""{][^""]*)""")]
    private static partial Regex ColorAttrRegex();

    [GeneratedRegex(@"(?<![\w.])(Background|Foreground|BorderBrush|Fill|Stroke)\s*=\s*""([^""{][^""]*)""")]
    private static partial Regex BrushAttrRegex();

    [GeneratedRegex(@"(?<![\w.])FontFamily\s*=\s*""([^""{][^""]*)""")]
    private static partial Regex FontFamilyRegex();
}

public class AxamlStaticAnalysisTests
{
    private static readonly string Root = AxamlAnalyzer.FindRepoRoot();
    private static string Src => Path.Combine(Root, "src");
    private static string AppDir => Path.Combine(Src, "DiscCheck.App");

    private static HashSet<string> DefinedKeysInRepo()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        // DAS.Design token files plus the Accent_Override declared in the App's own .axaml.
        foreach (var dir in new[] { Path.Combine(Src, "DAS.Design"), AppDir })
            foreach (var file in AxamlAnalyzer.AxamlFiles(dir))
                foreach (var key in AxamlAnalyzer.DefinedKeys(File.ReadAllText(file)))
                    keys.Add(key);
        return keys;
    }

    private static string Rel(string file) => Path.GetRelativePath(Root, file);

    [Fact]
    public void Token_definitions_are_found_in_DAS_Design()
    {
        var keys = DefinedKeysInRepo();
        keys.Should().Contain("Das.Brush.Background");
        keys.Should().Contain("Das.Color.Series.1");
    }

    // Requirement 37.1, 35.3
    [Fact]
    public void Every_referenced_Das_or_DiscCheck_token_key_exists()
    {
        var defined = DefinedKeysInRepo();
        var findings = AxamlAnalyzer.AxamlFiles(Src)
            .SelectMany(f => AxamlAnalyzer.FindUnknownTokenReferences(Rel(f), File.ReadAllText(f), defined))
            .ToList();

        findings.Should().BeEmpty("every Token_Key must exist; found:{0}", AxamlAnalyzer.Format(findings));
    }

    // Requirement 37.2, 35.2
    [Fact]
    public void App_axaml_files_contain_no_literal_colors_brushes_or_fonts()
    {
        var findings = AxamlAnalyzer.AxamlFiles(AppDir)
            .SelectMany(f => AxamlAnalyzer.FindLiteralStyleValues(Rel(f), File.ReadAllText(f)))
            .ToList();

        findings.Should().BeEmpty("literal style values are forbidden; found:{0}", AxamlAnalyzer.Format(findings));
    }

    [Fact]
    public void Unknown_token_is_reported_with_file_line_and_key()
    {
        const string xaml = "<Window>\n  <!-- {DynamicResource Das.Commented} -->\n  <TextBlock Foreground=\"{DynamicResource Das.Missing.Key}\" />\n</Window>";
        var findings = AxamlAnalyzer.FindUnknownTokenReferences("v.axaml", xaml, new HashSet<string> { "Das.Other" }).ToList();

        findings.Should().ContainSingle();
        findings[0].File.Should().Be("v.axaml");
        findings[0].Line.Should().Be(3);
        findings[0].Key.Should().Be("Das.Missing.Key");
    }

    [Fact]
    public void Literal_values_are_detected_with_file_and_line()
    {
        const string xaml = "<Border\n  Background=\"#FF0000\">\n  <SolidColorBrush Color=\"Red\" />\n  <TextBlock FontFamily=\"Arial\" Foreground=\"Blue\" />\n  <TextBlock FontFamily=\"{DynamicResource Das.Font.Primary}\" Foreground=\"{DynamicResource Das.Brush.Text}\" />\n</Border>";
        var findings = AxamlAnalyzer.FindLiteralStyleValues("v.axaml", xaml).ToList();

        findings.Select(f => f.Message).Should().Contain(new[]
        {
            "literal color", "literal SolidColorBrush", "literal Color value", "literal FontFamily", "literal color name in Foreground",
        });
        findings.Should().OnlyContain(f => f.File == "v.axaml" && f.Line >= 2 && f.Line <= 4);
        findings.Should().NotContain(f => f.Line == 5);
    }
}
