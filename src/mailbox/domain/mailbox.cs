public class Mailbox
{
    public string Name { get; }
    public string PINhash { get; }
    public MailboxStatus Status { get; }

    public Mailbox(string name, string pinhash)
    {
        Name = name;
        PINhash = pinhash;
        Status = MailboxStatus.Offline;
    }

    public Mailbox(int id, string name, string pinhash)
    {
        Name = name;
        PINhash = pinhash;
        Status = MailboxStatus.Offline;
    }
}
