using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DAS.Design.Controls;
using DiscCheck.Core;
using FluentAssertions;

namespace DAS.Design.Tests;

public class DesignTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DiscCheck.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string DesignDir => Path.Combine(RepoRoot(), "src", "DAS.Design");

    private static Dictionary<string, string> ReadColors(string variant)
    {
        var doc = XDocument.Load(Path.Combine(DesignDir, "Tokens", $"Colors.{variant}.axaml"));
        return doc.Root!.Elements().Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value.Trim());
    }

    private static (byte R, byte G, byte B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        return (byte.Parse(h[0..2], NumberStyles.HexNumber), byte.Parse(h[2..4], NumberStyles.HexNumber), byte.Parse(h[4..6], NumberStyles.HexNumber));
    }

    [Fact]
    public void LightAndDarkTokenKeysAreIdentical()
    {
        var light = ReadColors("Light").Keys.ToHashSet();
        var dark = ReadColors("Dark").Keys.ToHashSet();
        light.Should().NotBeEmpty();
        light.SetEquals(dark).Should().BeTrue("missing in Dark: {0}; missing in Light: {1}",
            string.Join(", ", light.Except(dark)), string.Join(", ", dark.Except(light)));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ColorPairsMeetContrastRatios(string variant)
    {
        var c = ReadColors(variant);
        string[] k = ["Text|Background", "Text|Surface", "Text|SurfaceAlt", "TextSecondary|Background", "TextSecondary|Surface",
            "OnPrimary|Primary", "OnAccent|Accent", "Primary|Background", "Primary|Surface", "Accent|Background", "Accent|Surface",
            "Status.Ok|Status.OkSurface", "Status.Warn|Status.WarnSurface", "Status.Ko|Status.KoSurface"];
        var text = k.Select(p => p.Split('|'));
        var graphic = new[] { "Chart.C1", "Chart.C2", "Chart.CU", "Chart.Speed" }
            .SelectMany(g => new[] { new[] { g, "Background" }, new[] { g, "Surface" } });

        var failures = new List<string>();
        void Check(IEnumerable<string[]> pairs, double min)
        {
            foreach (var p in pairs)
            {
                var ratio = ContrastCalculator.ContrastRatio(Rgb(c[$"Das.Color.{p[0]}"]), Rgb(c[$"Das.Color.{p[1]}"]));
                if (ratio < min)
                {
                    failures.Add($"{variant}: {p[0]} on {p[1]} = {ratio:F2} (< {min})");
                }
            }
        }
        Check(text, 4.5);
        Check(graphic, 3.0);
        failures.Should().BeEmpty();
    }

    [Fact]
    public void DesignProjectHasNoDiscCheckReferences()
    {
        var csproj = XDocument.Load(Path.Combine(DesignDir, "DAS.Design.csproj"));
        var refs = csproj.Descendants().Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "");
        refs.Should().NotContain(r => r.Contains("DiscCheck", StringComparison.OrdinalIgnoreCase));

        typeof(DasTheme).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "").Should().NotContain(n => n.StartsWith("DiscCheck", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void DasThemeLoadsHeadless()
    {
        Application.Current!.Styles.OfType<DasTheme>().Should().HaveCount(1);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void IconAndBadgeInstantiateInBothVariants(string variant)
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var panel = new StackPanel();
        var icon = new DasIcon { IconName = "das.status.ok", Size = 16 };
        var badges = Enum.GetValues<DasStatus>().Select(s => new DasStatusBadge { Status = s }).ToList();
        panel.Children.Add(icon);
        foreach (var b in badges)
        {
            panel.Children.Add(b);
        }
        var window = new Window { RequestedThemeVariant = theme, Content = panel };
        window.Show();

        window.ActualThemeVariant.Should().Be(theme);
        icon.Size.Should().Be(16);
        badges.Select(b => b.DisplayText).Should().Equal("OK", "ATTENZIONE", "KO");
        window.Close();
    }

    private static readonly string[] IconKeys =
    [
        "das.drive.optical", "das.disc.cd", "das.history", "das.settings", "das.export.csv", "das.export.png",
        "das.export.html", "das.eject", "das.reset", "das.status.warning", "das.status.ok", "das.status.ko",
        "das.reminder.bell", "das.theme.light", "das.theme.dark",
    ];

    [Fact]
    public void IconSetHasFifteenCompliantSvgFilesAndResources()
    {
        var dir = Path.Combine(DesignDir, "Assets", "Icons");
        var files = Directory.GetFiles(dir, "*.svg").Select(Path.GetFileNameWithoutExtension).ToList();
        files.Should().BeEquivalentTo(IconKeys);

        foreach (var key in IconKeys)
        {
            var svg = XDocument.Load(Path.Combine(dir, key + ".svg")).Root!;
            ((string)svg.Attribute("viewBox")!).Should().Be("0 0 24 24");
            svg.Attribute("width").Should().BeNull();
            svg.Attribute("height").Should().BeNull();
            ((string)svg.Attribute("stroke-width")!).Should().Be("1.5");
            ((string)svg.Attribute("stroke")!).Should().Be("currentColor");
            ((string)svg.Attribute("fill")!).Should().Be("none");
            File.ReadAllText(Path.Combine(dir, key + ".svg")).Should().NotMatchRegex("#[0-9a-fA-F]{3,8}|rgb\\(|<text|<image|gradient|filter");
        }

        var res = XDocument.Load(Path.Combine(DesignDir, "Tokens", "Icons.axaml")).Root!.Elements()
            .Select(e => (string)e.Attribute(X + "Key")!).ToList();
        res.Should().BeEquivalentTo(IconKeys.Select(k => "Das.Icon." + k));
    }

    [AvaloniaFact]
    public void IconResourcesResolveAsGeometry()
    {
        var window = new Window();
        window.Show();
        foreach (var key in IconKeys)
        {
            window.TryFindResource("Das.Icon." + key, window.ActualThemeVariant, out var res).Should().BeTrue(key);
            res.Should().BeAssignableTo<Avalonia.Media.Geometry>(key);
        }
        window.Close();
    }

    [Fact]
    public void LogoFilesMatchSha256Manifest()
    {
        var logoDir = Path.Combine(DesignDir, "Assets", "Logo");
        var lines = File.ReadAllLines(Path.Combine(logoDir, "logo.sha256")).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        lines.Should().NotBeEmpty();
        foreach (var line in lines)
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var file = Path.Combine(DesignDir, "Assets", parts[1].Trim().Replace('/', Path.DirectorySeparatorChar));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            hash.Should().Be(parts[0].ToLowerInvariant(), "hash of {0}", parts[1]);
        }
    }

    [Fact]
    public void VersionEqualsLatestChangelogEntry()
    {
        var changelog = File.ReadAllText(Path.Combine(DesignDir, "CHANGELOG.md"));
        var m = Regex.Match(changelog, @"^## \[(\d+\.\d+\.\d+[^\]]*)\]", RegexOptions.Multiline);
        m.Success.Should().BeTrue();
        DasDesignInfo.Version.Should().Be(m.Groups[1].Value);
    }
}
