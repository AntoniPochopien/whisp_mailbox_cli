public class HiddenService
{
    public string OnionAddress { get; }
    public string PrivateKey { get; }
    public int LocalPort { get; }
    public int VirtualPort { get; }

    public HiddenService(string onionAddress, string privateKey, int localPort, int virtualPort = 80)
    {
        OnionAddress = onionAddress;
        PrivateKey = privateKey;
        LocalPort = localPort;
        VirtualPort = virtualPort;
    }

    public string FullOnionAddress => $"{OnionAddress}.onion";
}

