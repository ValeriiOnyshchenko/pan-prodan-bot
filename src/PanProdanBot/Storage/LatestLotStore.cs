using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PanProdanBot.Auctions;

namespace PanProdanBot.Storage;

/// <summary>
/// The most recent broadcast lot per source (with Telegram photo file ids),
/// shown to every new subscriber right after the welcome message.
/// </summary>
public sealed class LatestLotStore
{
    private readonly string _connectionString;

    public LatestLotStore(IOptions<BotOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS latest_lots (
                source     TEXT PRIMARY KEY,
                lot_json   TEXT NOT NULL,
                file_ids   TEXT,
                sent_at    TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task SaveAsync(Lot lot, string[]? fileIds, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO latest_lots (source, lot_json, file_ids, sent_at) VALUES ($s, $l, $f, $t)
            ON CONFLICT(source) DO UPDATE SET lot_json = $l, file_ids = $f, sent_at = $t;
            """;
        cmd.Parameters.AddWithValue("$s", SourceOf(lot));
        cmd.Parameters.AddWithValue("$l", JsonSerializer.Serialize(lot));
        cmd.Parameters.AddWithValue("$f", fileIds is { Length: > 0 } ? JsonSerializer.Serialize(fileIds) : DBNull.Value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Remembers photo file ids obtained later (e.g. when the lot was first sent to a new subscriber).</summary>
    public async Task SaveFileIdsAsync(Lot lot, string[] fileIds, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE latest_lots SET file_ids = $f WHERE source = $s AND file_ids IS NULL;";
        cmd.Parameters.AddWithValue("$s", SourceOf(lot));
        cmd.Parameters.AddWithValue("$f", JsonSerializer.Serialize(fileIds));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(Lot Lot, string[]? FileIds)>> GetAllAsync(CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT lot_json, file_ids FROM latest_lots ORDER BY source;";
        var result = new List<(Lot, string[]?)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var lot = JsonSerializer.Deserialize<Lot>(r.GetString(0));
            if (lot is null) continue;
            var ids = r.IsDBNull(1) ? null : JsonSerializer.Deserialize<string[]>(r.GetString(1));
            result.Add((lot, ids));
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
