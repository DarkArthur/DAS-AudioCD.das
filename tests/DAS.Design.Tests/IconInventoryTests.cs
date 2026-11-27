using System.Security.Cryptography;
using System.Xml.Linq;
using FluentAssertions;

namespace DAS.Design.Tests;

/// <summary>Icon inventory: 15 das.* + 5 disccheck.* keys, SVG rules, .ico sizes, logo location (Requirements 37.3, 37.4, 37.9).</summary>
public class IconInventoryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly int[] IcoSizes = [16, 24, 32, 48, 64, 128, 256];

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DiscCheck.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string DesignIcons => Path.Combine(Repo(), "src", "DAS.Design", "Assets", "Icons");
    private static string AppIcons => Path.Combine(Repo(), "src", "DiscCheck.App", "Assets", "Icons");

    private static List<string> AllSvgs() =>
        Directory.GetFiles(DesignIcons, "*.svg").Concat(Directory.GetFiles(AppIcons, "*.svg")).ToList();

    [Fact]
    public void ExactlyTwentyUniqueKeys_FifteenDasAndFiveDiscCheck()
    {
        var keys = AllSvgs().Select(f => Path.GetFileNameWithoutExtension(f)!).ToList();
        keys.Should().HaveCount(20);
        keys.Should().OnlyHaveUniqueItems();
        keys.Count(k => k.StartsWith("das.", StringComparison.Ordinal)).Should().Be(15);
        keys.Count(k => k.StartsWith("disccheck.", StringComparison.Ordinal)).Should().Be(5);

        var designKeys = Directory.GetFiles(DesignIcons, "*.svg").Select(f => Path.GetFileNameWithoutExtension(f)!);
        designKeys.Should().OnlyContain(k => k.StartsWith("das.", StringComparison.Ordinal));
    }

    [Fact]
    public void EverySvgIsCompliant()
    {
        foreach (var file in AllSvgs())
        {
            var name = Path.GetFileName(file);
            var svg = XDocument.Load(file).Root!;
            svg.Name.LocalName.Should().Be("svg", name);
            ((string?)svg.Attribute("viewBox")).Should().Be("0 0 24 24", name);
            svg.Attribute("width").Should().BeNull(name);
            svg.Attribute("height").Should().BeNull(name);
            ((string?)svg.Attribute("stroke-width")).Should().Be("1.5", name);
            ((string?)svg.Attribute("stroke")).Should().Be("currentColor", name);
            ((string?)svg.Attribute("fill")).Should().Be("none", name);
            File.ReadAllText(file).Should().NotMatchRegex("#[0-9a-fA-F]{3,8}|rgb\\(|<text|<image|gradient|filter", name);
        }
    }

    [Fact]
    public void DesignResourceDictionaryHasNoDiscCheckKeys()
    {
        var design = Path.Combine(Repo(), "src", "DAS.Design");
        var keys = XDocument.Load(Path.Combine(design, "Tokens", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key") ?? "").ToList();
        keys.Should().HaveCount(15);
        keys.Should().OnlyHaveUniqueItems();
        keys.Should().OnlyContain(k => k.StartsWith("Das.Icon.das.", StringComparison.Ordinal));

        Directory.GetFiles(design, "*", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f) && (f.EndsWith(".axaml") || f.EndsWith(".svg") || f.EndsWith(".cs")))
            .Should().OnlyContain(f => !File.ReadAllText(f).Contains("disccheck.", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("das")]
    [InlineData("disccheck")]
    public void IcoContainsSevenSizes(string name)
    {
        var dir = name == "das" ? DesignIcons : AppIcons;
        var bytes = File.ReadAllBytes(Path.Combine(dir, name + ".ico"));
        BitConverter.ToUInt16(bytes, 0).Should().Be(0);
        BitConverter.ToUInt16(bytes, 2).Should().Be(1);
        var count = BitConverter.ToUInt16(bytes, 4);
        count.Should().Be(7);
        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + 16 * i] == 0 ? 256 : bytes[6 + 16 * i]);
        sizes.Should().Equal(IcoSizes);
    }

    [Fact]
    public void NoLogoCopyOutsideDesignProject()
    {
        var repo = Repo();
        var designRoot = Path.Combine(repo, "src", "DAS.Design") + Path.DirectorySeparatorChar;
        var logoDir = Path.Combine(designRoot, "Assets", "Logo");
        var logoHashes = Directory.GetFiles(logoDir, "*.svg")
            .Select(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToHashSet();

        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(f) || f.StartsWith(designRoot, StringComparison.OrdinalIgnoreCase)
                || f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
            {
                continue;
            }
            var file = Path.GetFileName(f);
            var byName = file.StartsWith("das-logo-", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("das-mark-", StringComparison.OrdinalIgnoreCase);
            var byHash = f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                && logoHashes.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
            if (byName || byHash)
            {
                offenders.Add(Path.GetRelativePath(repo, f));
            }
        }
        offenders.Should().BeEmpty();
    }

    private static bool IsBuildOutput(string path)
    {
        var s = Path.DirectorySeparatorChar;
        return path.Contains($"{s}obj{s}") || path.Contains($"{s}bin{s}") || path.Contains($"{s}node_modules{s}");
    }
}
