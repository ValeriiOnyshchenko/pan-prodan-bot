using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Storage;

/// <summary>Remembers every lot Пан Продан has already looked at, so each lot is broadcast at most once.</summary>
public sealed class SeenLotStore
{
    private readonly string _connectionString;

    public SeenLotStore(IOptions<BotOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS seen_lots (
                key        TEXT PRIMARY KEY,
                source     TEXT NOT NULL,
                outcome    TEXT NOT NULL,   -- broadcast | rejected | baseline
                seen_at    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_seen_lots_source ON seen_lots(source);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<HashSet<string>> GetKeysAsync(string source, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT key FROM seen_lots WHERE source = $s;";
        cmd.Parameters.AddWithValue("$s", source);
        var keys = new HashSet<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) keys.Add(r.GetString(0));
        return keys;
    }

    public Task MarkAsync(string key, string source, string outcome, CancellationToken ct) =>
        MarkManyAsync([key], source, outcome, ct);

    public async Task MarkManyAsync(IEnumerable<string> keys, string source, string outcome, CancellationToken ct)
    {
        await using var db = Open();
        await using var tx = db.BeginTransaction();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO seen_lots (key, source, outcome, seen_at) VALUES ($k, $s, $o, $t);";
        var k = cmd.Parameters.Add("$k", SqliteType.Text);
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$o", outcome);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        foreach (var key in keys)
        {
            k.Value = key;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
