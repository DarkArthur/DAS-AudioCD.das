using System.Diagnostics;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.Startup;

/// <summary>Opens an http(s) address in the default browser.</summary>
public interface IUrlOpener
{
    void Open(string url);
}

public sealed class ShellUrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        // Only http(s): the address comes from a file on a share or a server and must never start anything else.
        if (!UpdateChecker.IsHttpUrl(url))
        {
            Log.Warning("Address not opened (not http/https): {Url}", url);
            return;
        }

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warning(ex, "Cannot open {Url}", url); }
    }
}
