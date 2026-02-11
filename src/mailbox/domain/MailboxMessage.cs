/// <summary>
/// Represents a message received by the mailbox
/// </summary>
public class MailboxMessage
{
    public int MailboxId { get; }
    public string Endpoint { get; }
    public string Body { get; }
    public DateTime ReceivedAt { get; }

    public MailboxMessage(int mailboxId, string endpoint, string body, DateTime receivedAt)
    {
        MailboxId = mailboxId;
        Endpoint = endpoint;
        Body = body;
        ReceivedAt = receivedAt;
    }

    public MailboxMessage(int mailboxId, string endpoint, string body)
        : this(mailboxId, endpoint, body, DateTime.UtcNow)
    {
    }
}




