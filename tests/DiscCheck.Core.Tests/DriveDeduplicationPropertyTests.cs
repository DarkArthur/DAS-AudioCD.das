using DiscCheck.Data;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Core.Tests;

public class DriveDeduplicationPropertyTests
{
    private static SqliteConnection NewDb()
    {
        var db = Migrator.OpenConnection("Data Source=:memory:");
        new Migrator().Migrate(db);
        return db;
    }

    // Small domain so that repeated triples are frequent.
    private static (string Vendor, string Model, string Firmware) Triple(byte b) =>
        ("V" + (b % 3), "M" + (b / 3 % 3), "F" + (b / 9 % 3));

    // Feature: disccheck, Property 18: Idempotenza della deduplicazione dei Drive
    // **Validates: Requirements 4.5**
    [Property(MaxTest = 100)]
    public bool RepeatedInsertsOfSameTriple_ReturnSameIdAndSingleRow(byte[] picks)
    {
        picks ??= [];
        using var db = NewDb();
        var repo = new DriveRepository(db);

        var ids = new Dictionary<(string, string, string), long>();
        foreach (var p in picks)
        {
            var t = Triple(p);
            var id = repo.AddIfMissing(new DriveRecord(0, t.Vendor, t.Model, t.Firmware));
            if (ids.TryGetValue(t, out var first))
            {
                if (first != id) return false;
            }
            else
            {
                ids[t] = id;
            }
        }

        var rows = repo.List();
        return rows.Count == ids.Count
            && rows.Select(r => r.Id).Distinct().Count() == rows.Count
            && rows.All(r => ids[(r.Vendor, r.Model, r.Firmware)] == r.Id);
    }

    // Feature: disccheck, Property 18: Idempotenza della deduplicazione dei Drive
    // **Validates: Requirements 4.5**
    [Property(MaxTest = 100)]
    public bool InsertionOrderDoesNotChangeRowCount(byte[] picks)
    {
        picks ??= [];
        static int Count(IEnumerable<byte> seq)
        {
            using var db = NewDb();
            var repo = new DriveRepository(db);
            foreach (var p in seq)
            {
                var t = Triple(p);
                repo.AddIfMissing(new DriveRecord(0, t.Vendor, t.Model, t.Firmware));
            }
            return repo.List().Count;
        }

        var distinct = picks.Select(Triple).Distinct().Count();
        return Count(picks) == distinct
            && Count(picks.Reverse()) == distinct
            && Count(picks.Concat(picks)) == distinct;
    }
}
