/// <summary>
/// Interface for TOR hidden service management
/// </summary>
public interface ITorRepository : IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task AuthenticateAsync(string? password = null, CancellationToken cancellationToken = default);
    Task<HiddenService> CreateHiddenServiceAsync(int localPort, int virtualPort = 80, CancellationToken cancellationToken = default);
    Task<HiddenService> AttachHiddenServiceAsync(string privateKey, int localPort, int virtualPort = 80, CancellationToken cancellationToken = default);
    Task RemoveHiddenServiceAsync(string onionAddress, CancellationToken cancellationToken = default);
    bool IsConnected { get; }
}

