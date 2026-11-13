using System.IO;
using System.Net.Http;

namespace LedManager.Setup.Serial;

/// <summary>
/// Minimal HTTP client for the APIExpose REST endpoints (deployments pushed from
/// the "Contrôles" view). The base URL comes from LedManager.ini [APIExpose]
/// BaseUrl - the same setting the dashboard probe uses - with ws:// normalized
/// to http:// since both speak to the same Kestrel listener.
/// </summary>
public static class ApiExposeClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string ResolveBaseUrl(string pluginRoot)
    {
        var baseUrl = "http://127.0.0.1:12345";
        var ini = Path.Combine(pluginRoot, "LedManager.ini");
        if (File.Exists(ini))
        {
            baseUrl = LedManager.Core.Ini.IniDocument.Load(ini).Get("APIExpose", "BaseUrl", baseUrl);
        }

        if (baseUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "http://" + baseUrl["ws://".Length..];
        }

        return baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// The cabinet key for that address: empty on the loopback, where APIExpose asks
    /// for none. Read from LedManager.ini [APIExpose] ApiKey, else from the APIExpose
    /// folder next to this plugin. Read at each call: the user may have just set it.
    /// </summary>
    public static string ApiKeyFor(string baseUrl)
    {
        var pluginRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var configured = "";
        try
        {
            var ini = Path.Combine(pluginRoot, "LedManager.ini");
            if (File.Exists(ini))
            {
                configured = LedManager.Core.Ini.IniDocument.Load(ini).Get("APIExpose", "ApiKey", "");
            }
        }
        catch
        {
            // unreadable ini: fall back to the sibling key file
        }

        return LedManager.Core.Runtime.ApiExposeLink.ApiKeyFor(baseUrl, configured, pluginRoot);
    }

    private static async Task<(bool Ok, string Body)> SendAsync(HttpMethod method, string baseUrl, string path, HttpContent? content)
    {
        try
        {
            using var request = new HttpRequestMessage(method, baseUrl + path) { Content = content };
            var key = ApiKeyFor(baseUrl);
            if (key.Length > 0)
            {
                request.Headers.TryAddWithoutValidation(LedManager.Core.Runtime.ApiExposeLink.ApiKeyHeader, key);
            }

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if ((int)response.StatusCode == 401)
            {
                body = LedManager.Core.Runtime.ApiExposeLink.DescribeFailure(
                    "401", LedManager.Core.Runtime.ApiExposeLink.IsLoopback(baseUrl), key.Length > 0);
            }

            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>POST without body; returns (success, response body or error message).</summary>
    public static Task<(bool Ok, string Body)> PostAsync(string baseUrl, string path)
        => SendAsync(HttpMethod.Post, baseUrl, path, null);

    /// <summary>POST with a JSON body; returns (success, response body or error message).</summary>
    public static async Task<(bool Ok, string Body)> PostJsonAsync(string baseUrl, string path, string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, baseUrl, path, content).ConfigureAwait(false);
    }

    /// <summary>GET; returns (success, response body or error message).</summary>
    public static Task<(bool Ok, string Body)> GetAsync(string baseUrl, string path)
        => SendAsync(HttpMethod.Get, baseUrl, path, null);
}
