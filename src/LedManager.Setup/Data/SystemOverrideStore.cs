using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LedManager.Setup.Data;

/// <summary>
/// Reads/writes overrides\systems\&lt;system&gt;.json — the sparse patch the runtime
/// applies on top of the Data Pack (schema ledmanager.panel-override.v1). This store
/// only manages the player-1 slot colors ("1".."8"); every other key the user may
/// have written by hand (player-prefixed slots "2:3", "outputs"…) is preserved as-is.
/// </summary>
public sealed class SystemOverrideStore
{
    private readonly string _pluginRoot;

    public SystemOverrideStore(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
    }

    public string PathFor(string system)
        => Path.Combine(_pluginRoot, "overrides", "systems", SafeName(system) + ".json");

    public bool Exists(string system) => File.Exists(PathFor(system));

    /// <summary>Player-1 slot colors of the patch (slot → COLOR).</summary>
    public IReadOnlyDictionary<int, string> LoadSlotColors(string system)
    {
        var result = new Dictionary<int, string>();
        var path = PathFor(system);
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["slots"] is not JsonObject slots)
            {
                return result;
            }

            foreach (var (key, value) in slots)
            {
                if (!int.TryParse(key, out var slot) || slot is < 1 or > 8)
                {
                    continue; // "2:3" style keys are preserved but not edited here
                }

                var color = value switch
                {
                    JsonValue direct when direct.TryGetValue<string>(out var s) => s,
                    JsonObject obj when obj["color"]?.GetValue<string>() is { } s => s,
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(color))
                {
                    result[slot] = color.Trim().ToUpperInvariant();
                }
            }
        }
        catch
        {
            // malformed patch: treated as empty, the save path rewrites it cleanly
        }

        return result;
    }

    /// <summary>
    /// Writes the player-1 slot patch. Empty dictionary removes those keys; the file
    /// is deleted entirely when nothing else remains in it.
    /// </summary>
    public string Save(string system, IReadOnlyDictionary<int, string> slotColors)
    {
        var path = PathFor(system);
        JsonObject root;
        try
        {
            root = (File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null) ?? new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        root["schema"] = "ledmanager.panel-override.v1";
        var slots = root["slots"] as JsonObject ?? new JsonObject();
        root["slots"] = slots;

        // rebuild only the plain player-1 keys, keep "player:slot" keys untouched
        foreach (var key in slots.Select(pair => pair.Key).Where(k => int.TryParse(k, out _)).ToList())
        {
            slots.Remove(key);
        }

        foreach (var (slot, color) in slotColors.OrderBy(pair => pair.Key))
        {
            slots[slot.ToString()] = new JsonObject { ["color"] = color.ToUpperInvariant() };
        }

        var hasContent = slots.Count > 0 ||
                         (root["outputs"] is JsonObject outputs && outputs.Count > 0);
        if (!hasContent)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return path;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public void Delete(string system)
    {
        var path = PathFor(system);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Trim().ToLowerInvariant().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
