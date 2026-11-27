using DiscCheck.Core;
using FsCheck;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class SettingsRoundTripPropertyTests
{
    // Feature: disccheck, Property 16: Round-trip JSON delle impostazioni
    // **Validates: Requirements 22.8**
    [Property(MaxTest = 100)]
    public bool Settings_JsonRoundTrip(
        uint meanRaw, uint warnRaw, uint koExtra, byte speed, bool liteOnOld, byte pathSeed, bool debug,
        bool dark, byte reread, byte timeout, byte years)
    {
        var warn = warnRaw / 1000.0;
        var settings = new Settings
        {
            Thresholds = new Thresholds(meanRaw / 1000.0, warn, warn + 0.001 + koExtra / 1000.0),
            DefaultSpeedX = 1 + speed % 52,
            LiteOnMethod = liteOnOld ? LiteOnMethod.Old : LiteOnMethod.New,
            DatabasePath = "C:\\dir \u00e8" + pathSeed + "\\\"q\"\\d.db",
            DebugMode = debug,
            Theme = dark ? SettingsTheme.Dark : SettingsTheme.Light,
            RereadAttempts = 1 + reread % 10,
            ScsiTimeoutSeconds = 1 + timeout % 300,
            RecheckYears = 5 + years % 6,
        };
        if (SettingsValidator.Validate(settings).Count != 0) return false;

        var back = SettingsJson.Deserialize(SettingsJson.Serialize(settings));
        return back == settings;
    }
}
