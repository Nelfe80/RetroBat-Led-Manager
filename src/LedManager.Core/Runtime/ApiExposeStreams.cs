using System.Text.Json;

namespace LedManager.Core.Runtime;

/// <summary>
/// Which optional WebSocket streams this APIExpose publishes. /ws/scoring and
/// /ws/multiplayer only exist since APIExpose 1.9.185: they are opened when
/// GET /api/v1/ws/streams announces them, and never otherwise, so an older API keeps
/// working with the historical five streams and no 404 loop in the log.
/// </summary>
public static class ApiExposeStreams
{
    public const string CatalogPath = "/api/v1/ws/streams";

    public const string Scoring = "scoring";
    public const string Multiplayer = "multiplayer";

    /// <summary>Streams opened only when the API announces them, in opening order.</summary>
    public static IReadOnlyList<string> Optional { get; } = new[] { Scoring, Multiplayer };

    /// <summary>
    /// Stream names listed by a GET /api/v1/ws/streams answer. The API writes camelCase
    /// (<c>streams[].name</c>); PascalCase and a bare array are read as well. An unreadable
    /// answer lists nothing.
    /// </summary>
    public static IReadOnlySet<string> ParseAnnounced(string? json)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return names;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement streams;
            if (root.ValueKind == JsonValueKind.Array)
            {
                streams = root;
            }
            else if (root.ValueKind != JsonValueKind.Object || !TryGetProperty(root, "streams", out streams) || streams.ValueKind != JsonValueKind.Array)
            {
                return names;
            }

            foreach (var stream in streams.EnumerateArray())
            {
                var name = stream.ValueKind switch
                {
                    JsonValueKind.String => stream.GetString(),
                    JsonValueKind.Object when TryGetProperty(stream, "name", out var value) && value.ValueKind == JsonValueKind.String => value.GetString(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }
        }
        catch (JsonException)
        {
        }

        return names;
    }

    /// <summary>
    /// Reads the answer to GET /api/v1/ws/streams. A success lists the streams; a 404 is an
    /// API older than the catalog, which publishes none of the optional streams (final);
    /// anything else (401, 5xx) is worth asking again later, the API may be starting.
    /// </summary>
    public static StreamCatalogAnswer Interpret(int statusCode, string? body)
    {
        if (statusCode is >= 200 and < 300)
        {
            return new StreamCatalogAnswer(true, ParseAnnounced(body));
        }

        if (statusCode == 404)
        {
            return new StreamCatalogAnswer(true, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return new StreamCatalogAnswer(false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The optional streams to open: announced by the API and given a path in the
    /// configuration (an empty path turns a stream off). Order follows <see cref="Optional"/>.
    /// </summary>
    public static IReadOnlyList<(string Stream, string Path)> SelectOptional(
        IReadOnlySet<string> announced,
        IReadOnlyDictionary<string, string> configuredPaths)
    {
        var selected = new List<(string, string)>();
        foreach (var stream in Optional)
        {
            if (!announced.Contains(stream) ||
                !configuredPaths.TryGetValue(stream, out var path) ||
                string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            selected.Add((stream, path.Trim()));
        }

        return selected;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

/// <summary>
/// <paramref name="Final"/> = the API answered for good (the announced set is to be used);
/// false = ask again after a wait.
/// </summary>
public sealed record StreamCatalogAnswer(bool Final, IReadOnlySet<string> Announced);
