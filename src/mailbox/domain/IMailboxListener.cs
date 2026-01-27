/// <summary>
/// Interface for mailbox HTTP listener service
/// </summary>
public interface IMailboxListener : IDisposable
{
    Task StartAsync(int port, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsListening { get; }
}

