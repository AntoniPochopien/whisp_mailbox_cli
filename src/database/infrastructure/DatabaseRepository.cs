using Microsoft.Data.Sqlite;

public class DatabaseRepository : IDatabaseRepository
{
    private const string _ConnectionString = "Data Source=whisp_mailbox.db";
    private readonly SqliteConnection _Connection;

    public DatabaseRepository()
    {
        _Connection = new SqliteConnection(_ConnectionString);
        _Connection.Open();
    }

    public override void InitializeDatabase()
    {
        var createTableCommand = _Connection.CreateCommand();
        createTableCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS mailboxes (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                pinhash TEXT NOT NULL,
                port INTEGER NOT NULL DEFAULT 0,
                onion_private_key TEXT,
                onion_address TEXT
            )";
        createTableCommand.ExecuteNonQuery();

        // Migration: add new columns if they don't exist
        MigrateAddColumn("port", "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumn("onion_private_key", "TEXT");
        MigrateAddColumn("onion_address", "TEXT");
    }

    private void MigrateAddColumn(string columnName, string columnDef)
    {
        try
        {
            var cmd = _Connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE mailboxes ADD COLUMN {columnName} {columnDef}";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists, ignore
        }
    }

    public override DbEntity<Mailbox> AddMailbox(Mailbox mailbox)
    {
        var insertCommand = _Connection.CreateCommand();
        insertCommand.CommandText = @"
            INSERT INTO mailboxes (name, pinhash, port, onion_private_key, onion_address) 
            VALUES (@name, @pinhash, @port, @onion_private_key, @onion_address);
            SELECT last_insert_rowid();";
        insertCommand.Parameters.AddWithValue("@name", mailbox.Name);
        insertCommand.Parameters.AddWithValue("@pinhash", mailbox.PINhash);
        insertCommand.Parameters.AddWithValue("@port", mailbox.Port);
        insertCommand.Parameters.AddWithValue("@onion_private_key", (object?)mailbox.OnionPrivateKey ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("@onion_address", (object?)mailbox.OnionAddress ?? DBNull.Value);
        
        var id = Convert.ToInt32(insertCommand.ExecuteScalar());
        return new DbEntity<Mailbox>(id, mailbox);
    }

    public override void DeleteMailbox(int id)
    {
        var deleteCommand = _Connection.CreateCommand();
        deleteCommand.CommandText = "DELETE FROM mailboxes WHERE id = @id";
        deleteCommand.Parameters.AddWithValue("@id", id);
        deleteCommand.ExecuteNonQuery();
    }

    public override List<DbEntity<Mailbox>> GetAllMailboxes()
    {
        var mailboxes = new List<DbEntity<Mailbox>>();

        var selectCommand = _Connection.CreateCommand();
        selectCommand.CommandText = "SELECT id, name, pinhash, port, onion_private_key, onion_address FROM mailboxes";

        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var mailbox = ReadMailboxFromReader(reader);
                mailboxes.Add(mailbox);
            }
        }

        return mailboxes;
    }

    public override DbEntity<Mailbox>? GetMailbox(int id)
    {
        var selectCommand = _Connection.CreateCommand();
        selectCommand.CommandText = "SELECT id, name, pinhash, port, onion_private_key, onion_address FROM mailboxes WHERE id = @id";
        selectCommand.Parameters.AddWithValue("@id", id);

        using var reader = selectCommand.ExecuteReader();
        if (reader.Read())
        {
            return ReadMailboxFromReader(reader);
        }
        return null;
    }

    public override DbEntity<Mailbox>? GetMailboxByName(string name)
    {
        var selectCommand = _Connection.CreateCommand();
        selectCommand.CommandText = "SELECT id, name, pinhash, port, onion_private_key, onion_address FROM mailboxes WHERE name = @name";
        selectCommand.Parameters.AddWithValue("@name", name);

        using var reader = selectCommand.ExecuteReader();
        if (reader.Read())
        {
            return ReadMailboxFromReader(reader);
        }
        return null;
    }

    public override void UpdateMailbox(DbEntity<Mailbox> mailboxEntity)
    {
        var mailbox = mailboxEntity.Entity;
        var updateCommand = _Connection.CreateCommand();
        updateCommand.CommandText = @"
            UPDATE mailboxes 
            SET name = @name, pinhash = @pinhash, port = @port, 
                onion_private_key = @onion_private_key, onion_address = @onion_address
            WHERE id = @id";
        updateCommand.Parameters.AddWithValue("@id", mailboxEntity.Id);
        updateCommand.Parameters.AddWithValue("@name", mailbox.Name);
        updateCommand.Parameters.AddWithValue("@pinhash", mailbox.PINhash);
        updateCommand.Parameters.AddWithValue("@port", mailbox.Port);
        updateCommand.Parameters.AddWithValue("@onion_private_key", (object?)mailbox.OnionPrivateKey ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@onion_address", (object?)mailbox.OnionAddress ?? DBNull.Value);
        updateCommand.ExecuteNonQuery();
    }

    public override void UpdateMailboxHiddenService(int id, string privateKey, string onionAddress)
    {
        var updateCommand = _Connection.CreateCommand();
        updateCommand.CommandText = @"
            UPDATE mailboxes 
            SET onion_private_key = @onion_private_key, onion_address = @onion_address
            WHERE id = @id";
        updateCommand.Parameters.AddWithValue("@id", id);
        updateCommand.Parameters.AddWithValue("@onion_private_key", privateKey);
        updateCommand.Parameters.AddWithValue("@onion_address", onionAddress);
        updateCommand.ExecuteNonQuery();
    }

    private static DbEntity<Mailbox> ReadMailboxFromReader(SqliteDataReader reader)
    {
        var id = reader.GetInt32(0);
        var name = reader.GetString(1);
        var pinhash = reader.GetString(2);
        var port = reader.GetInt32(3);
        var onionPrivateKey = reader.IsDBNull(4) ? null : reader.GetString(4);
        var onionAddress = reader.IsDBNull(5) ? null : reader.GetString(5);

        var mailbox = new Mailbox(id, name, pinhash, port, onionPrivateKey, onionAddress);
        return new DbEntity<Mailbox>(id, mailbox);
    }
}