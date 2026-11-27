using System.Xml.Linq;
using FluentAssertions;

namespace DiscCheck.App.Tests;

/// <summary>DiscCheck_Icon_Pack and disccheck.ico (Requirements 32.8-32.10, 34.2, 34.3, 35.7).</summary>
public class IconPackTests
{
    private static readonly string[] Keys =
    [
        "disccheck.scan.pulse", "disccheck.chart.errors", "disccheck.verify.bitperfect",
        "disccheck.compare.curves", "disccheck.archive.degradation",
    ];

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DiscCheck.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    [Fact]
    public void PackHasFiveCompliantSvgsAndMatchingResources()
    {
        var assets = Path.Combine(Repo(), "src", "DiscCheck.App", "Assets");
        Directory.GetFiles(Path.Combine(assets, "Icons"), "*.svg").Select(Path.GetFileNameWithoutExtension)
            .Should().BeEquivalentTo(Keys);

        foreach (var key in Keys)
        {
            var svg = XDocument.Load(Path.Combine(assets, "Icons", key + ".svg")).Root!;
            ((string)svg.Attribute("viewBox")!).Should().Be("0 0 24 24");
            svg.Attribute("width").Should().BeNull();
            ((string)svg.Attribute("stroke")!).Should().Be("currentColor");
            ((string)svg.Attribute("stroke-width")!).Should().Be("1.5");
        }

        var res = XDocument.Load(Path.Combine(assets, "DiscCheckIcons.axaml")).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null).ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value);
        foreach (var key in Keys)
        {
            res.Should().ContainKey("DiscCheck.Icon." + key);
            res.Should().ContainKey("Das.Icon." + key); // lookup key used by DasIcon
            res["Das.Icon." + key].Should().Be(res["DiscCheck.Icon." + key]);
        }
    }

    [Fact]
    public void DasDesignContainsNoDiscCheckIcons()
    {
        var icons = Path.Combine(Repo(), "src", "DAS.Design");
        Directory.GetFiles(icons, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => f.EndsWith(".svg") || f.EndsWith(".axaml"))
            .Should().OnlyContain(f => !Path.GetFileName(f).StartsWith("disccheck.") && !File.ReadAllText(f).Contains("disccheck."));
    }

    [Fact]
    public void IcoHasSevenExpectedSizes()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Repo(), "src", "DiscCheck.App", "Assets", "Icons", "disccheck.ico"));
        BitConverter.ToUInt16(bytes, 2).Should().Be(1);
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + 16 * i] == 0 ? 256 : bytes[6 + 16 * i]);
        sizes.Should().Equal(16, 24, 32, 48, 64, 128, 256);
    }
}
