using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Manages Tor: auto-download, auto-start, and control port communication.
/// </summary>
public class TorService : IDisposable
{
    private const string TorVersion = "14.0.4";
    private const int ControlPort = 9051;

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "whisp_mailbox");

    private static readonly string TorDirectory = Path.Combine(AppDataPath, "tor");
    private static readonly string TorDataDirectory = Path.Combine(AppDataPath, "tor_data");

    private readonly string _controlHost;
    private readonly int _controlPort;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _torProcess;
    private bool _disposed;

    public bool IsConnected => _client?.Connected ?? false;

    public TorService(string controlHost = "127.0.0.1", int controlPort = ControlPort)
    {
        _controlHost = controlHost;
        _controlPort = controlPort;
    }

    // ── Tor lifecycle ──────────────────────────────────────────────

    /// <summary>
    /// Ensures Tor is installed, running, and connected. Does everything needed.
    /// </summary>
    public async Task EnsureTorReadyAsync(CancellationToken ct = default)
    {
        // 1. Check if Tor control port is already reachable
        if (await IsTorRunningAsync(ct))
        {
            Console.WriteLine("[+] Tor is already running.");
            await ConnectAsync(ct);
            await AuthenticateAsync(ct: ct);
            return;
        }

        // 2. Check if Tor is installed, if not download it
        if (GetTorPath() == null)
        {
            Console.WriteLine("[*] Tor not found, downloading...");
            await DownloadTorAsync(ct);
            Console.WriteLine("[+] Tor downloaded.");
        }

        // 3. Start Tor
        Console.WriteLine("[*] Starting Tor (this may take up to 60s)...");
        var started = await StartTorAsync(ct);
        if (!started)
            throw new InvalidOperationException("Failed to start Tor. Timed out waiting for control port.");

        Console.WriteLine("[+] Tor started.");

        // 4. Connect & authenticate
        await ConnectAsync(ct);
        await AuthenticateAsync(ct: ct);
    }

    public async Task<bool> IsTorRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var probe = new TcpClient();
            await probe.ConnectAsync(_controlHost, _controlPort, ct);
            return true;
        }
        catch { return false; }
    }

    private string? GetTorPath()
    {
        // Check our local download first
        foreach (var path in GetLocalTorPaths())
        {
            if (File.Exists(path)) return path;
        }

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tor.exe" : "tor";
        foreach (var dir in pathDirs)
        {
            var p = Path.Combine(dir, exeName);
            if (File.Exists(p)) return p;
        }

        // Common Windows paths
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var common = new[]
            {
                @"C:\Program Files\Tor\tor.exe",
                @"C:\Program Files (x86)\Tor\tor.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            };
            foreach (var p in common)
                if (File.Exists(p)) return p;
        }

        return null;
    }

    private static string[] GetLocalTorPaths()
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tor.exe" : "tor";
        return [
            Path.Combine(TorDirectory, "tor", exe),
            Path.Combine(TorDirectory, exe),
            Path.Combine(TorDirectory, "Tor", exe),
        ];
    }

    private async Task DownloadTorAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(AppDataPath);

        var (downloadUrl, archiveName) = GetDownloadInfo();
        var archivePath = Path.Combine(AppDataPath, archiveName);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        long downloaded = 0;

        await using var content = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0)
            {
                var pct = (int)(downloaded * 100 / total);
                Console.Write($"\r[*] Downloading Tor... {pct}%");
            }
        }
        Console.WriteLine();
        file.Close();

        // Extract
        if (Directory.Exists(TorDirectory))
            Directory.Delete(TorDirectory, true);
        Directory.CreateDirectory(TorDirectory);

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{TorDirectory}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        });

        if (proc != null)
        {
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"Failed to extract Tor: {err}");
            }
        }

        File.Delete(archivePath);
    }

    private async Task<bool> StartTorAsync(CancellationToken ct)
    {
        var torPath = GetTorPath();
        if (torPath == null) return false;

        Directory.CreateDirectory(TorDataDirectory);

        var torrcPath = Path.Combine(TorDataDirectory, "torrc");
        await File.WriteAllTextAsync(torrcPath, $"""
            DataDirectory {TorDataDirectory}
            ControlPort {ControlPort}
            SocksPort 9050
            """, ct);

        _torProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = torPath,
                Arguments = $"-f \"{torrcPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        _torProcess.Start();

        // Wait up to 60s for control port to become available
        var deadline = DateTime.Now.AddSeconds(60);
        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsTorRunningAsync(ct)) return true;
            await Task.Delay(500, ct);
        }

        return false;
    }

    private static (string url, string archiveName) GetDownloadInfo()
    {
        var baseUrl = $"https://archive.torproject.org/tor-package-archive/torbrowser/{TorVersion}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.X64 ? "windows-x86_64" : "windows-i686";
            var name = $"tor-expert-bundle-{arch}-{TorVersion}.tar.gz";
            return ($"{baseUrl}/{name}", name);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var name = $"tor-expert-bundle-macos-x86_64-{TorVersion}.tar.gz";
            return ($"{baseUrl}/{name}", name);
        }
        else
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x86_64" : "linux-i686";
            var name = $"tor-expert-bundle-{arch}-{TorVersion}.tar.gz";
            return ($"{baseUrl}/{name}", name);
        }
    }

    // ── Control port communication ─────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _client = new TcpClient();
        await _client.ConnectAsync(_controlHost, _controlPort, ct);
        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };
    }

    public async Task AuthenticateAsync(string? password = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var cmd = string.IsNullOrEmpty(password) ? "AUTHENTICATE" : $"AUTHENTICATE \"{password}\"";
        await SendCommandAsync(cmd, ct);
        var response = await ReadResponseAsync(ct);
        if (!response.StartsWith("250"))
            throw new InvalidOperationException($"Tor authentication failed: {response}");
    }

    public async Task<HiddenService> CreateHiddenServiceAsync(int localPort, int virtualPort = 80, CancellationToken ct = default)
    {
        EnsureConnected();
        await SendCommandAsync($"ADD_ONION NEW:ED25519-V3 Port={virtualPort},127.0.0.1:{localPort}", ct);
        return await ParseAddOnionResponseAsync(localPort, virtualPort, ct);
    }

    public async Task<HiddenService> AttachHiddenServiceAsync(string privateKey, int localPort, int virtualPort = 80, CancellationToken ct = default)
    {
        EnsureConnected();
        await SendCommandAsync($"ADD_ONION {privateKey} Port={virtualPort},127.0.0.1:{localPort}", ct);
        return await ParseAddOnionResponseAsync(localPort, virtualPort, ct);
    }

    public async Task RemoveHiddenServiceAsync(string onionAddress, CancellationToken ct = default)
    {
        EnsureConnected();
        var serviceId = onionAddress.Replace(".onion", "");
        await SendCommandAsync($"DEL_ONION {serviceId}", ct);
        var response = await ReadResponseAsync(ct);
        if (!response.StartsWith("250"))
            throw new InvalidOperationException($"Failed to remove hidden service: {response}");
    }

    private async Task<HiddenService> ParseAddOnionResponseAsync(int localPort, int virtualPort, CancellationToken ct)
    {
        string? serviceId = null;
        string? privateKey = null;

        while (true)
        {
            var line = await _reader!.ReadLineAsync(ct);
            if (line == null) break;

            if (line.StartsWith("250-ServiceID="))
                serviceId = line["250-ServiceID=".Length..];
            else if (line.StartsWith("250-PrivateKey="))
                privateKey = line["250-PrivateKey=".Length..];
            else if (line.StartsWith("250 "))
                break;
            else if (line.StartsWith("5"))
                throw new InvalidOperationException($"Tor error: {line}");
        }

        if (serviceId == null)
            throw new InvalidOperationException("Failed to get service ID from Tor response");

        return new HiddenService(serviceId, privateKey ?? "EXISTING", localPort, virtualPort);
    }

    private async Task SendCommandAsync(string command, CancellationToken ct)
    {
        EnsureConnected();
        await _writer!.WriteLineAsync(command.AsMemory(), ct);
    }

    private async Task<string> ReadResponseAsync(CancellationToken ct)
    {
        EnsureConnected();
        return await _reader!.ReadLineAsync(ct) ?? throw new InvalidOperationException("No response from Tor");
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to Tor control port. Call ConnectAsync first.");
    }

    public void StopTor()
    {
        if (_torProcess != null && !_torProcess.HasExited)
        {
            _torProcess.Kill();
            _torProcess.Dispose();
            _torProcess = null;
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
        StopTor();
    }
}
