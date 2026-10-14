using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PanProdanBot.Auctions;

namespace PanProdanBot.Storage;

/// <summary>
/// The last <see cref="KeepPerSource"/> broadcast lots of each source (with Telegram photo file ids).
/// A new subscriber gets the newest one per source that still passes the current filters and hasn't expired.
/// Keeping several (not just one) matters: the newest may have expired or be excluded by a filter added later.
/// </summary>
public sealed class RecentLotStore
{
    public const int KeepPerSource = 20;
    private readonly string _connectionString;

    public RecentLotStore(IOptions<BotOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS recent_lots (
                key        TEXT PRIMARY KEY,
                source     TEXT NOT NULL,
                lot_json   TEXT NOT NULL,
                file_ids   TEXT,
                sent_at    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_recent_lots_source ON recent_lots(source, sent_at);
            """;
        cmd.ExecuteNonQuery();

        // One-time migration from the old single-lot table.
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'latest_lots';";
        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO recent_lots (key, source, lot_json, file_ids, sent_at)
                SELECT json_extract(lot_json, '$.Key'), source, lot_json, file_ids, sent_at FROM latest_lots;
                DROP TABLE latest_lots;
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public async Task SaveAsync(Lot lot, string[]? fileIds, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO recent_lots (key, source, lot_json, file_ids, sent_at) VALUES ($k, $s, $l, $f, $t)
            ON CONFLICT(key) DO UPDATE SET lot_json = $l, file_ids = COALESCE($f, file_ids), sent_at = $t;
            DELETE FROM recent_lots WHERE source = $s AND key NOT IN
                (SELECT key FROM recent_lots WHERE source = $s ORDER BY sent_at DESC LIMIT $keep);
            """;
        cmd.Parameters.AddWithValue("$k", lot.Key);
        cmd.Parameters.AddWithValue("$s", SourceOf(lot));
        cmd.Parameters.AddWithValue("$l", JsonSerializer.Serialize(lot));
        cmd.Parameters.AddWithValue("$f", fileIds is { Length: > 0 } ? JsonSerializer.Serialize(fileIds) : DBNull.Value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$keep", KeepPerSource);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Remembers photo file ids obtained later (when the lot was first uploaded for a new subscriber).</summary>
    public async Task SaveFileIdsAsync(string key, string[] fileIds, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE recent_lots SET file_ids = $f WHERE key = $k AND file_ids IS NULL;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$f", JsonSerializer.Serialize(fileIds));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>All stored lots, newest first.</summary>
    public async Task<IReadOnlyList<(Lot Lot, string Source, string[]? FileIds)>> GetAllAsync(CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT lot_json, source, file_ids FROM recent_lots ORDER BY sent_at DESC;";
        var result = new List<(Lot, string, string[]?)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (JsonSerializer.Deserialize<Lot>(r.GetString(0)) is not { } lot) continue;
            var ids = r.IsDBNull(2) ? null : JsonSerializer.Deserialize<string[]>(r.GetString(2));
            result.Add((lot, r.GetString(1), ids));
        }
        return result;
    }

    private static string SourceOf(Lot lot) => lot.Key.Split(':', 2)[0];

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
