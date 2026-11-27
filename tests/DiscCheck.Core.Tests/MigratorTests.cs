using DiscCheck.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Core.Tests;

public class MigratorTests : IDisposable
{
    private readonly SqliteConnection _db;

    public MigratorTests()
    {
        _db = Migrator.OpenConnection("Data Source=:memory:");
    }

    public void Dispose() => _db.Dispose();

    private long Scalar(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        return Convert.ToInt64(c.ExecuteScalar());
    }

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    [Fact]
    public void Migrate_creates_all_tables_and_sets_latest_version()
    {
        var m = new Migrator();
        m.Migrate(_db);

        Migrator.GetCurrentVersion(_db).Should().Be(2);
        m.MaxSupportedVersion.Should().Be(2);
        foreach (var t in new[] { "schema_version", "drives", "media", "discs", "scans", "scan_samples", "verifications", "verification_tracks" })
            Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'").Should().Be(1, t);
    }

    [Fact]
    public void Migration_002_makes_cu_nullable_and_keeps_existing_samples()
    {
        var all = new Migrator();
        var first = new Migrator(LoadScripts().Where(s => s.Version == 1));
        first.Migrate(_db);
        Exec("INSERT INTO drives(id,vendor,model,firmware) VALUES (1,'v','m','f')");
        Exec("INSERT INTO discs(id,toc_hash,track_count,total_sectors) VALUES (1,'h',1,100)");
        Exec("INSERT INTO scans(id,disc_id,drive_id,scanned_at,speed_x,scanner,verdict) VALUES (1,1,1,'2024-01-01',8,'LiteOn','OK')");
        Exec("INSERT INTO scan_samples(scan_id,t_sec,lba,c1,c2,cu,speed_x) VALUES (1,0,0,3,2,0,8)");
        var notNull = () => Exec("INSERT INTO scan_samples(scan_id,t_sec,lba,c2,cu) VALUES (1,1,75,0,NULL)");
        notNull.Should().Throw<SqliteException>();

        all.Migrate(_db);

        Migrator.GetCurrentVersion(_db).Should().Be(2);
        Exec("INSERT INTO scan_samples(scan_id,t_sec,lba,c2,cu) VALUES (1,1,75,0,NULL)");
        Scalar("SELECT COUNT(*) FROM scan_samples").Should().Be(2);
        Scalar("SELECT cu FROM scan_samples WHERE t_sec=0").Should().Be(0);
        Scalar("SELECT COUNT(*) FROM scan_samples WHERE cu IS NULL").Should().Be(1);
        Exec("DELETE FROM scans WHERE id=1");
        Scalar("SELECT COUNT(*) FROM scan_samples").Should().Be(0);
    }

    private static IEnumerable<(int Version, string Sql)> LoadScripts()
    {
        var asm = typeof(Migrator).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal)))
        {
            var version = int.Parse(name.Split("Migrations.")[1][..3]);
            using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
            yield return (version, reader.ReadToEnd());
        }
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        var m = new Migrator();
        m.Migrate(_db);
        m.Migrate(_db);
        Scalar("SELECT COUNT(*) FROM schema_version").Should().Be(1);
    }

    [Fact]
    public void Check_constraints_reject_invalid_values()
    {
        new Migrator().Migrate(_db);
        Exec("INSERT INTO drives(id,vendor,model,firmware) VALUES (1,'v','m','f')");
        Exec("INSERT INTO discs(id,toc_hash,track_count,total_sectors) VALUES (1,'h',1,100)");

        var bad = () => Exec("INSERT INTO scans(disc_id,drive_id,scanned_at,speed_x,scanner,verdict) VALUES (1,1,'2024-01-01',8,'Other','OK')");
        bad.Should().Throw<SqliteException>();
        var bad2 = () => Exec("INSERT INTO scans(disc_id,drive_id,scanned_at,speed_x,scanner,verdict) VALUES (1,1,'2024-01-01',8,'LiteOn','MAYBE')");
        bad2.Should().Throw<SqliteException>();
        var bad3 = () => Exec("INSERT INTO verifications(disc_id,drive_id,verified_at,offset_used,result) VALUES (1,1,'2024-01-01',0,'X')");
        bad3.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Drives_unique_and_delete_scan_cascades_to_samples()
    {
        new Migrator().Migrate(_db);
        Exec("INSERT INTO drives(vendor,model,firmware) VALUES ('v','m','f')");
        var dup = () => Exec("INSERT INTO drives(vendor,model,firmware) VALUES ('v','m','f')");
        dup.Should().Throw<SqliteException>();

        Exec("INSERT INTO discs(id,toc_hash,track_count,total_sectors) VALUES (1,'h',1,100)");
        Exec("INSERT INTO scans(id,disc_id,drive_id,scanned_at,speed_x,scanner,verdict) VALUES (1,1,1,'2024-01-01',8,'LiteOn','OK')");
        Exec("INSERT INTO scan_samples(scan_id,t_sec,lba,c1,c2,cu,speed_x) VALUES (1,0,0,NULL,0,0,NULL),(1,1,75,1,0,0,8)");
        var dupSample = () => Exec("INSERT INTO scan_samples(scan_id,t_sec,lba,c2,cu) VALUES (1,0,0,0,0)");
        dupSample.Should().Throw<SqliteException>();

        Exec("DELETE FROM scans WHERE id=1");
        Scalar("SELECT COUNT(*) FROM scan_samples").Should().Be(0);
    }

    [Fact]
    public void Failing_script_is_rolled_back_and_version_unchanged()
    {
        var scripts = new (int, string)[]
        {
            (1, "CREATE TABLE schema_version (version INTEGER NOT NULL); CREATE TABLE a (x INTEGER);"),
            (2, "CREATE TABLE b (x INTEGER); INSERT INTO missing_table VALUES (1);"),
        };
        var m = new Migrator(scripts);

        var act = () => m.Migrate(_db);

        act.Should().Throw<MigrationException>().Which.ScriptVersion.Should().Be(2);
        Migrator.GetCurrentVersion(_db).Should().Be(1);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='b'").Should().Be(0);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='a'").Should().Be(1);
    }

    [Fact]
    public void Failure_and_incompatibility_messages_report_the_versions()
    {
        var failing = new Migrator([(1, "CREATE TABLE schema_version (version INTEGER NOT NULL);"), (2, "INSERT INTO missing_table VALUES (1);")]);
        var failure = () => failing.Migrate(_db);
        failure.Should().Throw<MigrationException>().Which.Message.Should().Contain("002");

        using var other = Migrator.OpenConnection("Data Source=:memory:");
        using (var c = other.CreateCommand())
        {
            c.CommandText = "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version VALUES (99);";
            c.ExecuteNonQuery();
        }
        var m = new Migrator();
        var incompatible = () => m.Migrate(other);
        incompatible.Should().Throw<IncompatibleSchemaException>().Which.Message
            .Should().Contain("99").And.Contain(m.MaxSupportedVersion.ToString());
    }

    [Fact]
    public void Future_schema_version_is_rejected_without_changes()
    {
        Exec("CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version VALUES (99);");
        var m = new Migrator();

        var act = () => m.Migrate(_db);

        var ex = act.Should().Throw<IncompatibleSchemaException>().Which;
        ex.FoundVersion.Should().Be(99);
        ex.SupportedVersion.Should().Be(m.MaxSupportedVersion);
        Scalar("SELECT version FROM schema_version").Should().Be(99);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='drives'").Should().Be(0);
    }
}
