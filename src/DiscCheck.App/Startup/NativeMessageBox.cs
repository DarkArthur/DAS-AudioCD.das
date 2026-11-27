using System.Runtime.InteropServices;

namespace DiscCheck.App.Startup;

/// <summary>Minimal error dialog usable before Avalonia is running (startup failures, Requirement 1.9).</summary>
internal static class NativeMessageBox
{
    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hWnd, string text, string caption, uint type);

    public static void ShowError(string caption, string text)
    {
        if (OperatingSystem.IsWindows())
        {
            MessageBox(0, text, caption, MbOk | MbIconError);
        }
    }
}

