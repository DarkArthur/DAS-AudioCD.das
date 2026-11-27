using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscCheck.Core;

/// <summary>UI theme (Requirement 22.7). Default Light.</summary>
public enum SettingsTheme { Light, Dark }

/// <summary>Application settings persisted in <c>settings.json</c> (design: Settings).</summary>
public sealed record Settings
{
    public const string DefaultDatabasePath = "%LOCALAPPDATA%\\DiscCheck\\disccheck.db";

    public Thresholds Thresholds { get; init; } = new();
    public int DefaultSpeedX { get; init; } = 8;
    public LiteOnMethod LiteOnMethod { get; init; } = LiteOnMethod.New;
    public string DatabasePath { get; init; } = DefaultDatabasePath;
    public bool DebugMode { get; init; }
    public SettingsTheme Theme { get; init; } = SettingsTheme.Light;
    public int RereadAttempts { get; init; } = 3;
    public int ScsiTimeoutSeconds { get; init; } = 30;
    public int RecheckYears { get; init; } = 5;
}

/// <summary>A violated constraint: the offending field and the constraint (Requirement 22.9).</summary>
public sealed record SettingsError(string Field, string Message);

/// <summary>Validation of <see cref="Settings"/> (Requirements 11.5, 20.12, 22.4, 22.9).</summary>
public static class SettingsValidator
{
    public const string ThresholdsField = "thresholds";

    public static IReadOnlyList<SettingsError> Validate(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var errors = new List<SettingsError>();

        var t = s.Thresholds;
        if (t is null) errors.Add(new(ThresholdsField, "Le soglie sono obbligatorie."));
        else
        {
            CheckThreshold(errors, "thresholds.c1MeanWarn", t.C1MeanWarn);
            CheckThreshold(errors, "thresholds.c1MaxWarn", t.C1MaxWarn);
            CheckThreshold(errors, "thresholds.c1MaxKo", t.C1MaxKo);
            if (IsFiniteNonNegative(t.C1MaxWarn) && IsFiniteNonNegative(t.C1MaxKo) && !(t.C1MaxWarn < t.C1MaxKo))
                errors.Add(new("thresholds.c1MaxWarn", "La soglia C1 massimo per WARN deve essere minore della soglia C1 massimo per KO."));
        }

        if (s.DefaultSpeedX is < 1 or > 52) errors.Add(new("defaultSpeedX", "La velocità deve essere un intero tra 1 e 52."));
        if (!Enum.IsDefined(s.LiteOnMethod)) errors.Add(new("liteOnMethod", "La variante Lite-On deve essere New oppure Old."));
        if (string.IsNullOrWhiteSpace(s.DatabasePath)) errors.Add(new("databasePath", "Il percorso del Database non può essere vuoto."));
        if (!Enum.IsDefined(s.Theme)) errors.Add(new("theme", "Il tema deve essere Light oppure Dark."));
        if (s.RereadAttempts is < 1 or > 10) errors.Add(new("rereadAttempts", "Il numero di tentativi di rilettura deve essere un intero tra 1 e 10."));
        if (s.ScsiTimeoutSeconds is < 1 or > 300) errors.Add(new("scsiTimeoutSeconds", "Il timeout SCSI deve essere un intero tra 1 e 300 secondi."));
        if (s.RecheckYears is < 5 or > 10) errors.Add(new("recheckYears", "L'intervallo di ricontrollo deve essere un intero tra 5 e 10 anni."));
        return errors;
    }

    private static bool IsFiniteNonNegative(double v) => double.IsFinite(v) && v >= 0;

    private static void CheckThreshold(List<SettingsError> errors, string field, double v)
    {
        if (!IsFiniteNonNegative(v)) errors.Add(new(field, "La soglia deve essere un valore numerico maggiore o uguale a 0."));
    }
}

/// <summary>Outcome of <see cref="ISettingsStore.Load"/>. <see cref="Errors"/> non-empty means defaults were used for the listed fields and the file was left untouched.</summary>
public sealed record SettingsLoadResult(Settings Settings, IReadOnlyList<SettingsError> Errors, bool FileCreated = false)
{
    public bool HasWarnings => Errors.Count > 0;
}

/// <summary>Outcome of <see cref="ISettingsStore.Save"/>; on failure <c>settings.json</c> is unchanged.</summary>
public sealed record SaveResult(bool Success, IReadOnlyList<SettingsError> Errors)
{
    public static SaveResult Ok { get; } = new(true, []);
}

public interface ISettingsStore
{
    SettingsLoadResult Load();
    SaveResult Save(Settings settings);
}

/// <summary>JSON (de)serialization of <see cref="Settings"/> with the schema of the design.</summary>
public static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(Settings s) => JsonSerializer.Serialize(ToDto(s), Options);

    /// <summary>Strict deserialization (throws <see cref="JsonException"/> on malformed content).</summary>
    public static Settings Deserialize(string json) =>
        FromDto(JsonSerializer.Deserialize<Dto>(json, Options) ?? throw new JsonException("Empty settings document."));

    private sealed record ThresholdsDto(double C1MeanWarn, double C1MaxWarn, double C1MaxKo);

    private sealed record Dto(ThresholdsDto Thresholds, int DefaultSpeedX, LiteOnMethod LiteOnMethod, string DatabasePath,
                              bool DebugMode, SettingsTheme Theme, int RereadAttempts, int ScsiTimeoutSeconds, int RecheckYears);

    private static Dto ToDto(Settings s) => new(
        new ThresholdsDto(s.Thresholds.C1MeanWarn, s.Thresholds.C1MaxWarn, s.Thresholds.C1MaxKo),
        s.DefaultSpeedX, s.LiteOnMethod, s.DatabasePath, s.DebugMode, s.Theme, s.RereadAttempts, s.ScsiTimeoutSeconds, s.RecheckYears);

    private static Settings FromDto(Dto d) => new()
    {
        Thresholds = new Thresholds(d.Thresholds.C1MeanWarn, d.Thresholds.C1MaxWarn, d.Thresholds.C1MaxKo),
        DefaultSpeedX = d.DefaultSpeedX,
        LiteOnMethod = d.LiteOnMethod,
        DatabasePath = d.DatabasePath,
        DebugMode = d.DebugMode,
        Theme = d.Theme,
        RereadAttempts = d.RereadAttempts,
        ScsiTimeoutSeconds = d.ScsiTimeoutSeconds,
        RecheckYears = d.RecheckYears,
    };

    /// <summary>
    /// Lenient parse used at load time: each field that is missing, mistyped or out of range falls back to its default and
    /// is reported; all thresholds fall back together (Requirement 11.9). Throws <see cref="JsonException"/> if the document is not a JSON object.
    /// </summary>
    internal static (Settings Settings, List<SettingsError> Errors) ParseLenient(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("settings.json must contain a JSON object.");
        var root = doc.RootElement;
        var def = new Settings();
        var errors = new List<SettingsError>();

        var thresholds = def.Thresholds;
        if (root.TryGetProperty("thresholds", out var te))
        {
            if (te.ValueKind == JsonValueKind.Object
                && TryNumber(te, "c1MeanWarn", def.Thresholds.C1MeanWarn, out var mean)
                && TryNumber(te, "c1MaxWarn", def.Thresholds.C1MaxWarn, out var warn)
                && TryNumber(te, "c1MaxKo", def.Thresholds.C1MaxKo, out var ko))
            {
                var candidate = new Thresholds(mean, warn, ko);
                var trial = SettingsValidator.Validate(def with { Thresholds = candidate })
                    .Where(e => e.Field.StartsWith("thresholds", StringComparison.Ordinal)).ToList();
                if (trial.Count == 0) thresholds = candidate; else errors.AddRange(trial);
            }
            else errors.Add(new SettingsError("thresholds", "Le soglie devono essere valori numerici maggiori o uguali a 0."));
        }

        var result = def with
        {
            Thresholds = thresholds,
            DefaultSpeedX = Int(root, "defaultSpeedX", def.DefaultSpeedX, 1, 52, "La velocità deve essere un intero tra 1 e 52.", errors),
            LiteOnMethod = Enumeration(root, "liteOnMethod", def.LiteOnMethod, errors),
            DatabasePath = Str(root, "databasePath", def.DatabasePath, errors),
            DebugMode = Bool(root, "debugMode", def.DebugMode, errors),
            Theme = Enumeration(root, "theme", def.Theme, errors),
            RereadAttempts = Int(root, "rereadAttempts", def.RereadAttempts, 1, 10, "Il numero di tentativi di rilettura deve essere un intero tra 1 e 10.", errors),
            ScsiTimeoutSeconds = Int(root, "scsiTimeoutSeconds", def.ScsiTimeoutSeconds, 1, 300, "Il timeout SCSI deve essere un intero tra 1 e 300 secondi.", errors),
            RecheckYears = Int(root, "recheckYears", def.RecheckYears, 5, 10, "L'intervallo di ricontrollo deve essere un intero tra 5 e 10 anni.", errors),
        };
        return (result, errors);
    }

    private static bool TryNumber(JsonElement obj, string name, double fallback, out double value)
    {
        value = fallback;
        if (!obj.TryGetProperty(name, out var e)) return true;
        return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out value);
    }

    private static int Int(JsonElement root, string name, int fallback, int min, int max, string message, List<SettingsError> errors)
    {
        if (!root.TryGetProperty(name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) && v >= min && v <= max) return v;
        errors.Add(new SettingsError(name, message));
        return fallback;
    }

    private static string Str(JsonElement root, string name, string fallback, List<SettingsError> errors)
    {
        if (!root.TryGetProperty(name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())) return e.GetString()!;
        errors.Add(new SettingsError(name, "Il percorso del Database non può essere vuoto."));
        return fallback;
    }

    private static bool Bool(JsonElement root, string name, bool fallback, List<SettingsError> errors)
    {
        if (!root.TryGetProperty(name, out var e)) return fallback;
        if (e.ValueKind is JsonValueKind.True or JsonValueKind.False) return e.GetBoolean();
        errors.Add(new SettingsError(name, "Il valore deve essere true oppure false."));
        return fallback;
    }

    private static T Enumeration<T>(JsonElement root, string name, T fallback, List<SettingsError> errors) where T : struct, Enum
    {
        if (!root.TryGetProperty(name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.String && Enum.TryParse<T>(e.GetString(), true, out var v) && Enum.IsDefined(v)
            && !int.TryParse(e.GetString(), out _)) return v;
        errors.Add(new SettingsError(name, $"Valore non valido; valori ammessi: {string.Join(", ", Enum.GetNames<T>())}."));
        return fallback;
    }
}

/// <summary>
/// File-based <see cref="ISettingsStore"/> (Requirements 11.5, 11.9, 20.12, 22.2-22.4, 22.9, 22.10).
/// Missing file: created with defaults. Unreadable/invalid file: defaults, errors reported, file left untouched.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly object _gate = new();
    private Settings _current = new();

    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }

    /// <summary>Last loaded or saved settings (defaults before <see cref="Load"/>).</summary>
    public Settings Current { get { lock (_gate) return _current; } }

    /// <summary>Default location: <c>%LOCALAPPDATA%\DiscCheck\settings.json</c>.</summary>
    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscCheck", "settings.json");

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            var defaults = new Settings();
            if (!File.Exists(Path))
            {
                var errors = new List<SettingsError>();
                bool created = false;
                try
                {
                    WriteAtomic(SettingsJson.Serialize(defaults));
                    created = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new SettingsError("file", $"Impossibile creare settings.json: {ex.Message}"));
                }
                _current = defaults;
                return new SettingsLoadResult(defaults, errors, created);
            }

            try
            {
                var (settings, errors) = SettingsJson.ParseLenient(File.ReadAllText(Path));
                _current = settings;
                return new SettingsLoadResult(settings, errors);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _current = defaults;
                return new SettingsLoadResult(defaults, [new SettingsError("file", $"settings.json non leggibile o non valido: {ex.Message}")]);
            }
        }
    }

    public SaveResult Save(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0) return new SaveResult(false, errors);

        lock (_gate)
        {
            try
            {
                WriteAtomic(SettingsJson.Serialize(settings));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SaveResult(false, [new SettingsError("file", $"Impossibile scrivere settings.json: {ex.Message}")]);
            }
            _current = settings;
        }
        Changed?.Invoke(settings);
        return SaveResult.Ok;
    }

    /// <summary>Raised after a successful <see cref="Save"/> (outside the lock), so consumers can pick up the new values.</summary>
    public event Action<Settings>? Changed;

    // Write to a temp file then replace, so a failure never leaves a partial settings.json.
    private void WriteAtomic(string content)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}

/// <summary>
/// <see cref="ILiteOnMethodSetting"/> fed by the Settings_Store: always reports the current stored value.
/// Wiring into the DI container is done in task 15.4.
/// </summary>
public sealed class StoreLiteOnMethodSetting(SettingsStore store) : ILiteOnMethodSetting
{
    public LiteOnMethod Method => store.Current.LiteOnMethod;
}
