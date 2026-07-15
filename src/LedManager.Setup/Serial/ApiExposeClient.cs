using System.IO;
using System.Net.Http;

namespace LedManager.Setup.Serial;

/// <summary>
/// Minimal HTTP client for the APIExpose REST endpoints (deployments pushed from
/// the "Contrôles" view). The base URL comes from LedManager.ini [APIExpose]
/// BaseUrl — the same setting the dashboard probe uses — with ws:// normalized
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

    /// <summary>POST without body; returns (success, response body or error message).</summary>
    public static async Task<(bool Ok, string Body)> PostAsync(string baseUrl, string path)
    {
        try
        {
            using var response = await Http.PostAsync(baseUrl + path, content: null).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>POST with a JSON body; returns (success, response body or error message).</summary>
    public static async Task<(bool Ok, string Body)> PostJsonAsync(string baseUrl, string path, string json)
    {
        try
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(baseUrl + path, content).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>GET; returns (success, response body or error message).</summary>
    public static async Task<(bool Ok, string Body)> GetAsync(string baseUrl, string path)
    {
        try
        {
            using var response = await Http.GetAsync(baseUrl + path).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
