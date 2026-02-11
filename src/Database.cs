using Microsoft.Data.Sqlite;

/// <summary>
/// Simple SQLite storage for onion service config and incoming messages.
/// </summary>
public class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    public Database(string dbPath = "whisp_mailbox.db")
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY,
                endpoint TEXT NOT NULL,
                body TEXT NOT NULL,
                received_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // --- Config (onion private key, address, pin hash) ---

    public (string? privateKey, string? onionAddress) GetOnionConfig()
    {
        return (GetConfig("onion_private_key"), GetConfig("onion_address"));
    }

    public void SaveOnionConfig(string privateKey, string onionAddress)
    {
        SetConfig("onion_private_key", privateKey);
        SetConfig("onion_address", onionAddress);
    }

    public string? GetPinHash() => GetConfig("pin_hash");

    public void SavePinHash(string pinHash) => SetConfig("pin_hash", pinHash);

    private string? GetConfig(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM config WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetConfig(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO config (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    // --- Messages ---

    public void AddMessage(string endpoint, string body)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO messages (endpoint, body, received_at)
            VALUES (@endpoint, @body, @received_at)
            """;
        cmd.Parameters.AddWithValue("@endpoint", endpoint);
        cmd.Parameters.AddWithValue("@body", body);
        cmd.Parameters.AddWithValue("@received_at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<(int id, string endpoint, string body, DateTime receivedAt)> GetAllMessages()
    {
        var messages = new List<(int, string, string, DateTime)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, endpoint, body, received_at FROM messages ORDER BY received_at ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTime.Parse(reader.GetString(3))
            ));
        }
        return messages;
    }

    public void DeleteAllMessages()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM messages";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}

