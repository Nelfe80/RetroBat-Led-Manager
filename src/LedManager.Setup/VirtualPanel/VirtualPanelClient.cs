using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedManager.Setup.VirtualPanel;

/// <summary>
/// Subscribes to the LedManager [VirtualPanel] mirror (loopback TCP, one JSON per line)
/// and raises one event per mirrored command. Reconnects forever until disposed.
/// </summary>
public sealed class VirtualPanelClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _host;
    private readonly int _port;
    private readonly CancellationTokenSource _stopCts = new();

    public event Action<VirtualPanelEvent>? MessageReceived;
    public event Action<bool>? ConnectionChanged;

    public VirtualPanelClient(string host = "127.0.0.1", int port = 12377)
    {
        _host = host;
        _port = port;
    }

    public void Start()
    {
        _ = Task.Run(() => RunAsync(_stopCts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port, token);
                ConnectionChanged?.Invoke(true);

                using var reader = new StreamReader(client.GetStream());
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    VirtualPanelEvent? evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<VirtualPanelEvent>(line, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (evt is not null)
                    {
                        MessageReceived?.Invoke(evt);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Connection refused or dropped: LedManager is not running (yet).
            }

            ConnectionChanged?.Invoke(false);
            try
            {
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _stopCts.Cancel();
    }
}

public sealed class VirtualPanelEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "command";

    [JsonPropertyName("sender")]
    public string Sender { get; set; } = "";

    [JsonPropertyName("player")]
    public int? Player { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("panelUpdate")]
    public bool PanelUpdate { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
