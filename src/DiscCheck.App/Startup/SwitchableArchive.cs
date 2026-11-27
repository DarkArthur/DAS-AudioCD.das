using DiscCheck.Core;
using DiscCheck.Data;

namespace DiscCheck.App.Startup;

/// <summary>Switches the Database at runtime (Requirement 22.6).</summary>
public interface IArchiveSwitcher
{
    /// <summary>Path of the Database currently in use.</summary>
    string CurrentPath { get; }

    /// <summary>
    /// Opens the Database at <paramref name="path"/>, creating and migrating it if missing; on success closes the previous
    /// one and returns null. On failure the previous Database stays in use and the cause is returned (Requirement 22.11).
    /// </summary>
    string? TryReopen(string path);
}

/// <summary>
/// <see cref="IArchive"/> that forwards to the current Database and can move to another file without the consumers
/// (repositories, view models) noticing. Calls in flight finish on the old connection before it is closed.
/// </summary>
public sealed class SwitchableArchive : IArchive, IArchiveSwitcher, IDisposable
{
    private readonly Func<string, ArchiveOpenResult> _open;
    private readonly ReaderWriterLockSlim _lock = new();
    private IArchive _current;

    public SwitchableArchive(ArchiveOpenResult initial, string initialPath, Func<string, ArchiveOpenResult>? open = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial.Archive;
        CurrentPath = initialPath;
        _open = open ?? (p => ArchiveOpener.Open(p));
    }

    public string CurrentPath { get; private set; }

    public T Execute<T>(Func<ArchiveRepositories, T> work)
    {
        _lock.EnterReadLock();
        try { return _current.Execute(work); }
        finally { _lock.ExitReadLock(); }
    }

    public string? TryReopen(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = _open(path);
        if (result.Error is not null)
        {
            result.Dispose();
            return result.Error;
        }

        IArchive old;
        _lock.EnterWriteLock();
        try
        {
            old = _current;
            _current = result.Archive;
            CurrentPath = path;
        }
        finally { _lock.ExitWriteLock(); }

        (old as IDisposable)?.Dispose();
        return null;
    }

    public void Dispose()
    {
        _lock.EnterWriteLock();
        try { (_current as IDisposable)?.Dispose(); }
        finally { _lock.ExitWriteLock(); }
    }
}
