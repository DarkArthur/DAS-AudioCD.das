namespace DiscCheck.App.Startup;

/// <summary>
/// Attribution text for the graphic assets, required because decision D5 was resolved with option (b), CC-BY 4.0
/// (Requirement 36.6; docs/decisions.md). Shown in the About window and appended by the Exporter to the HTML report.
/// Holder confirmed (see src/DAS.Design/LICENSE-ASSETS.md).
/// </summary>
public static class AssetAttribution
{
    public const string Holder = "DarkArthur DAS";

    public const string DefaultText =
        "Logo e icone DAS.Design © 2026 " + Holder +
        ", licenza CC-BY 4.0 (https://creativecommons.org/licenses/by/4.0/).";

    public static string? CurrentText { get; set; } = DefaultText;
}