public interface ITorManager
{
    /// <summary>
    /// Checks if TOR is available on the system
    /// </summary>
    bool IsTorInstalled();
    
    /// <summary>
    /// Gets the path to the TOR executable
    /// </summary>
    string? GetTorPath();
    
    /// <summary>
    /// Downloads TOR Expert Bundle to the application directory
    /// </summary>
    Task DownloadTorAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Starts the TOR process with control port enabled
    /// </summary>
    Task<bool> StartTorAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stops the managed TOR process
    /// </summary>
    void StopTor();
    
    /// <summary>
    /// Checks if TOR control port is accessible
    /// </summary>
    Task<bool> IsTorRunningAsync(CancellationToken cancellationToken = default);
}

