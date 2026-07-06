using System.IO;

namespace LedManager.Setup.Controls;

/// <summary>
/// The recommended physical layout (resources\setup\layouts\retrobat_standard.json).
/// Falls back to the curator's canonical LAYOUT_SLOTS if the file is missing.
/// </summary>
public sealed class PanelLayoutDefinition
{
    private static readonly Dictionary<string, int[][]> FallbackRows = new()
    {
        ["2-Button"] = new[] { new[] { 1, 2 } },
        ["4-Button"] = new[] { new[] { 4, 3 }, new[] { 1, 2 } },
        ["6-Button"] = new[] { new[] { 4, 3, 5 }, new[] { 1, 2, 6 } },
        ["8-Button"] = new[] { new[] { 4, 3, 5, 7 }, new[] { 1, 2, 6, 8 } }
    };

    private static readonly Dictionary<int, string> FallbackLabels = new()
    {
        [1] = "A", [2] = "B", [3] = "X", [4] = "Y", [5] = "L1", [6] = "R1", [7] = "L2", [8] = "R2"
    };

    private Dictionary<string, int[][]> _rows = FallbackRows;
    private Dictionary<int, string> _labels = FallbackLabels;

    public static PanelLayoutDefinition Load(string? pluginRoot)
    {
        var definition = new PanelLayoutDefinition();
        if (pluginRoot is null)
        {
            return definition;
        }

        var path = Path.Combine(pluginRoot, "resources", "setup", "layouts", "retrobat_standard.json");
        if (!File.Exists(path))
        {
            return definition;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("layouts", out var layouts))
            {
                var rows = new Dictionary<string, int[][]>();
                foreach (var layout in layouts.EnumerateObject())
                {
                    if (layout.Value.TryGetProperty("rows", out var rowsElement))
                    {
                        rows[layout.Name] = rowsElement.EnumerateArray()
                            .Select(r => r.EnumerateArray().Select(v => v.GetInt32()).ToArray())
                            .ToArray();
                    }
                }

                if (rows.Count > 0)
                {
                    definition._rows = rows;
                }
            }

            if (root.TryGetProperty("buttons", out var buttons))
            {
                var labels = new Dictionary<int, string>();
                foreach (var button in buttons.EnumerateObject())
                {
                    if (int.TryParse(button.Name, out var slot)
                        && button.Value.TryGetProperty("retrobat", out var name))
                    {
                        labels[slot] = name.GetString() ?? "";
                    }
                }

                if (labels.Count > 0)
                {
                    definition._labels = labels;
                }
            }
        }
        catch
        {
            // Malformed file: the canonical fallback still applies.
        }

        return definition;
    }

    public IEnumerable<int[]> RowsFor(int buttonCount)
    {
        var key = buttonCount switch
        {
            <= 2 => "2-Button",
            <= 4 => "4-Button",
            <= 6 => "6-Button",
            _ => "8-Button"
        };

        var rows = _rows.TryGetValue(key, out var found) ? found : FallbackRows["8-Button"];
        return rows
            .Select(row => row.Where(slot => slot <= buttonCount).ToArray())
            .Where(row => row.Length > 0);
    }

    public string RetrobatLabel(int slot)
    {
        return _labels.TryGetValue(slot, out var label) ? label : "";
    }
}
