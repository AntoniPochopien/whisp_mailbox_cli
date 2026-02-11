public abstract class IDatabaseRepository
{
    abstract public void InitializeDatabase();
    abstract public DbEntity<Mailbox>? GetMailbox(int id);
    abstract public DbEntity<Mailbox>? GetMailboxByName(string name);
    abstract public List<DbEntity<Mailbox>> GetAllMailboxes();
    abstract public DbEntity<Mailbox> AddMailbox(Mailbox mailbox);
    abstract public void UpdateMailbox(DbEntity<Mailbox> mailbox);
    abstract public void UpdateMailboxHiddenService(int id, string privateKey, string onionAddress);
    abstract public void DeleteMailbox(int id);
    
    // Message storage methods
    abstract public DbEntity<MailboxMessage> AddMessage(MailboxMessage message);
    abstract public List<DbEntity<MailboxMessage>> GetMessagesByMailboxId(int mailboxId);
    abstract public void DeleteMessagesByMailboxId(int mailboxId);
}