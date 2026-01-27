using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;

public class TorManager : ITorManager
{
    private const string TorVersion = "14.0.4";
    private const int ControlPort = 9051;
    
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "whisp_mailbox");
    
    private static readonly string TorDirectory = Path.Combine(AppDataPath, "tor");
    private static readonly string TorDataDirectory = Path.Combine(AppDataPath, "tor_data");
    
    private Process? _torProcess;

    public bool IsTorInstalled()
    {
        return GetTorPath() != null;
    }

    public string? GetTorPath()
    {
        // Check local app directory first (our downloaded version)
        var localTorPath = GetLocalTorPath();
        if (File.Exists(localTorPath))
            return localTorPath;

        // Check PATH environment variable
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            var torPath = Path.Combine(dir, GetTorExecutableName());
            if (File.Exists(torPath))
                return torPath;
        }

        // Check common installation paths on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var commonPaths = new[]
            {
                @"C:\Program Files\Tor\tor.exe",
                @"C:\Program Files (x86)\Tor\tor.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    public async Task DownloadTorAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDataPath);
        
        var (downloadUrl, archiveName) = GetDownloadInfo();
        var archivePath = Path.Combine(AppDataPath, archiveName);

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        // Download with progress
        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                progress?.Report((double)downloadedBytes / totalBytes * 100);
            }
        }

        fileStream.Close();

        // Extract the archive
        await ExtractTorAsync(archivePath, cancellationToken);

        // Clean up archive
        File.Delete(archivePath);
    }

    private async Task ExtractTorAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (Directory.Exists(TorDirectory))
            Directory.Delete(TorDirectory, true);

        Directory.CreateDirectory(TorDirectory);

        // Use tar command (available on Windows 10 1803+, macOS, Linux)
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{TorDirectory}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        });

        if (process != null)
        {
            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Failed to extract TOR archive: {error}");
            }
        }
    }

    public async Task<bool> StartTorAsync(CancellationToken cancellationToken = default)
    {
        var torPath = GetTorPath();
        if (torPath == null)
            return false;

        // Create data directory
        Directory.CreateDirectory(TorDataDirectory);

        // Create torrc file
        var torrcPath = Path.Combine(TorDataDirectory, "torrc");
        var torrcContent = $"""
            DataDirectory {TorDataDirectory}
            ControlPort {ControlPort}
            SocksPort 9050
            """;
        await File.WriteAllTextAsync(torrcPath, torrcContent, cancellationToken);

        // Start TOR process
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

        // Wait for TOR to be ready (check control port)
        var maxWait = TimeSpan.FromSeconds(60);
        var started = DateTime.Now;

        while (DateTime.Now - started < maxWait)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsTorRunningAsync(cancellationToken))
                return true;

            await Task.Delay(500, cancellationToken);
        }

        return false;
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

    public async Task<bool> IsTorRunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", ControlPort, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLocalTorPath()
    {
        // The Expert Bundle extracts to: tor/tor.exe (Windows) or tor/tor (Unix)
        var torExeName = GetTorExecutableName();
        
        // Try common extraction paths
        var possiblePaths = new[]
        {
            Path.Combine(TorDirectory, "tor", torExeName),
            Path.Combine(TorDirectory, torExeName),
            Path.Combine(TorDirectory, "Tor", torExeName),
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        // Default expected path
        return Path.Combine(TorDirectory, "tor", torExeName);
    }

    private static string GetTorExecutableName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tor.exe" : "tor";
    }

    private static (string url, string archiveName) GetDownloadInfo()
    {
        // TOR Expert Bundle download URLs
        var baseUrl = $"https://archive.torproject.org/tor-package-archive/torbrowser/{TorVersion}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.X64 ? "windows-x86_64" : "windows-i686";
            var archiveName = $"tor-expert-bundle-{arch}-{TorVersion}.tar.gz";
            return ($"{baseUrl}/{archiveName}", archiveName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var archiveName = $"tor-expert-bundle-macos-x86_64-{TorVersion}.tar.gz";
            return ($"{baseUrl}/{archiveName}", archiveName);
        }
        else // Linux
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x86_64" : "linux-i686";
            var archiveName = $"tor-expert-bundle-{arch}-{TorVersion}.tar.gz";
            return ($"{baseUrl}/{archiveName}", archiveName);
        }
    }
}

