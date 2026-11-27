using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class TocHashPropertyTests
{
    /// <summary>Valid TOC: 1..99 tracks, strictly increasing start LBAs, lead-out beyond the last track.</summary>
    private static Toc BuildToc(byte trackSeed, int[] gaps, int firstStart)
    {
        int n = 1 + trackSeed % 99;
        int G(int i) => 1 + ((gaps.Length == 0 ? 0 : gaps[i % gaps.Length]) & 0x7FFF);

        var tracks = new List<TocEntry>();
        int lba = firstStart & 0xFFFF;
        for (int i = 0; i < n; i++)
        {
            tracks.Add(new TocEntry(i + 1, lba, true));
            lba += G(i);
        }

        return new Toc(tracks, lba);
    }

    // Feature: disccheck, Property 8: Determinismo e sensibilità del toc_hash
    // **Validates: Requirements 5.8**
    [Property(MaxTest = 100)]
    public bool TocHash_IsDeterministic(byte trackSeed, int[] gaps, int firstStart)
    {
        var toc = BuildToc(trackSeed, gaps, firstStart);
        var copy = new Toc([.. toc.Tracks.Select(t => t with { })], toc.LeadOutLba);

        string h = TocHasher.Hash(toc);
        return h == TocHasher.Hash(toc) && h == TocHasher.Hash(copy);
    }

    // Feature: disccheck, Property 8: Determinismo e sensibilità del toc_hash
    // **Validates: Requirements 5.8**
    [Property(MaxTest = 100)]
    public bool TocHash_ChangesWhenTrackCountStartOrLeadOutDiffer(byte trackSeed, int[] gaps, int firstStart, byte pick)
    {
        var toc = BuildToc(trackSeed, gaps, firstStart);
        string h = TocHasher.Hash(toc);

        // Different track count: append one more track at the old lead-out.
        var more = new Toc([.. toc.Tracks, new TocEntry(toc.Tracks.Count + 1, toc.LeadOutLba, true)], toc.LeadOutLba + 1);

        // Different start LBA for one track.
        int idx = pick % toc.Tracks.Count;
        var changedStart = new Toc(
            [.. toc.Tracks.Select((t, i) => i == idx ? t with { StartLba = t.StartLba + 1 } : t)],
            toc.LeadOutLba);

        // Different lead-out LBA.
        var changedLeadOut = new Toc(toc.Tracks, toc.LeadOutLba + 1);

        var hashes = new List<string> { h, TocHasher.Hash(more), TocHasher.Hash(changedStart), TocHasher.Hash(changedLeadOut) };

        if (toc.Tracks.Count > 1)
            hashes.Add(TocHasher.Hash(new Toc([.. toc.Tracks.Take(toc.Tracks.Count - 1)], toc.LeadOutLba)));

        return hashes.Distinct().Count() == hashes.Count;
    }
}
