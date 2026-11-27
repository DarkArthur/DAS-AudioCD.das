using System.Security.Principal;

namespace DiscCheck.App.Startup;

/// <summary>Facts established during startup that the UI must surface (Requirements 1.5, 26.7).</summary>
public sealed record StartupInfo(bool IsAdministrator, bool FileLogAvailable, string? LogDirectory)
{
    /// <summary>Scans and verification are enabled only with administrator privileges (Requirement 26.7).</summary>
    public bool ScansEnabled => IsAdministrator;

    public static bool DetectAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
