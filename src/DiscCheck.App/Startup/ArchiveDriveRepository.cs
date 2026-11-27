using DiscCheck.Core;

namespace DiscCheck.App.Startup;

/// <summary>
/// <see cref="IDriveRepository"/> over <see cref="IArchive"/>: each call runs inside <see cref="IArchive.Execute{T}"/>,
/// so services such as OffsetService and CacheDetectorRunner can hold it without keeping the repositories open.
/// </summary>
public sealed class ArchiveDriveRepository(IArchive archive) : IDriveRepository
{
    public long AddIfMissing(DriveRecord drive) => archive.Execute(r => r.Drives.AddIfMissing(drive));
    public DriveRecord? Get(long id) => archive.Execute(r => r.Drives.Get(id));
    public IReadOnlyList<DriveRecord> List() => archive.Execute(r => r.Drives.List());
}
