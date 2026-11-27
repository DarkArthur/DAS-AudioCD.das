using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscCheck.Core;

public enum UpdateStatus { Disabled, UpToDate, UpdateAvailable, Failed }

/// <summary>Outcome of <see cref="UpdateChecker.CheckAsync"/>. <see cref="Url"/> is set only when it is an http(s) address.</summary>
public sealed record UpdateCheckResult(UpdateStatus Status, string? LatestVersion = null, string? Url = null,
                                       string? Notes = null, string? Message = null)
{
    public bool IsUpdateAvailable => Status == UpdateStatus.UpdateAvailable;
}

/// <summary>Content of the published <c>version.json</c> (written by the release workflow).</summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("installer")] string? Installer = null,
    [property: JsonPropertyName("sha256")] string? Sha256 = null,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("published")] string? Published = null);

/// <summary>Reads the text of the manifest from <paramref name="source"/> (http(s) address, UNC or local path).</summary>
public interface IUpdateManifestReader
{
    Task<string> ReadAsync(string source, CancellationToken ct);
}

/// <summary>
/// Default reader: http(s) through <see cref="HttpClient"/> (short timeout, no credentials), anything else as a file path.
/// </summary>
public sealed class UpdateManifestReader : IUpdateManifestReader
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const long MaxBytes = 64 * 1024;

    public async Task<string> ReadAsync(string source, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        if (UpdateChecker.IsHttpUrl(source))
        {
            using var http = new HttpClient { Timeout = Timeout };
            using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxBytes) throw new InvalidDataException("Manifest troppo grande.");
            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }

        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("File version.json non trovato.", source);
        if (info.Length > MaxBytes) throw new InvalidDataException("Manifest troppo grande.");
        return await File.ReadAllTextAsync(source, timeout.Token).ConfigureAwait(false);
    }
}

/// <summary>
/// Looks for a newer published version (notification only: nothing is downloaded or run). Any failure is reported as
/// <see cref="UpdateStatus.Failed"/> and never throws, so a missing network cannot disturb the startup.
/// </summary>
public sealed class UpdateChecker(IUpdateManifestReader reader)
{
    public async Task<UpdateCheckResult> CheckAsync(string? source, string currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return new UpdateCheckResult(UpdateStatus.Disabled);
        if (!TryParseVersion(currentVersion, out var current))
            return new UpdateCheckResult(UpdateStatus.Failed, Message: $"Versione corrente non valida: {currentVersion}");

        try
        {
            var text = await reader.ReadAsync(source.Trim(), ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (manifest is null || !TryParseVersion(manifest.Version, out var latest))
                return new UpdateCheckResult(UpdateStatus.Failed, Message: "version.json non contiene una versione valida.");

            if (latest <= current) return new UpdateCheckResult(UpdateStatus.UpToDate, manifest.Version);
            return new UpdateCheckResult(UpdateStatus.UpdateAvailable, latest.ToString(3),
                IsHttpUrl(manifest.Url) ? manifest.Url : null, manifest.Notes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, Message: ex.Message);
        }
    }

    /// <summary>Parses "1.2.3", "v1.2.3", "1.2.3-rc.1" and "1.2.3+build" as 1.2.3 (pre-release suffixes are ignored).</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0) s = s[..cut];
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4 || !parts.All(p => int.TryParse(p, out var n) && n >= 0)) return false;
        var n3 = parts.Select(int.Parse).Concat([0, 0, 0]).Take(3).ToArray();
        version = new Version(n3[0], n3[1], n3[2]);
        return true;
    }

    public static bool IsHttpUrl(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
}
