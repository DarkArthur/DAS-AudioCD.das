using System.Globalization;
using System.Text.Json;

namespace DiscCheck.Core;

/// <summary>ATIP lead-in start time, mm:ss:ff, used as the Media_ID (Requirement 5.3).</summary>
public record AtipTime(int Minutes, int Seconds, int Frames)
{
    public bool IsValid => Minutes is >= 0 and <= 99 && Seconds is >= 0 and <= 59 && Frames is >= 0 and <= 74;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Minutes:00}:{Seconds:00}:{Frames:00}");

    public static bool TryParse(string? text, out AtipTime? time)
    {
        time = null;
        var f = text?.Split(':');
        if (f is not { Length: 3 } ||
            !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int m) ||
            !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int s) ||
            !int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int fr))
            return false;

        var t = new AtipTime(m, s, fr);
        if (!t.IsValid) return false;
        time = t;
        return true;
    }
}

/// <summary>Pure parser of a READ TOC (format 4, ATIP) response (Requirement 5.3).</summary>
public static class AtipParser
{
    /// <summary>4-byte header plus the descriptor up to the lead-in start time (bytes 8-10).</summary>
    public const int MinLength = 11;

    public static bool TryParse(ReadOnlySpan<byte> data, out AtipTime? time, out string? error)
    {
        time = null;
        if (data.Length < MinLength)
        {
            error = $"risposta ATIP troppo corta ({data.Length} byte ricevuti, {MinLength} attesi)";
            return false;
        }

        var t = new AtipTime(data[8], data[9], data[10]);
        if (!t.IsValid)
        {
            error = $"tempo di inizio lead-in ATIP non valido ({data[8]}:{data[9]}:{data[10]})";
            return false;
        }

        time = t;
        error = null;
        return true;
    }
}

/// <summary>
/// ATIP_Table: exact lookup of the CD-R manufacturer by Media_ID (Requirements 5.4-5.7, 5.14).
/// Never throws: a missing or invalid table yields an empty table and an error reported through the log delegate.
/// </summary>
public sealed class AtipTable
{
    public const string UnknownManufacturer = "produttore sconosciuto";
    public const string ResourceName = "DiscCheck.Core.Resources.atip-manufacturers.json";

    /// <summary>Codes excluded until confirmed on a public source (Requirement 5.7).</summary>
    public static readonly IReadOnlySet<string> ExcludedCodes = new HashSet<string> { "97:24:01", "97:34:23" };

    private readonly Dictionary<AtipTime, string> _entries;

    private AtipTable(int version, Dictionary<AtipTime, string> entries)
    {
        Version = version;
        _entries = entries;
    }

    /// <summary>Table version (&gt;= 1), or 0 when the table could not be loaded.</summary>
    public int Version { get; }

    public int Count => _entries.Count;

    public static AtipTable Empty { get; } = new(0, []);

    /// <summary>Manufacturer for the exact Media_ID, or <see cref="UnknownManufacturer"/>.</summary>
    public string Lookup(AtipTime mediaId) => _entries.TryGetValue(mediaId, out var m) ? m : UnknownManufacturer;

    /// <summary>Loads the table embedded in this assembly.</summary>
    public static AtipTable LoadEmbedded(Action<string>? logError = null)
    {
        using var stream = typeof(AtipTable).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            logError?.Invoke($"ATIP_Table assente: risorsa {ResourceName} non trovata");
            return Empty;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), logError);
    }

    /// <summary>Parses the JSON text; on any problem logs the error and returns <see cref="Empty"/>.</summary>
    public static AtipTable Parse(string? json, Action<string>? logError = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logError?.Invoke("ATIP_Table assente o vuota");
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number ||
                !v.TryGetInt32(out int version) || version < 1)
            {
                logError?.Invoke("ATIP_Table non valida: campo 'version' mancante o non intero >= 1");
                return Empty;
            }

            if (!root.TryGetProperty("entries", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                logError?.Invoke("ATIP_Table non valida: campo 'entries' mancante");
                return Empty;
            }

            var entries = new Dictionary<AtipTime, string>();
            foreach (var e in list.EnumerateArray())
            {
                string? code = Text(e, "atip"), name = Text(e, "manufacturer"), source = Text(e, "source");
                if (!AtipTime.TryParse(code, out var time) || string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(source))
                {
                    logError?.Invoke($"ATIP_Table: voce ignorata (codice, produttore o fonte mancanti/non validi): {code}");
                    continue;
                }

                if (ExcludedCodes.Contains(time!.ToString()))
                {
                    logError?.Invoke($"ATIP_Table: voce {time} esclusa (non confermata su fonte pubblica)");
                    continue;
                }

                if (!entries.TryAdd(time, name))
                    logError?.Invoke($"ATIP_Table: voce duplicata ignorata: {time}");
            }

            return new AtipTable(version, entries);
        }
        catch (JsonException ex)
        {
            logError?.Invoke($"ATIP_Table non è un JSON valido: {ex.Message}");
            return Empty;
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
