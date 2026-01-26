using Microsoft.Data.Sqlite;

class DatabaseRepository : IDatabaseRepository
{
    private const string _ConnectionString = "Data Source=whisp_mailbox.db";
    private SqliteConnection _Connection = null!;

    public override void InitializeDatabase()
    {
        _Connection = new SqliteConnection(_ConnectionString);
        _Connection.Open();

        var createTableCommand = _Connection.CreateCommand();
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

        var insertCommand = _Connection.CreateCommand();
        insertCommand.CommandText = @"
            INSERT INTO mailboxes (name, pinhash) VALUES (@name, @pinhash)";
        insertCommand.Parameters.AddWithValue("@name", mailbox.Entity.Name);
        insertCommand.Parameters.AddWithValue("@pinhash", mailbox.Entity.PINhash);
        insertCommand.ExecuteNonQuery();
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