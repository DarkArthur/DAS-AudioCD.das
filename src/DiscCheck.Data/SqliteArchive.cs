using DiscCheck.Core;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Data;

/// <summary>One open Database connection with its repositories; calls are serialised with a lock.</summary>
public sealed class SqliteArchive : IArchive, IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ArchiveRepositories _repos;
    private readonly object _gate = new();

    public SqliteArchive(SqliteConnection db)
    {
        _db = db;
        _repos = new ArchiveRepositories(new DriveRepository(db), new MediaRepository(db), new DiscRepository(db),
                                         new ScanRepository(db), new VerificationRepository(db));
    }

    public T Execute<T>(Func<ArchiveRepositories, T> work)
    {
        lock (_gate) return work(_repos);
    }

    public void Dispose()
    {
        lock (_gate) _db.Dispose();
    }
}

/// <summary>Stands in for a Database that could not be opened: every call reports the opening error.</summary>
public sealed class UnavailableArchive(string message) : IArchive
{
    public T Execute<T>(Func<ArchiveRepositories, T> work) => throw new ArchiveUnavailableException(message);
}

/// <summary>Result of opening the Database at startup. <see cref="Error"/> is null on success.</summary>
public sealed class ArchiveOpenResult(IArchive archive, string? error) : IDisposable
{
    public IArchive Archive { get; } = archive;
    public string? Error { get; } = error;

    public void Dispose() => (Archive as IDisposable)?.Dispose();
}

public static class ArchiveOpener
{
    /// <summary>Default location: %LOCALAPPDATA%\DiscCheck\disccheck.db (Requirement 16.1).</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscCheck", "disccheck.db");

    /// <summary>
    /// Creates the Database if missing and applies the pending migrations (Requirement 16.8). Never throws: on failure
    /// the result carries an <see cref="UnavailableArchive"/> and a message with path and cause (Requirements 16.9,
    /// 16.10, 16.15). An existing file that is not a valid Database is only read, never modified.
    /// </summary>
    public static ArchiveOpenResult Open(string path, Migrator? migrator = null)
    {
        SqliteConnection? connection = null;
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);

            connection = Migrator.OpenConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
            (migrator ?? new Migrator()).Migrate(connection);
            return new ArchiveOpenResult(new SqliteArchive(connection), null);
        }
        catch (Exception ex)
        {
            connection?.Dispose();
            string message = ex switch
            {
                IncompatibleSchemaException or MigrationException =>
                    $"Impossibile usare il Database \"{path}\": {ex.Message}",
                SqliteException s => $"Impossibile aprire il Database \"{path}\": {s.Message}",
                _ => $"Impossibile creare o aprire il Database \"{path}\": {ex.Message}",
            };
            return new ArchiveOpenResult(new UnavailableArchive(message), message);
        }
    }
}
