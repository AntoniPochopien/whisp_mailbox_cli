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
                pinhash TEXT NOT NULL
            )";

        createTableCommand.ExecuteNonQuery();
    }
    public override DbEntity<Mailbox> AddMailbox(Mailbox mailbox)
    {
        var insertCommand = _Connection.CreateCommand();
        insertCommand.CommandText = @"
            INSERT INTO mailboxes (name, pinhash) VALUES (@name, @pinhash)";
        insertCommand.Parameters.AddWithValue("@name", mailbox.Name);
        insertCommand.Parameters.AddWithValue("@pinhash", mailbox.PINhash);
        insertCommand.ExecuteNonQuery();
        return new DbEntity<Mailbox>(0, mailbox);
    }

    public override void DeleteMailbox(int id)
    {
        throw new NotImplementedException();
    }

    public override List<DbEntity<Mailbox>> GetAllMailboxes()
    {
        var mailboxes = new List<DbEntity<Mailbox>>();

        var selectCommand = _Connection.CreateCommand();
        selectCommand.CommandText = "SELECT id, name, pinhash FROM mailboxes";

        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var name = reader.GetString(1);
                var pinhash = reader.GetString(2);

                var mailbox = new Mailbox(id, name, pinhash);
                mailboxes.Add(new DbEntity<Mailbox>(id, mailbox));
            }
        }

        return mailboxes;
    }

    public override DbEntity<Mailbox> GetMailbox(int id)
    {
        throw new NotImplementedException();
    }

    public override void UpdateMailbox(DbEntity<Mailbox> mailbox)
    {
        throw new NotImplementedException();
    }
}