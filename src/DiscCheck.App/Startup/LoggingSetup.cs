using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DiscCheck.App.Startup;

/// <summary>Controls the minimum log level at runtime: Information, or Verbose while Modalità_Debug is on.</summary>
public sealed class LogLevelController(LoggingLevelSwitch levelSwitch)
{
    public bool DebugMode => levelSwitch.MinimumLevel == LogEventLevel.Verbose;

    public void SetDebugMode(bool enabled) =>
        levelSwitch.MinimumLevel = enabled ? LogEventLevel.Verbose : LogEventLevel.Information;
}

public static class LoggingSetup
{
    public static string DefaultLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscCheck", "logs");

    /// <summary>
    /// Builds the Serilog logger writing to console and file. If the file sink cannot be initialised the
    /// logger writes to console only and <paramref name="fileLogAvailable"/> is false (Requirement 1.5).
    /// </summary>
    public static Logger Create(LoggingLevelSwitch levelSwitch, string logDirectory, out bool fileLogAvailable)
    {
        const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(outputTemplate: template);

        fileLogAvailable = ProbeWritable(logDirectory);
        if (fileLogAvailable)
        {
            config.WriteTo.File(
                Path.Combine(logDirectory, "discheck-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: template,
                shared: true);
        }

        return config.CreateLogger();
    }

    private static bool ProbeWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
