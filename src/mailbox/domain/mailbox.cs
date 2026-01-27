public class Mailbox
{
    public int Id { get; }
    public string Name { get; }
    public string PINhash { get; }
    public MailboxStatus Status { get; }

    public Mailbox(int id, string name, string PINhash)
    {
        Id = id;
        Name = name;
        this.PINhash = PINhash;
        Status = MailboxStatus.Offline;
    }
}
