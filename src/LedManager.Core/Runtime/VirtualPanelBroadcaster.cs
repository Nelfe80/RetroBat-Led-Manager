using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace LedManager.Core.Runtime;

/// <summary>
/// Mirrors every resolved command to local subscribers (the Setup app's virtual panel).
/// Loopback-only TCP server, one JSON object per line. Slow clients drop old messages
/// instead of slowing the hardware path down.
/// </summary>
public sealed class VirtualPanelBroadcaster : IAsyncDisposable
{
    private const int ClientQueueCapacity = 256;

    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();
    private readonly CancellationTokenSource _stopCts = new();
    private Task? _acceptLoop;

    /// <summary>Called on client connect to greet it with the current known state.</summary>
    public Func<IReadOnlyList<VirtualPanelMessage>>? SnapshotProvider { get; set; }

    public VirtualPanelBroadcaster(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stopCts.Token));
        Console.WriteLine($"[virtualpanel] listening on 127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
    }

    public void Publish(VirtualPanelMessage message)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var line = Serialize(message);
        foreach (var client in _clients.Values)
        {
            client.Writer.TryWrite(line);
        }
    }

    private static string Serialize(VirtualPanelMessage message)
    {
        return JsonSerializer.Serialize(message, VirtualPanelJson.Options);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await _listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeClientAsync(tcpClient, token), token);
        }
    }

    private async Task ServeClientAsync(TcpClient tcpClient, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(ClientQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _clients[id] = queue;
        Console.WriteLine($"[virtualpanel] client connected ({_clients.Count} total)");

        try
        {
            using (tcpClient)
            {
                tcpClient.NoDelay = true;
                var stream = tcpClient.GetStream();

                var snapshot = SnapshotProvider?.Invoke();
                if (snapshot is not null)
                {
                    foreach (var message in snapshot)
                    {
                        await WriteLineAsync(stream, Serialize(message), token);
                    }
                }

                await foreach (var line in queue.Reader.ReadAllAsync(token))
                {
                    await WriteLineAsync(stream, line, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            _clients.TryRemove(id, out _);
            Console.WriteLine($"[virtualpanel] client disconnected ({_clients.Count} left)");
        }
    }

    private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, token);
    }

    public async ValueTask DisposeAsync()
    {
        _stopCts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // already stopped
        }

        foreach (var client in _clients.Values)
        {
            client.Writer.TryComplete();
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch
            {
                // shutdown path
            }
        }
    }
}

/// <summary>One mirrored command. `type` is "command" for live traffic, "snapshot" for the greeting replay.</summary>
public sealed record VirtualPanelMessage(
    string Type,
    string Sender,
    int? Player,
    string Command,
    bool PanelUpdate,
    long Timestamp)
{
    public static VirtualPanelMessage Live(string sender, int? player, string command, bool panelUpdate)
        => new("command", sender, player, command, panelUpdate, Environment.TickCount64);

    public static VirtualPanelMessage Snapshot(string sender, int? player, string command)
        => new("snapshot", sender, player, command, true, Environment.TickCount64);
}

internal static class VirtualPanelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
