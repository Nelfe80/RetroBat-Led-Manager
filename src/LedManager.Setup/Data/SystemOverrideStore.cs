using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LedManager.Setup.Data;

/// <summary>
/// Reads/writes the sparse override patches the runtime applies on top of the Data
/// Pack (schema ledmanager.panel-override.v1): overrides\systems\&lt;system&gt;.json and
/// overrides\games\&lt;system&gt;\&lt;rom&gt;.json. This store only manages the player-1 slot
/// colors ("1".."8"); every other key the user may have written by hand
/// (player-prefixed slots "2:3", "outputs"…) is preserved as-is.
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

    public string PathForGame(string system, string rom)
        => Path.Combine(_pluginRoot, "overrides", "games", SafeName(system), SafeName(rom) + ".json");

    public bool Exists(string system) => File.Exists(PathFor(system));

    public bool ExistsGame(string system, string rom) => File.Exists(PathForGame(system, rom));

    /// <summary>Player-1 slot colors of the system patch (slot → COLOR).</summary>
    public IReadOnlyDictionary<int, string> LoadSlotColors(string system)
        => LoadSlotColorsAt(PathFor(system));

    /// <summary>Player-1 slot colors of the game patch (slot → COLOR).</summary>
    public IReadOnlyDictionary<int, string> LoadGameSlotColors(string system, string rom)
        => LoadSlotColorsAt(PathForGame(system, rom));

    public string Save(string system, IReadOnlyDictionary<int, string> slotColors)
        => SaveAt(PathFor(system), slotColors);

    public string SaveGame(string system, string rom, IReadOnlyDictionary<int, string> slotColors)
        => SaveAt(PathForGame(system, rom), slotColors);

    public void Delete(string system) => DeleteAt(PathFor(system));

    public void DeleteGame(string system, string rom) => DeleteAt(PathForGame(system, rom));

    /// <summary>
    /// Light-channel patches of a game: output name → (slot réalloué, couleur ON,
    /// cibles système). Runtime schema (PanelStateOverrides) :
    /// "outputs": { "TORP_LAMP_1": { "slot": 1, "color": "RED" },
    ///              "lamp0": { "targets": ["START","SELECT"] } }.
    /// </summary>
    public IReadOnlyDictionary<string, (int? Slot, string? Color, IReadOnlyList<string> Targets)> LoadGameOutputPatches(string system, string rom)
    {
        var result = new Dictionary<string, (int?, string?, IReadOnlyList<string>)>(StringComparer.OrdinalIgnoreCase);
        var path = PathForGame(system, rom);
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["outputs"] is not JsonObject outputs)
            {
                return result;
            }

            foreach (var (name, value) in outputs)
            {
                if (value is not JsonObject entry)
                {
                    continue;
                }

                int? slot = entry["slot"]?.GetValue<int>();
                var color = entry["color"]?.GetValue<string>();
                var targets = new List<string>();
                if (entry["target"]?.GetValue<string>() is { } single && !string.IsNullOrWhiteSpace(single))
                {
                    targets.Add(single.Trim().ToUpperInvariant());
                }

                if (entry["targets"] is JsonArray many)
                {
                    targets.AddRange(many.OfType<JsonValue>()
                        .Select(v => v.TryGetValue<string>(out var s) ? s : null)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim().ToUpperInvariant()));
                }

                if (slot is not null || targets.Count > 0 || !string.IsNullOrWhiteSpace(color))
                {
                    result[name] = (slot, color?.Trim().ToUpperInvariant(), targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                }
            }
        }
        catch
        {
            // malformed patch: treated as empty
        }

        return result;
    }

    /// <summary>Writes the light-channel patches, preserving every other key of the
    /// file (slots colors, hand-written entries). Empty dictionary removes the
    /// outputs section; the file is deleted when nothing remains.</summary>
    public string SaveGameOutputPatches(string system, string rom, IReadOnlyDictionary<string, (int? Slot, string? Color, IReadOnlyList<string> Targets)> patches)
    {
        var path = PathForGame(system, rom);
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
        if (patches.Count == 0)
        {
            root.Remove("outputs");
        }
        else
        {
            var outputs = new JsonObject();
            foreach (var (name, patch) in patches.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = new JsonObject();
                if (patch.Targets.Count > 0)
                {
                    entry["targets"] = new JsonArray(patch.Targets.Select(t => (JsonNode)t.ToUpperInvariant()).ToArray());
                }
                else if (patch.Slot is { } slot)
                {
                    entry["slot"] = slot;
                }

                if (!string.IsNullOrWhiteSpace(patch.Color))
                {
                    entry["color"] = patch.Color!.ToUpperInvariant();
                }

                outputs[name] = entry;
            }

            root["outputs"] = outputs;
        }

        return WriteOrDelete(path, root);
    }

    /// <summary>
    /// Input-channel patches: mame input id (P1_BUTTON1) → panel buttons that
    /// trigger it (multi-allocation). Consumed by the MAME cfg deployment, which
    /// ORs the matching JOYCODEs.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> LoadGameInputPatches(string system, string rom)
    {
        var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        var path = PathForGame(system, rom);
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["inputs"] is not JsonObject inputs)
            {
                return result;
            }

            foreach (var (id, value) in inputs)
            {
                if ((value as JsonObject)?["slots"] is JsonArray slots)
                {
                    var parsed = slots.Select(n => n?.GetValue<int>() ?? -1).Where(s => s >= 1).ToArray();
                    if (parsed.Length > 0)
                    {
                        result[id] = parsed;
                    }
                }
            }
        }
        catch
        {
            // malformed patch: treated as empty
        }

        return result;
    }

    public string SaveGameInputPatches(string system, string rom, IReadOnlyDictionary<string, IReadOnlyList<int>> patches)
    {
        var path = PathForGame(system, rom);
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
        if (patches.Count == 0)
        {
            root.Remove("inputs");
        }
        else
        {
            var inputs = new JsonObject();
            foreach (var (id, slots) in patches.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                inputs[id] = new JsonObject { ["slots"] = new JsonArray(slots.OrderBy(s => s).Select(s => (JsonNode)s).ToArray()) };
            }

            root["inputs"] = inputs;
        }

        return WriteOrDelete(path, root);
    }

    private static string WriteOrDelete(string path, JsonObject root)
    {
        var hasContent = (root["slots"] is JsonObject s && s.Count > 0) ||
                         (root["outputs"] is JsonObject o && o.Count > 0) ||
                         (root["inputs"] is JsonObject i && i.Count > 0);
        if (!hasContent)
        {
            DeleteAt(path);
            return path;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static IReadOnlyDictionary<int, string> LoadSlotColorsAt(string path)
    {
        var result = new Dictionary<int, string>();
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
    private static string SaveAt(string path, IReadOnlyDictionary<int, string> slotColors)
    {
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

        if (slots.Count == 0)
        {
            root.Remove("slots");
        }

        return WriteOrDelete(path, root);
    }

    private static void DeleteAt(string path)
    {
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
