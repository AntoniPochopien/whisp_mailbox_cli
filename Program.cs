using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using QRCoder;

// --- Initialize database ---
var db = new Database();

// --- Pick a free local port ---
var localPort = GetAvailablePort();

Console.WriteLine($"[*] Local listener port: {localPort}");

// --- Ensure Tor is downloaded, running, and connected ---
var tor = new TorService();

try
{
       await tor.EnsureTorReadyAsync();
}
catch (Exception ex)
{
       Console.WriteLine($"[!] Failed to start Tor: {ex.Message}");
       return 1;
}

Console.WriteLine("[+] Connected to Tor.");

// --- Create or reattach hidden service ---
HiddenService hiddenService;
var (savedKey, _) = db.GetOnionConfig();

if (savedKey != null)
{
       Console.WriteLine("[*] Reattaching existing hidden service...");
       hiddenService = await tor.AttachHiddenServiceAsync(savedKey, localPort);
       Console.WriteLine("[+] Hidden service reattached.");
}
else
{
       Console.WriteLine("[*] Creating new hidden service...");
       hiddenService = await tor.CreateHiddenServiceAsync(localPort);
       db.SaveOnionConfig(hiddenService.PrivateKey, hiddenService.OnionAddress);
       Console.WriteLine("[+] Hidden service created and saved.");
}



// --- Display onion address ---
var onionAddress = hiddenService.FullOnionAddress;

DisplayQrCode(onionAddress);

Console.WriteLine();
Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════════╗");
Console.WriteLine($"  Your Onion Address: {onionAddress}");
Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// --- Password: generate on first run, reuse hash on subsequent runs ---
var pinHash = db.GetPinHash();

if (pinHash == null)
{
       // First run — generate a random password, show it once, store only the hash
       var password = GeneratePassword();
       pinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
       db.SavePinHash(pinHash);

       Console.WriteLine();
       Console.WriteLine("╔═══════════════════════════════════════════════════════════════════╗");
       Console.WriteLine($"  Your mailbox password: {password}");
       Console.WriteLine("  SAVE THIS! It will NOT be shown again.");
       Console.WriteLine("╚═══════════════════════════════════════════════════════════════════╝");
       Console.WriteLine();
}
else
{
       Console.WriteLine("[+] Existing mailbox found, password already configured.");
}

// --- Start HTTP listener ---
var listener = new MailboxListener(db, pinHash);
await listener.StartAsync(localPort);

Console.WriteLine($"[+] Mailbox is ONLINE (loopback only: 127.0.0.1:{localPort})");
Console.WriteLine("[*] Endpoints: POST /pair, POST /message, GET /pull?pin=..., GET /ping");
Console.WriteLine("[*] Press Ctrl+C to stop.");
Console.WriteLine();

// --- Wait for Ctrl+C ---
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
       e.Cancel = true;
       cts.Cancel();
};

try
{
       await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }

// --- Shutdown ---
Console.WriteLine();
Console.WriteLine("[*] Shutting down...");

await listener.StopAsync();

try
{
       await tor.RemoveHiddenServiceAsync(hiddenService.OnionAddress);
}
catch { /* Tor may already be gone */ }

tor.Dispose();
db.Dispose();

Console.WriteLine("[+] Goodbye.");
return 0;

// --- Helpers ---

static int GetAvailablePort()
{
       var l = new TcpListener(IPAddress.Loopback, 0);
       l.Start();
       var port = ((IPEndPoint)l.LocalEndpoint).Port;
       l.Stop();
       return port;
}

static string GeneratePassword(int length = 8)
{
       const string digits = "0123456789";
       return string.Create(length, digits, (span, d) =>
       {
              var bytes = RandomNumberGenerator.GetBytes(length);
              for (int i = 0; i < span.Length; i++)
                     span[i] = d[bytes[i] % d.Length];
       });
}

static void DisplayQrCode(string text)
{
       try
       {
              using var qrGenerator = new QRCodeGenerator();
              var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
              var qrCode = new AsciiQRCode(qrCodeData);
              var qrCodeAsAscii = qrCode.GetGraphic(1);
              Console.WriteLine("QR Code:");
              Console.WriteLine(qrCodeAsAscii);
       }
       catch (Exception ex)
       {
              Console.WriteLine($"[!] Could not generate QR code: {ex.Message}");
       }
}
