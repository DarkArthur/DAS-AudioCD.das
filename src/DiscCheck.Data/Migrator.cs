using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Data;

/// <summary>Uno script di migrazione fallito; il suo rollback è già stato eseguito.</summary>
public sealed class MigrationException(int scriptVersion, Exception inner)
    : Exception($"Migrazione {scriptVersion:000} fallita: {inner.Message}", inner)
{
    public int ScriptVersion { get; } = scriptVersion;
}

/// <summary>Il Database ha una schema_version superiore alla massima nota.</summary>
public sealed class IncompatibleSchemaException(int foundVersion, int supportedVersion)
    : Exception($"Il Database ha schema_version {foundVersion}, superiore alla versione massima supportata {supportedVersion}: database incompatibile con questa versione dell'App.")
{
    public int FoundVersion { get; } = foundVersion;
    public int SupportedVersion { get; } = supportedVersion;
}

/// <summary>Applica gli script SQL incorporati NNN_*.sql in ordine crescente, una transazione per script.</summary>
public sealed partial class Migrator
{
    private readonly IReadOnlyList<(int Version, string Sql)> _scripts;

    public Migrator() : this(LoadEmbeddedScripts()) { }

    /// <summary>Costruttore per test con script alternativi.</summary>
    public Migrator(IEnumerable<(int Version, string Sql)> scripts)
    {
        _scripts = scripts.OrderBy(s => s.Version).ToList();
    }

    public int MaxSupportedVersion => _scripts.Count == 0 ? 0 : _scripts[^1].Version;

    /// <summary>Apre una connessione con PRAGMA foreign_keys=ON (da usare per ogni connessione).</summary>
    public static SqliteConnection OpenConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Versione corrente dello schema (0 se schema_version non esiste). Sola lettura.</summary>
    public static int GetCurrentVersion(SqliteConnection connection)
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version';";
            if (Convert.ToInt64(exists.ExecuteScalar()) == 0) return 0;
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Applica le migrazioni mancanti. Lancia <see cref="IncompatibleSchemaException"/> (senza modificare il DB)
    /// se la versione trovata è superiore alla massima nota, <see cref="MigrationException"/> dopo rollback dello script fallito.
    /// </summary>
    public void Migrate(SqliteConnection connection)
    {
        EnsureForeignKeys(connection);

        var current = GetCurrentVersion(connection);
        if (current > MaxSupportedVersion)
            throw new IncompatibleSchemaException(current, MaxSupportedVersion);

        foreach (var (version, sql) in _scripts.Where(s => s.Version > current))
        {
            using var tx = connection.BeginTransaction();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                using (var upd = connection.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version(version) VALUES ($v);";
                    upd.Parameters.AddWithValue("$v", version);
                    upd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { /* transazione già annullata */ }
                throw new MigrationException(version, ex);
            }
        }
    }

    private static void EnsureForeignKeys(SqliteConnection connection)
    {
        // Il PRAGMA non ha effetto dentro una transazione: va eseguito prima di aprirne una.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
    }

    private static List<(int Version, string Sql)> LoadEmbeddedScripts()
    {
        var asm = typeof(Migrator).Assembly;
        var result = new List<(int, string)>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            var m = ScriptNameRegex().Match(name);
            if (!m.Success) continue;
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            result.Add((int.Parse(m.Groups[1].Value), reader.ReadToEnd()));
        }
        return result;
    }

    [GeneratedRegex(@"Migrations\.(\d{3})_[^.]*\.sql$")]
    private static partial Regex ScriptNameRegex();
}
