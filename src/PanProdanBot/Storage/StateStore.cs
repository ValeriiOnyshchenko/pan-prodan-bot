using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Storage;

/// <summary>Small key/value store for bot state that must survive redeploys (e.g. the last announced filter signature).</summary>
public sealed class StateStore
{
    private readonly string _connectionString;

    public StateStore(IOptions<BotOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS bot_state (key TEXT PRIMARY KEY, value TEXT NOT NULL, updated TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM bot_state WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bot_state (key, value, updated) VALUES ($k, $v, $t)
            ON CONFLICT(key) DO UPDATE SET value = $v, updated = $t;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
