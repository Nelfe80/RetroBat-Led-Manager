using System.IO;
using System.Text.Json;

namespace LedManager.Setup.Data;

/// <summary>
/// Reads the curated per-system panels from the APIExpose Data Pack
/// (resources\dynpanels\systems\*.json → system_template.layouts). Colors are keyed
/// by the `physical` field — the wired LED slot, validated on real hardware
/// (Score Master: B1 green, B2 yellow, B6 red, B3 blue, B4/B5 gray).
/// </summary>
public sealed class SystemPanelCatalog
{
    public sealed record PanelLayout(
        string Key,
        string DisplayName,
        int PanelButtons,
        IReadOnlyDictionary<int, (string Color, string Label)> BySlot,
        string StartColor,
        string SelectColor,
        string JoyColor);

    private readonly string _systemsDir;

    public SystemPanelCatalog(string pluginRoot)
    {
        _systemsDir = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "resources", "dynpanels", "systems"));
    }

    public bool Available => Directory.Exists(_systemsDir);

    public IReadOnlyList<string> ListSystems()
        => Available
            ? Directory.GetFiles(_systemsDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name != null)
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

    /// <summary>Layouts of a system: generic sizes first (2/4/6/8), then named variants.</summary>
    public IReadOnlyList<PanelLayout> LoadLayouts(string system)
    {
        var path = Path.Combine(_systemsDir, system + ".json");
        if (!File.Exists(path))
        {
            return Array.Empty<PanelLayout>();
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("system_template", out var template) ||
                !template.TryGetProperty("layouts", out var layouts) ||
                layouts.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<PanelLayout>();
            }

            var results = new List<PanelLayout>();
            foreach (var entry in layouts.EnumerateObject())
            {
                results.Add(Parse(entry.Name, entry.Value));
            }

            return results
                .OrderBy(layout => layout.Key.Contains(':') ? 1 : 0)
                .ThenBy(layout => layout.PanelButtons)
                .ThenBy(layout => layout.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<PanelLayout>();
        }
    }

    private static PanelLayout Parse(string key, JsonElement layout)
    {
        var count = layout.TryGetProperty("panel_buttons", out var pb) && pb.TryGetInt32(out var pbv) ? pbv : 8;
        var bySlot = new Dictionary<int, (string Color, string Label)>();
        string start = "GRAY", select = "GRAY", joy = "WHITE";

        if (layout.TryGetProperty("joystick", out var joystick) && joystick.ValueKind == JsonValueKind.Object &&
            joystick.TryGetProperty("color", out var joyColor) && joyColor.ValueKind == JsonValueKind.String)
        {
            joy = Normalize(joyColor.GetString());
        }

        if (layout.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in buttons.EnumerateObject())
            {
                var color = entry.Value.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String
                    ? Normalize(c.GetString())
                    : "BLACK";

                if (entry.Name.Equals("START", StringComparison.OrdinalIgnoreCase))
                {
                    start = color;
                    continue;
                }

                if (entry.Name.Equals("COIN", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    select = color;
                    continue;
                }

                if (entry.Value.TryGetProperty("physical", out var physical) &&
                    physical.TryGetInt32(out var slot) && slot is >= 1 and <= 8)
                {
                    // historical button name shown centered on the panel button:
                    // the game FUNCTION (A, B, L, R…), not the ES accessory name
                    var function = entry.Value.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.String
                        ? fnEl.GetString() ?? ""
                        : "";
                    if (function.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        function = "";
                    }

                    bySlot[slot] = (color, function);
                }
            }
        }

        var display = key.Contains(':') ? key[(key.IndexOf(':') + 1)..].Trim() : key;
        return new PanelLayout(key, display, count, bySlot, start, select, joy);
    }

    private static string Normalize(string? color)
        => string.IsNullOrWhiteSpace(color) ? "BLACK" : color.Trim().ToUpperInvariant();
}
