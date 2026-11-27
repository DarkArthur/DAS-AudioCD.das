using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;

namespace DAS.Design.Charts.Tests;

/// <summary>
/// Project independence: DAS.Design.Charts must not depend on DiscCheck.*, and DAS.Design must not depend on ScottPlot.
/// </summary>
public class ProjectIndependenceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DiscCheck.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static (List<string> ProjectRefs, List<string> PackageRefs) ReadCsproj(string relativePath)
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), relativePath));
        var projects = doc.Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? "").ToList();
        var packages = doc.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "").ToList();
        return (projects, packages);
    }

    private static bool IsDiscCheck(string reference) =>
        Path.GetFileNameWithoutExtension(reference.Replace('\\', '/').Split('/').Last())
            .StartsWith("DiscCheck", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ChartsCsproj_HasNoDiscCheckReferences()
    {
        var (projects, packages) = ReadCsproj(Path.Combine("src", "DAS.Design.Charts", "DAS.Design.Charts.csproj"));

        projects.Where(IsDiscCheck).Should().BeEmpty();
        packages.Where(p => p.StartsWith("DiscCheck", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact]
    public void ChartsAssembly_HasNoDiscCheckReferences()
    {
        var refs = typeof(DasChartView).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "");

        refs.Where(n => n.StartsWith("DiscCheck", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact]
    public void DesignCsproj_HasNoScottPlotReference()
    {
        var (projects, packages) = ReadCsproj(Path.Combine("src", "DAS.Design", "DAS.Design.csproj"));

        projects.Concat(packages)
            .Where(r => r.Contains("ScottPlot", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact]
    public void DesignAssembly_HasNoScottPlotReference()
    {
        var refs = typeof(DasTheme).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "");

        refs.Where(n => n.StartsWith("ScottPlot", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }
}
