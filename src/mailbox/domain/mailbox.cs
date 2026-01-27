public class Mailbox
{
    public string Name { get; }
    public string PINhash { get; }
    public MailboxStatus Status { get; private set; }
    
    /// <summary>
    /// TOR hidden service private key (ED25519-V3:base64key format)
    /// Used to restore the same .onion address after restart
    /// </summary>
    public string? OnionPrivateKey { get; private set; }
    
    /// <summary>
    /// The .onion address (without .onion suffix)
    /// </summary>
    public string? OnionAddress { get; private set; }
    
    /// <summary>
    /// The local port this mailbox listens on
    /// </summary>
    public int Port { get; }

    public Mailbox(string name, string pinhash, int port = 0)
    {
        Name = name;
        PINhash = pinhash;
        Port = port;
        Status = MailboxStatus.Offline;
    }

    public Mailbox(int id, string name, string pinhash, int port, string? onionPrivateKey, string? onionAddress)
    {
        Name = name;
        PINhash = pinhash;
        Port = port;
        OnionPrivateKey = onionPrivateKey;
        OnionAddress = onionAddress;
        Status = MailboxStatus.Offline;
    }

    /// <summary>
    /// Sets the hidden service details after creating/attaching a TOR hidden service
    /// </summary>
    public void SetHiddenService(string privateKey, string onionAddress)
    {
        OnionPrivateKey = privateKey;
        OnionAddress = onionAddress;
    }

    /// <summary>
    /// Marks the mailbox as online
    /// </summary>
    public void SetOnline() => Status = MailboxStatus.Online;

    /// <summary>
    /// Marks the mailbox as offline
    /// </summary>
    public void SetOffline() => Status = MailboxStatus.Offline;

    /// <summary>
    /// Gets the full .onion URL if available
    /// </summary>
    public string? FullOnionAddress => OnionAddress != null ? $"{OnionAddress}.onion" : null;
    
    /// <summary>
    /// Returns true if this mailbox has a hidden service configured
    /// </summary>
    public bool HasHiddenService => !string.IsNullOrEmpty(OnionPrivateKey);
}
