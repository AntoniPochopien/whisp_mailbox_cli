/// <summary>
/// Interface for TOR hidden service management
/// </summary>
public interface ITorRepository : IDisposable
{
    /// <summary>
    /// Connects to the TOR control port
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Authenticates with the TOR control port
    /// </summary>
    Task AuthenticateAsync(string? password = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new hidden service with a new key pair
    /// </summary>
    Task<HiddenService> CreateHiddenServiceAsync(int localPort, int virtualPort = 80, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Attaches an existing hidden service using a stored private key
    /// </summary>
    Task<HiddenService> AttachHiddenServiceAsync(string privateKey, int localPort, int virtualPort = 80, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a hidden service by its onion address
    /// </summary>
    Task RemoveHiddenServiceAsync(string onionAddress, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if connected to TOR control port
    /// </summary>
    bool IsConnected { get; }
}

