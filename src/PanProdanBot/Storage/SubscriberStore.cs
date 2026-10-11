using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Storage;

/// <summary>Stores chat ids of users who pressed Start. Thread-safe; one short-lived connection per call.</summary>
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
        cmd.CommandText = "DELETE FROM subscribers WHERE chat_id = $id;";
        cmd.Parameters.AddWithValue("$id", chatId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
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
