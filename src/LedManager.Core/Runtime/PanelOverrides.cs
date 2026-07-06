using System.Text.Json;

namespace LedManager.Core.Runtime;

/// <summary>
/// User overrides: sparse patches applied on top of the resolved panel state.
/// Files live next to LedManager.ini and never touch the APIExpose Data Pack:
///   overrides\systems\&lt;system&gt;.json          (every game of a system)
///   overrides\games\&lt;system&gt;\&lt;rom&gt;.json      (one game, wins over the system patch)
/// Patch format (only what the user changed):
/// {
///   "schema": "ledmanager.panel-override.v1",
///   "slots":   { "1": { "color": "GREEN" }, "2:3": { "color": "RED" } },
///   "outputs": { "TORP_LAMP_1": { "slot": 1, "color": "RED" } }
/// }
/// Slot keys are "slot" (player 1) or "player:slot". Output keys match the
/// output's Name/OutputName/Id/Function case-insensitively.
/// </summary>
public static class PanelStateOverrides
{
    public static PanelState ApplyUserOverrides(this PanelState state, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(state.System))
        {
            return state;
        }

        var current = state;
        var systemPatch = Path.Combine(baseDirectory, "overrides", "systems", SafeName(state.System) + ".json");
        current = ApplyPatchFile(current, systemPatch);

        if (!string.IsNullOrWhiteSpace(state.Rom))
        {
            var gamePatch = Path.Combine(baseDirectory, "overrides", "games", SafeName(state.System), SafeName(state.Rom) + ".json");
            current = ApplyPatchFile(current, gamePatch);
        }

        return current;
    }

    private static PanelState ApplyPatchFile(PanelState state, string path)
    {
        if (!File.Exists(path))
        {
            return state;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var outputs = state.Outputs.ToList();
            var touched = 0;

            if (root.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in slots.EnumerateObject())
                {
                    if (!TryParseSlotKey(entry.Name, out var player, out var slot))
                    {
                        continue;
                    }

                    var color = ReadColor(entry.Value);
                    if (color is null)
                    {
                        continue;
                    }

                    for (var i = 0; i < outputs.Count; i++)
                    {
                        if (outputs[i].Slot == slot && PlayerMatches(outputs[i].Player, player))
                        {
                            outputs[i] = With(outputs[i], color: color);
                            touched++;
                        }
                    }
                }
            }

            if (root.TryGetProperty("outputs", out var outputPatches) && outputPatches.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in outputPatches.EnumerateObject())
                {
                    var color = ReadColor(entry.Value);
                    int? slot = entry.Value.ValueKind == JsonValueKind.Object
                                && entry.Value.TryGetProperty("slot", out var slotElement)
                                && slotElement.TryGetInt32(out var slotValue)
                        ? slotValue
                        : null;

                    for (var i = 0; i < outputs.Count; i++)
                    {
                        if (OutputMatches(outputs[i], entry.Name))
                        {
                            outputs[i] = With(outputs[i], color: color, slot: slot);
                            touched++;
                        }
                    }
                }
            }

            if (touched == 0)
            {
                return state;
            }

            Console.WriteLine($"[override] applied {Path.GetFileName(Path.GetDirectoryName(path))}\\{Path.GetFileName(path)} ({touched} output(s) patched)");
            return new PanelState
            {
                System = state.System,
                Rom = state.Rom,
                PanelId = state.PanelId,
                LayoutId = state.LayoutId,
                Sequence = state.Sequence,
                CapturedAt = state.CapturedAt,
                Outputs = outputs
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[override] ignored malformed patch {path}: {ex.Message}");
            return state;
        }
    }

    private static PanelOutput With(PanelOutput output, string? color = null, int? slot = null)
    {
        return new PanelOutput
        {
            Player = output.Player,
            Slot = slot ?? output.Slot,
            Target = output.Target,
            Color = color ?? output.Color,
            Id = output.Id,
            Name = output.Name,
            Function = output.Function,
            OutputName = output.OutputName
        };
    }

    private static bool OutputMatches(PanelOutput output, string name)
    {
        return Matches(output.Name, name)
               || Matches(output.OutputName, name)
               || Matches(output.Id, name)
               || Matches(output.Function, name);

        static bool Matches(string? candidate, string name)
            => candidate is not null && candidate.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PlayerMatches(int? outputPlayer, int patchPlayer)
    {
        // A slot patch without explicit player targets player 1, which also covers
        // outputs that do not declare a player at all.
        return outputPlayer == patchPlayer || (patchPlayer == 1 && (outputPlayer is null or 0));
    }

    private static bool TryParseSlotKey(string key, out int player, out int slot)
    {
        player = 1;
        var parts = key.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var parsedPlayer) && int.TryParse(parts[1], out slot))
        {
            player = parsedPlayer;
            return true;
        }

        return int.TryParse(key, out slot);
    }

    private static string? ReadColor(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?.Trim().ToUpperInvariant();
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("color", out var color)
            && color.ValueKind == JsonValueKind.String)
        {
            return color.GetString()?.Trim().ToUpperInvariant();
        }

        return null;
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToLowerInvariant().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
