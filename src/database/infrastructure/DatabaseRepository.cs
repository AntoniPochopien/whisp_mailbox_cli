using Microsoft.Data.Sqlite;

class DatabaseRepository : IDatabaseRepository
{
    private const string ConnectionString = "Data Source=whisp_mailbox.db";

    public override void InitializeDatabase()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS mailboxes (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                pinhash TEXT NOT NULL
            )";

        createTableCommand.ExecuteNonQuery();
    }
    public override void AddMailbox(DbEntity<Mailbox> mailbox)
    {
        throw new NotImplementedException();
    }

    public override void DeleteMailbox(int id)
    {
        throw new NotImplementedException();
    }

    public override List<DbEntity<Mailbox>> GetAllMailboxes()
    {
        throw new NotImplementedException();
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