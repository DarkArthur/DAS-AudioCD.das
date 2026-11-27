using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DiscCheck.Core;

/// <summary>One TOC track (Requirement 5.1). <see cref="IsAudio"/> is false when the control field flags a data track.</summary>
public record TocEntry(int TrackNo, int StartLba, bool IsAudio);

/// <summary>Table of contents of a disc: 1..99 tracks with increasing start LBAs plus the lead-out LBA.</summary>
public record Toc(IReadOnlyList<TocEntry> Tracks, int LeadOutLba)
{
    public const int SectorsPerSecond = 75;

    /// <summary>Total number of sectors, from the first track start to the lead-out.</summary>
    public int TotalSectors => LeadOutLba - (Tracks.Count > 0 ? Tracks[0].StartLba : 0);

    /// <summary>Length in sectors of the track at <paramref name="index"/> (0-based).</summary>
    public int TrackSectors(int index)
    {
        int end = index + 1 < Tracks.Count ? Tracks[index + 1].StartLba : LeadOutLba;
        return end - Tracks[index].StartLba;
    }

    public bool HasAudioTracks => Tracks.Any(t => t.IsAudio);

    /// <summary>Field-by-field equality (the record's own equality compares the list by reference).</summary>
    public bool IsEquivalentTo(Toc? other) =>
        other is not null && LeadOutLba == other.LeadOutLba && Tracks.SequenceEqual(other.Tracks);
}

/// <summary>Duration formatting: mm:ss with seconds rounded down, 75 sectors = 1 second (Requirement 5.2).</summary>
public static class TocFormat
{
    public static string Duration(int sectors)
    {
        long totalSeconds = Math.Max(0, sectors) / Toc.SectorsPerSecond;
        return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60:00}:{totalSeconds % 60:00}");
    }
}

/// <summary>Pure parser of a READ TOC (format 0, LBA addressing) response (Requirement 5.1).</summary>
public static class TocParser
{
    /// <summary>Header (4 bytes) plus at least one track descriptor and the lead-out descriptor (8 bytes each).</summary>
    public const int MinLength = 4 + 8 + 8;

    public const int LeadOutTrack = 0xAA;

    public static bool TryParse(ReadOnlySpan<byte> data, out Toc? toc, out string? error)
    {
        toc = null;
        if (data.Length < MinLength)
        {
            error = $"risposta TOC troppo corta ({data.Length} byte ricevuti, {MinLength} attesi)";
            return false;
        }

        int declared = ((data[0] << 8) | data[1]) + 2;
        int usable = Math.Min(declared, data.Length);
        int count = (usable - 4) / 8;
        int first = data[2], last = data[3];
        if (first < 1 || last < first || last > 99)
        {
            error = $"intestazione TOC non valida (tracce {first}-{last})";
            return false;
        }

        int expected = last - first + 1;
        if (count < expected + 1)
        {
            error = $"descrittori TOC incompleti ({count} presenti, {expected + 1} attesi)";
            return false;
        }

        var tracks = new List<TocEntry>(expected);
        int previous = int.MinValue;
        for (int i = 0; i < expected; i++)
        {
            var d = data.Slice(4 + i * 8, 8);
            int no = d[2];
            if (no != first + i)
            {
                error = $"numero di traccia inatteso ({no}, atteso {first + i})";
                return false;
            }

            if (!TryLba(d, out int lba) || lba <= previous)
            {
                error = $"LBA non valido o non crescente per la traccia {no}";
                return false;
            }

            previous = lba;
            tracks.Add(new TocEntry(no, lba, (d[1] & 0x04) == 0));
        }

        var lo = data.Slice(4 + expected * 8, 8);
        if (lo[2] != LeadOutTrack || !TryLba(lo, out int leadOut) || leadOut <= previous)
        {
            error = "descrittore del lead-out mancante o non valido";
            return false;
        }

        toc = new Toc(tracks, leadOut);
        error = null;
        return true;
    }

    private static bool TryLba(ReadOnlySpan<byte> descriptor, out int lba)
    {
        uint v = ((uint)descriptor[4] << 24) | ((uint)descriptor[5] << 16) | ((uint)descriptor[6] << 8) | descriptor[7];
        lba = (int)v;
        return v <= int.MaxValue;
    }
}

/// <summary>Canonical text form of a <see cref="Toc"/>: <c>tracks=N;1:LBA:A;2:LBA:D;leadout=LBA</c> (Requirement 5.11).</summary>
public static class TocSerializer
{
    public static string Serialize(Toc toc)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"tracks={toc.Tracks.Count}");
        foreach (var t in toc.Tracks)
            sb.Append(CultureInfo.InvariantCulture, $";{t.TrackNo}:{t.StartLba}:{(t.IsAudio ? 'A' : 'D')}");
        sb.Append(CultureInfo.InvariantCulture, $";leadout={toc.LeadOutLba}");
        return sb.ToString();
    }

    public static bool TryParse(string text, out Toc? toc)
    {
        toc = null;
        var parts = text.Split(';');
        if (parts.Length < 3 || !parts[0].StartsWith("tracks=", StringComparison.Ordinal) ||
            !parts[^1].StartsWith("leadout=", StringComparison.Ordinal))
            return false;

        if (!int.TryParse(parts[0].AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ||
            n < 1 || n > 99 || parts.Length != n + 2 ||
            !int.TryParse(parts[^1].AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out int leadOut))
            return false;

        var tracks = new List<TocEntry>(n);
        for (int i = 1; i <= n; i++)
        {
            var f = parts[i].Split(':');
            if (f.Length != 3 ||
                !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int no) ||
                !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int lba) ||
                f[2] is not ("A" or "D"))
                return false;
            tracks.Add(new TocEntry(no, lba, f[2] == "A"));
        }

        toc = new Toc(tracks, leadOut);
        return true;
    }
}

/// <summary>Deterministic <c>toc_hash</c>: SHA-256 (lowercase hex) of the track count, start LBAs and lead-out (Requirement 5.8).</summary>
public static class TocHasher
{
    public static string Hash(Toc toc)
    {
        var canonical = string.Create(CultureInfo.InvariantCulture,
            $"toc/v1;n={toc.Tracks.Count};starts={string.Join(',', toc.Tracks.Select(t => t.StartLba))};leadout={toc.LeadOutLba}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
