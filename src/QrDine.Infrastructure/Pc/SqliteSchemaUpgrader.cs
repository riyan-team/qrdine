using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Infrastructure.Pc;

/// <summary>
/// Keeps a SQLite database (the PC edition's, and local dev's) in step with the app after an update, the way the
/// online server's migrations do for Postgres. It only adds: missing tables, columns and indexes. It never drops or
/// changes anything, so an older version of the app still runs on the upgraded file.
/// How: the app's model is built in a scratch in-memory database and compared with the file.
/// </summary>
public static class SqliteSchemaUpgrader
{
    public sealed record Result(bool Created, IReadOnlyList<string> Added, string? BackupFile);

    /// <param name="backupFile">Where to copy the database first, when there is something to add (null: no copy).</param>
    public static async Task<Result> UpgradeAsync(AppDbContext db, ILogger log, string? backupFile = null, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = (SqliteConnection)db.Database.GetDbConnection();
            if (Objects(conn).Count(o => o.Type == "table") == 0)
            {
                await db.Database.EnsureCreatedAsync(ct);
                log.LogInformation("New database made from the model");
                return new(true, [], null);
            }

            using var scratch = new SqliteConnection("Data Source=:memory:");
            scratch.Open();
            using (var cmd = scratch.CreateCommand()) { cmd.CommandText = db.Database.GenerateCreateScript(); cmd.ExecuteNonQuery(); }

            var want = Objects(scratch);
            var have = Objects(conn).Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var props = PropertiesByColumn(db.Model);
            var plan = new List<(string What, string Sql, string? Fill)>();

            foreach (var t in want.Where(o => o.Type == "table"))
            {
                if (!have.Contains(t.Name)) { plan.Add(($"table {t.Name}", t.Sql!, null)); continue; }
                var existing = Columns(conn, t.Name).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var c in Columns(scratch, t.Name).Where(c => !existing.Contains(c.Name)))
                {
                    props.TryGetValue((t.Name, c.Name), out var p);
                    var (sql, fill) = AddColumn(t.Name, c, p);
                    plan.Add(($"column {t.Name}.{c.Name}", sql, fill));
                }
            }
            foreach (var i in want.Where(o => o.Type == "index" && o.Sql is not null && !have.Contains(o.Name)))
                plan.Add(($"index {i.Name}", i.Sql!, null));

            if (plan.Count == 0) return new(false, [], null);

            if (backupFile is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupFile))!);
                using var copy = conn.CreateCommand();
                copy.CommandText = "VACUUM INTO $f";
                copy.Parameters.AddWithValue("$f", backupFile);
                copy.ExecuteNonQuery();
                log.LogInformation("Database copied to {File} before the upgrade", backupFile);
            }

            using (var tx = conn.BeginTransaction())
            {
                foreach (var (what, sql, fill) in plan)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                    if (fill is not null) { cmd.CommandText = fill; cmd.ExecuteNonQuery(); }
                }
                tx.Commit();
            }
            var added = plan.Select(p => p.What).ToList();
            log.LogInformation("Database upgraded: added {Count} ({List})", added.Count, string.Join(", ", added));
            return new(false, added, backupFile);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private sealed record DbObject(string Type, string Name, string? Sql);
    private sealed record Column(string Name, string Type, bool NotNull, string? Default);

    private static List<DbObject> Objects(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE type IN ('table','index') AND name NOT LIKE 'sqlite_%' ORDER BY rowid";
        using var r = cmd.ExecuteReader();
        var list = new List<DbObject>();
        while (r.Read()) list.Add(new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    private static List<Column> Columns(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        using var r = cmd.ExecuteReader();
        var list = new List<Column>();
        while (r.Read()) list.Add(new(r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetInt64(3) != 0, r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    private static Dictionary<(string, string), IProperty> PropertiesByColumn(IModel model)
    {
        var map = new Dictionary<(string, string), IProperty>(new PairComparer());
        foreach (var et in model.GetEntityTypes())
        {
            if (et.GetTableName() is not string table) continue;
            var store = StoreObjectIdentifier.Table(table, et.GetSchema());
            foreach (var p in et.GetProperties())
                if (p.GetColumnName(store) is string col) map[(table, col)] = p;
        }
        return map;
    }

    /// <summary>
    /// SQLite can only add a column that may be empty or has a default. A required column gets the model's default,
    /// else the type's empty value (0, false, "", the first enum value, the earliest date). A required id (Guid)
    /// column is filled with a new random id on every existing row, so no two rows share one.
    /// </summary>
    private static (string Sql, string? Fill) AddColumn(string table, Column c, IProperty? p)
    {
        var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{c.Name}\" {c.Type}";
        if (!c.NotNull) return (c.Default is null ? sql : $"{sql} DEFAULT {c.Default}", null);
        if (c.Default is not null) return ($"{sql} NOT NULL DEFAULT {c.Default}", null);

        var clr = p is null ? null : Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
        if (clr == typeof(Guid))
            return ($"{sql} NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'",
                $"UPDATE \"{table}\" SET \"{c.Name}\" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || " +
                $"substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)))");

        string literal;
        if (p is not null && clr is not null)
        {
            object? value = p.GetDefaultValue();
            if (value is null)
                value = clr == typeof(string) ? "" : clr.IsEnum ? Enum.GetValues(clr).GetValue(0) : Activator.CreateInstance(clr);
            literal = ((RelationalTypeMapping)p.GetTypeMapping()).GenerateSqlLiteral(value);
        }
        else
        {
            var type = c.Type.ToUpperInvariant();
            literal = type.Contains("INT") ? "0" : type.Contains("REAL") ? "0.0" : type.Contains("BLOB") ? "X''" : "''";
        }
        return ($"{sql} NOT NULL DEFAULT {literal}", null);
    }

    private sealed class PairComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) k) =>
            HashCode.Combine(k.Item1.ToUpperInvariant(), k.Item2.ToUpperInvariant());
    }
}
