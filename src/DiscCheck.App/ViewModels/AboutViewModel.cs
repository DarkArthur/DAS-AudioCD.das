using System.Reflection;
using DAS.Design;
using DiscCheck.App.Startup;

namespace DiscCheck.App.ViewModels;

/// <summary>Content of the About window (Requirements 32.6, 36.6).</summary>
public sealed class AboutViewModel
{
    public AboutViewModel() : this(CurrentAppVersion(), AssetAttribution.CurrentText)
    {
    }

    public AboutViewModel(string appVersion, string? attribution)
    {
        AppVersion = appVersion;
        Attribution = string.IsNullOrWhiteSpace(attribution) ? null : attribution;
    }

    public string AppName => "DiscCheck";

    public string AppVersion { get; }

    public string AppVersionText => $"Versione applicazione: {AppVersion}";

    public string DesignVersionText => $"Versione DAS.Design: {DasDesignInfo.Version}";

    /// <summary>Attribution of the graphic assets; null when none is required (decision D5).</summary>
    public string? Attribution { get; }

    public bool HasAttribution => Attribution is not null;

    private static string CurrentAppVersion()
    {
        var assembly = typeof(AboutViewModel).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
