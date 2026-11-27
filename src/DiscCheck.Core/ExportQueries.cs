namespace DiscCheck.Core;

/// <summary>Archive reads shared by the ViewModels that export the HTML report.</summary>
public static class ExportQueries
{
    /// <summary>
    /// Latest verification of the disc with the given TOC hash, as report data: the Drive's Read_Offset, the offset
    /// used (Combined_Offset) and the per-track outcomes. Null when the disc has no verification.
    /// </summary>
    public static ReportVerification? LatestVerification(ArchiveRepositories r, string tocHash, DriveRecord drive)
    {
        var disc = r.Discs.FindByTocHash(tocHash);
        if (disc is null) return null;
        var v = r.Verifications.List().Where(x => x.DiscId == disc.Id).OrderByDescending(x => x.VerifiedAt).FirstOrDefault();
        return v is null ? null
            : new ReportVerification(v.VerifiedAt, v.Result, drive.ReadOffset, v.OffsetUsed, r.Verifications.GetTracks(v.Id));
    }
}
