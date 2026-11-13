using System.Net;

namespace LedManager.Core.Runtime;

/// <summary>
/// What it takes to talk to APIExpose, shared by the daemon and the Setup: which calls
/// need the cabinet key, where the key is found, how long to wait before the next
/// attempt, and how to say why a connection failed.
/// </summary>
public static class ApiExposeLink
{
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// APIExpose only asks for the cabinet key when the caller is not on the loopback.
    /// The LAN address of this very machine counts as "not loopback": the API sees the
    /// source address, not the intent.
    /// </summary>
    public static bool IsLoopback(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return true;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.IsLoopback) return true;
        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// The key to present, empty when none is needed or none is known. The configured
    /// value wins; otherwise the key APIExpose generated for this cabinet is read from
    /// the sibling plugin folder, which covers a LAN address that points back here.
    /// </summary>
    public static string ApiKeyFor(string? baseUrl, string? configuredKey, string pluginDirectory)
    {
        if (IsLoopback(baseUrl)) return string.Empty;
        var configured = (configuredKey ?? string.Empty).Trim();
        if (configured.Length > 0) return configured;
        return ReadLocalCabinetKey(pluginDirectory);
    }

    public static string ReadLocalCabinetKey(string pluginDirectory)
    {
        try
        {
            var path = Path.Combine(pluginDirectory, "..", "APIExpose", "state", "cabinet-api-key.txt");
            if (!File.Exists(path)) return string.Empty;
            var key = File.ReadAllText(path).Trim();
            return key.Length >= 16 ? key : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Wait before reconnection attempt number <paramref name="attempt"/> (1-based):
    /// 1 s, 2 s, 4 s, 8 s then 15 s, spread by ±20 % so the streams do not all knock at
    /// the same instant. <paramref name="jitter"/> is a draw in [0, 1).
    /// </summary>
    public static TimeSpan ReconnectDelay(int attempt, double jitter)
    {
        var step = Math.Clamp(attempt, 1, 5) - 1;
        var seconds = Math.Min(15d, Math.Pow(2, step));
        var spread = 0.8 + 0.4 * Math.Clamp(jitter, 0d, 1d);
        return TimeSpan.FromSeconds(seconds * spread);
    }

    /// <summary>
    /// Says in plain words why a connection failed, so the log tells the user what to
    /// do. Never contains the key.
    /// </summary>
    public static string DescribeFailure(string? message, bool loopback, bool keyPresented)
    {
        var text = message ?? string.Empty;
        if (text.Contains("401", StringComparison.Ordinal))
        {
            if (loopback) return "APIExpose refused the connection (401)";
            return keyPresented
                ? "APIExpose refused the cabinet key (401): check [APIExpose] ApiKey against the key of that cabinet"
                : "APIExpose asks for the cabinet key (401): set [APIExpose] ApiKey to the key of that cabinet (its APIExpose\\state\\cabinet-api-key.txt)";
        }
        if (text.Contains("403", StringComparison.Ordinal)) return "APIExpose refused the connection (403)";
        if (text.Contains("404", StringComparison.Ordinal)) return "this APIExpose does not publish that stream (404): update APIExpose or enable its WebSocket";
        return text.Length > 0 ? text : "connection closed";
    }
}
