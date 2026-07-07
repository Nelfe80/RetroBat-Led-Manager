using System.Net.Sockets;
using System.Net.WebSockets;

namespace LedManager.Setup.Serial;

/// <summary>
/// Connectivity probes for the dashboard: APIExpose WebSocket (the required data
/// source) and the LedManager virtual-panel mirror (proof that LedManager runs and
/// broadcasts). Both fail fast so the dashboard never hangs.
/// </summary>
public static class ApiExposeProbe
{
    public static async Task<bool> IsAliveAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var url = baseUrl.TrimEnd('/');
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "ws://" + url["http://".Length..];
            }

            using var socket = new ClientWebSocket();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await socket.ConnectAsync(new Uri(url + "/ws/frontend"), timeout.Token).ConfigureAwait(false);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "probe", CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The [VirtualPanel] mirror only listens while LedManager runs.</summary>
    public static async Task<bool> IsMirrorAliveAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", port);
            var done = await Task.WhenAny(connect, Task.Delay(1000)).ConfigureAwait(false);
            return done == connect && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
