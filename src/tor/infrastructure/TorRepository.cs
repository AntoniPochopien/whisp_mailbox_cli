using System.Net.Sockets;
using System.Text;

public class TorRepository : ITorRepository
{
    private readonly string _controlHost;
    private readonly int _controlPort;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _disposed;

    public bool IsConnected => _client?.Connected ?? false;

    public TorRepository(string controlHost = "127.0.0.1", int controlPort = 9051)
    {
        _controlHost = controlHost;
        _controlPort = controlPort;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;

        _client = new TcpClient();
        await _client.ConnectAsync(_controlHost, _controlPort, cancellationToken);
        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };
    }

    public async Task AuthenticateAsync(string? password = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        string authCommand = string.IsNullOrEmpty(password)
            ? "AUTHENTICATE"
            : $"AUTHENTICATE \"{password}\"";

        await SendCommandAsync(authCommand, cancellationToken);
        var response = await ReadResponseAsync(cancellationToken);

        if (!response.StartsWith("250"))
        {
            throw new InvalidOperationException($"TOR authentication failed: {response}");
        }
    }

    public async Task<HiddenService> CreateHiddenServiceAsync(int localPort, int virtualPort = 80, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // ADD_ONION NEW:ED25519-V3 Port=virtualPort,127.0.0.1:localPort
        var command = $"ADD_ONION NEW:ED25519-V3 Port={virtualPort},127.0.0.1:{localPort}";
        await SendCommandAsync(command, cancellationToken);

        return await ParseAddOnionResponseAsync(localPort, virtualPort, cancellationToken);
    }

    public async Task<HiddenService> AttachHiddenServiceAsync(string privateKey, int localPort, int virtualPort = 80, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // ADD_ONION ED25519-V3:privateKey Port=virtualPort,127.0.0.1:localPort
        var command = $"ADD_ONION {privateKey} Port={virtualPort},127.0.0.1:{localPort}";
        await SendCommandAsync(command, cancellationToken);

        return await ParseAddOnionResponseAsync(localPort, virtualPort, cancellationToken);
    }

    public async Task RemoveHiddenServiceAsync(string onionAddress, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Remove .onion suffix if present
        var serviceId = onionAddress.Replace(".onion", "");
        var command = $"DEL_ONION {serviceId}";
        await SendCommandAsync(command, cancellationToken);

        var response = await ReadResponseAsync(cancellationToken);
        if (!response.StartsWith("250"))
        {
            throw new InvalidOperationException($"Failed to remove hidden service: {response}");
        }
    }

    private async Task<HiddenService> ParseAddOnionResponseAsync(int localPort, int virtualPort, CancellationToken cancellationToken)
    {
        string? serviceId = null;
        string? privateKey = null;

        // Read multiline response
        while (true)
        {
            var line = await _reader!.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith("250-ServiceID="))
            {
                serviceId = line.Substring("250-ServiceID=".Length);
            }
            else if (line.StartsWith("250-PrivateKey="))
            {
                privateKey = line.Substring("250-PrivateKey=".Length);
            }
            else if (line.StartsWith("250 "))
            {
                // End of response
                break;
            }
            else if (line.StartsWith("5"))
            {
                throw new InvalidOperationException($"TOR error: {line}");
            }
        }

        if (serviceId == null)
        {
            throw new InvalidOperationException("Failed to get service ID from TOR response");
        }

        // If we're attaching an existing service, we won't get a new private key
        privateKey ??= "EXISTING";

        return new HiddenService(serviceId, privateKey, localPort, virtualPort);
    }

    private async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        EnsureConnected();
        await _writer!.WriteLineAsync(command.AsMemory(), cancellationToken);
    }

    private async Task<string> ReadResponseAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        var response = await _reader!.ReadLineAsync(cancellationToken);
        return response ?? throw new InvalidOperationException("No response from TOR");
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to TOR control port. Call ConnectAsync first.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
    }
}

