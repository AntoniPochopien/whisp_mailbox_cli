public abstract class IDatabaseRepository
{
    abstract public void InitializeDatabase();
    abstract public DbEntity<Mailbox> GetMailbox(int id);
    abstract public List<DbEntity<Mailbox>> GetAllMailboxes();
    abstract public void AddMailbox(DbEntity<Mailbox> mailbox);
    abstract public void UpdateMailbox(DbEntity<Mailbox> mailbox);
    abstract public void DeleteMailbox(int id);
}