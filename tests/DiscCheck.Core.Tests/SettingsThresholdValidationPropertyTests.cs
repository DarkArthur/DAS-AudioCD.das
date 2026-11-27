using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class SettingsThresholdValidationPropertyTests
{
    // Feature: disccheck, Property 17: Validazione delle Soglie
    // **Validates: Requirements 11.5, 11.9, 22.9**
    [Property(MaxTest = 200)]
    public bool Thresholds_AcceptedIffNonNegativeAndWarnBelowKo(double mean, double warn, double ko)
    {
        var settings = new Settings { Thresholds = new Thresholds(mean, warn, ko) };
        var errors = SettingsValidator.Validate(settings)
            .Where(e => e.Field.StartsWith("thresholds", StringComparison.Ordinal)).ToList();

        static bool NonNeg(double v) => double.IsFinite(v) && v >= 0;
        bool valid = NonNeg(mean) && NonNeg(warn) && NonNeg(ko) && warn < ko;

        return valid
            ? errors.Count == 0
            : errors.Count > 0 && errors.All(e => !string.IsNullOrWhiteSpace(e.Field) && !string.IsNullOrWhiteSpace(e.Message));
    }

    // Feature: disccheck, Property 17: Validazione delle Soglie (dense valid/invalid boundary region)
    // **Validates: Requirements 11.5, 11.9, 22.9**
    [Property(MaxTest = 200)]
    public bool Thresholds_SmallIntegerGrid_ErrorsNameOffendingField(sbyte mean, sbyte warn, sbyte ko)
    {
        var settings = new Settings { Thresholds = new Thresholds(mean / 4.0, warn / 4.0, ko / 4.0) };
        var errors = SettingsValidator.Validate(settings)
            .Where(e => e.Field.StartsWith("thresholds", StringComparison.Ordinal)).Select(e => e.Field).ToHashSet();

        if (mean < 0 != errors.Contains("thresholds.c1MeanWarn")) return false;
        if (warn < 0 && !errors.Contains("thresholds.c1MaxWarn")) return false;
        if (ko < 0 && !errors.Contains("thresholds.c1MaxKo")) return false;
        bool valid = mean >= 0 && warn >= 0 && ko >= 0 && warn < ko;
        return valid == (errors.Count == 0);
    }
}
