using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Storage;

/// <summary>
/// Subscribers (chat ids of users who pressed Start) and the per-user delivery log —
/// which lots each subscriber already got, so nobody receives the same lot twice.
/// Thread-safe; one short-lived connection per call.
/// </summary>
public sealed class SubscriberStore
{
    private readonly string _connectionString;

    public SubscriberStore(IOptions<BotOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS subscribers (
                chat_id     INTEGER PRIMARY KEY,
                username    TEXT,
                first_name  TEXT,
                subscribed  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS deliveries (
                chat_id     INTEGER NOT NULL,
                lot_key     TEXT NOT NULL,
                sent_at     TEXT NOT NULL,
                PRIMARY KEY (chat_id, lot_key)
            );
            CREATE INDEX IF NOT EXISTS ix_deliveries_lot ON deliveries(lot_key);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task AddAsync(long chatId, string? username, string? firstName, CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO subscribers (chat_id, username, first_name, subscribed)
            VALUES ($id, $u, $f, $s)
            ON CONFLICT(chat_id) DO UPDATE SET username = $u, first_name = $f;
            """;
        cmd.Parameters.AddWithValue("$id", chatId);
        cmd.Parameters.AddWithValue("$u", (object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", (object?)firstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> RemoveAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        // Unsubscribing forgets the user's delivery history too: a later /start begins from scratch.
        cmd.CommandText = "DELETE FROM deliveries WHERE chat_id = $id; DELETE FROM subscribers WHERE chat_id = $id;";
        cmd.Parameters.AddWithValue("$id", chatId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Chats that already received this lot.</summary>
    public async Task<HashSet<long>> GetRecipientsAsync(string lotKey, CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT chat_id FROM deliveries WHERE lot_key = $k;";
        cmd.Parameters.AddWithValue("$k", lotKey);
        var result = new HashSet<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(r.GetInt64(0));
        return result;
    }

    /// <summary>Lots this chat already received.</summary>
    public async Task<HashSet<string>> GetDeliveredAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT lot_key FROM deliveries WHERE chat_id = $c;";
        cmd.Parameters.AddWithValue("$c", chatId);
        var result = new HashSet<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(r.GetString(0));
        return result;
    }

    public async Task MarkDeliveredAsync(long chatId, string lotKey, CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO deliveries (chat_id, lot_key, sent_at) VALUES ($c, $k, $t);";
        cmd.Parameters.AddWithValue("$c", chatId);
        cmd.Parameters.AddWithValue("$k", lotKey);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Drops delivery records older than <paramref name="age"/> (those lots are long closed).</summary>
    public async Task PruneDeliveriesAsync(TimeSpan age, CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM deliveries WHERE sent_at < $t;";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.Subtract(age).ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<long>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT chat_id FROM subscribers;";
        var result = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetInt64(0));
        return result;
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
