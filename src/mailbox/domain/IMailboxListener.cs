/// <summary>
/// Interface for mailbox HTTP listener service
/// </summary>
public interface IMailboxListener : IDisposable
{
    /// <summary>
    /// Starts the HTTP listener for the specified mailbox
    /// </summary>
    /// <param name="port">Local port to listen on</param>
    /// <param name="mailboxId">Database ID of the mailbox</param>
    /// <param name="pinHash">Hashed PIN for authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartAsync(int port, int mailboxId, string pinHash, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsListening { get; }
}

