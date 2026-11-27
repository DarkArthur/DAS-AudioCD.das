using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class TocRoundTripPropertyTests
{
    /// <summary>Builds a valid READ TOC (format 0) response: 1..99 tracks, increasing start LBAs, lead-out after the last track.</summary>
    private static byte[] BuildTocBytes(byte trackSeed, int[] gaps, bool[] dataFlags, int firstStart)
    {
        int n = 1 + trackSeed % 99;
        int G(int i) => 1 + ((gaps.Length == 0 ? 0 : gaps[i % gaps.Length]) & 0x7FFF);
        bool D(int i) => dataFlags.Length != 0 && dataFlags[i % dataFlags.Length];

        var list = new List<byte> { 0, 0, 1, (byte)n };
        int lba = firstStart & 0xFFFF;
        void Add(byte control, byte track, int value) =>
            list.AddRange([0, control, track, 0, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

        for (int i = 0; i < n; i++)
        {
            Add(D(i) ? (byte)0x14 : (byte)0x10, (byte)(i + 1), lba);
            lba += G(i);
        }

        Add(0x10, 0xAA, lba);
        int len = list.Count - 2;
        list[0] = (byte)(len >> 8);
        list[1] = (byte)len;
        return [.. list];
    }

    // Feature: disccheck, Property 7: Round-trip della TOC
    // **Validates: Requirements 5.11**
    [Property(MaxTest = 100)]
    public bool Toc_ParseSerializeParse_IsEquivalent(byte trackSeed, int[] gaps, bool[] dataFlags, int firstStart)
    {
        byte[] bytes = BuildTocBytes(trackSeed, gaps, dataFlags, firstStart);

        if (!TocParser.TryParse(bytes, out var parsed, out _) || parsed is null)
            return false;

        string text = TocSerializer.Serialize(parsed);
        if (!TocSerializer.TryParse(text, out var reparsed) || reparsed is null)
            return false;

        return parsed.IsEquivalentTo(reparsed)
            && TocSerializer.Serialize(reparsed) == text
            && parsed.Tracks.Count is >= 1 and <= 99;
    }
}
