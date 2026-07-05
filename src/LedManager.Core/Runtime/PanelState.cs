using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedManager.Core.Runtime;

public sealed class PanelState
{
    public string? System { get; init; }
    public string? Rom { get; init; }
    public string? PanelId { get; init; }
    public string? LayoutId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<PanelOutput> Outputs { get; init; } = Array.Empty<PanelOutput>();

    [JsonIgnore]
    public bool IsEmpty => Outputs.Count == 0;

    public static PanelState FromPanelEvent(LedEvent evt)
    {
        var root = evt.Payload;
        // Real APIExpose panel.state events wrap everything in a "Payload" envelope:
        // {"Type":"panel.state",...,"Payload":{"ActivePanel":{...},"ActiveLayout":{...},"PanelKey":...}}.
        var payloadObj = FindObject(root, "Payload") ?? FindObject(root, "payload") ?? root;
        var active = FindObject(payloadObj, "ActivePanel") ?? FindObject(payloadObj, "activePanel") ?? root;
        var layout = FindObject(payloadObj, "ActiveLayout") ?? FindObject(payloadObj, "activeLayout");
        var outputs = new List<PanelOutput>();

        ReadSlots(active, outputs);

        return new PanelState
        {
            System = evt.System ?? ReadStringDeep(payloadObj, "system") ?? ReadStringDeep(payloadObj, "SystemId"),
            Rom = evt.Rom ?? ReadStringDeep(payloadObj, "rom") ?? ReadStringDeep(payloadObj, "Rom") ?? ReadStringDeep(payloadObj, "game"),
            PanelId = ReadString(active, "Id") ?? ReadString(active, "id") ?? ReadStringDeep(payloadObj, "PanelKey") ?? ReadStringDeep(payloadObj, "SourceFile") ?? ReadStringDeep(root, "panelId"),
            LayoutId = ReadStringNullable(layout, "Id") ?? ReadStringNullable(layout, "id") ?? ReadStringDeep(payloadObj, "ActiveLayoutId") ?? ReadStringDeep(root, "layout"),
            Sequence = ReadLongDeep(payloadObj, "Sequence") ?? ReadLongDeep(root, "Sequence") ?? 0,
            Outputs = CompletePhysicalSlots(outputs)
        };
    }

    public PanelState EnrichFromDynpanel(string baseDirectory)
    {
        var dynpanelOutputs = LoadDynpanelOutputs(baseDirectory, System, Rom, LayoutId);
        if (dynpanelOutputs.Count == 0)
        {
            return this;
        }

        var slottedDynOutputs = dynpanelOutputs
            .Where(output => output.Slot.HasValue)
            .ToArray();

        var hasExplicitDynpanelPlayers = slottedDynOutputs.Any(output => output.Player > 0);
        var baseByPlayerSlot = hasExplicitDynpanelPlayers
            ? new Dictionary<(int? Player, int Slot), PanelOutput>()
            : Outputs
                .Where(output => output.Slot.HasValue)
                .GroupBy(output => PlayerSlotKey(output))
                .ToDictionary(group => group.Key, group => group.First());

        foreach (var dynOutput in slottedDynOutputs)
        {
            baseByPlayerSlot[PlayerSlotKey(dynOutput)] = dynOutput;
        }

        var withoutSlot = dynpanelOutputs
            .Where(output => !output.Slot.HasValue)
            .ToArray();

        var players = baseByPlayerSlot.Keys
            .Select(key => key.Player)
            .Distinct()
            .OrderBy(player => player ?? int.MaxValue)
            .ToArray();
        if (players.Length == 0)
        {
            players = new int?[] { null };
        }

        return new PanelState
        {
            System = System,
            Rom = Rom,
            PanelId = PanelId,
            LayoutId = LayoutId,
            Sequence = Sequence,
            CapturedAt = CapturedAt,
            Outputs = players
                .SelectMany(player => Enumerable.Range(1, 8)
                    .Select(slot => baseByPlayerSlot.TryGetValue((player, slot), out var output)
                        ? output
                        : new PanelOutput { Player = player, Slot = slot, Target = $"SLOT:{slot}", Color = "BLACK" }))
                .Concat(withoutSlot)
                .ToArray()
        };
    }

    public PanelOutput? FindOutputSignal(string signal)
    {
        return FindOutputSignals(signal).FirstOrDefault();
    }

    public IReadOnlyList<PanelOutput> FindOutputSignals(string signal)
    {
        var normalized = NormalizeSignal(signal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<PanelOutput>();
        }

        return Outputs.Where(output =>
            SignalMatches(output.Id, normalized) ||
            SignalMatches(output.Name, normalized) ||
            SignalMatches(output.OutputName, normalized) ||
            SignalMatches(output.Function, normalized))
            .ToArray();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions()));
    }

    public static PanelState? Load(string path)
    {
        return File.Exists(path)
            ? JsonSerializer.Deserialize<PanelState>(File.ReadAllText(path), JsonOptions())
            : null;
    }

    private static void ReadSlots(JsonElement active, List<PanelOutput> outputs)
    {
        if (!TryGetPropertyAny(active, out var slots, "Slots", "slots") || slots.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var slot in slots.EnumerateArray())
        {
            var slotNumber = ReadInt(slot, "Slot") ?? ReadInt(slot, "slot") ?? ReadInt(slot, "Index") ?? ReadInt(slot, "index");
            var target = ReadString(slot, "Target") ?? ReadString(slot, "target") ?? (slotNumber.HasValue ? $"SLOT:{slotNumber}" : null);
            if (!slotNumber.HasValue && string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var player = ReadInt(slot, "Player") ?? ReadInt(slot, "player");
            var color = ReadString(slot, "Color") ?? ReadString(slot, "color");
            string? id = null;
            string? name = null;
            string? function = null;
            string? outputName = null;

            // Real APIExpose payloads put the button's color on its first wired input
            // (Slots[].Inputs[].Color) rather than directly on the slot.
            if (color is null && TryGetPropertyAny(slot, out var inputs, "Inputs", "inputs") && inputs.ValueKind == JsonValueKind.Array)
            {
                foreach (var input in inputs.EnumerateArray())
                {
                    color ??= ReadString(input, "Color") ?? ReadString(input, "color");
                    player ??= ReadInt(input, "Player") ?? ReadInt(input, "player");
                }
            }

            // Lamp-driven slots (e.g. seawolf) may have only Outputs, while games such as
            // Daytona expose both a button Input and a lamp Output on the same panel slot.
            // Always read output metadata so mame.output.changed can target lamp1/lamp5,
            // but keep the input color as the base panel color when it exists.
            if (TryGetPropertyAny(slot, out var slotOutputs, "Outputs", "outputs") && slotOutputs.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in slotOutputs.EnumerateArray())
                {
                    color ??= ReadString(output, "Color") ?? ReadString(output, "color");
                    player ??= ReadInt(output, "Player") ?? ReadInt(output, "player");
                    id ??= ReadString(output, "Id") ?? ReadString(output, "id");
                    name ??= ReadString(output, "Name") ?? ReadString(output, "name");
                    function ??= ReadString(output, "Function") ?? ReadString(output, "function");
                    outputName ??= ReadString(output, "OutputName") ?? ReadString(output, "outputName") ?? ReadString(output, "output_name");
                }
            }

            outputs.Add(new PanelOutput
            {
                Player = player,
                Slot = slotNumber,
                Target = target,
                Color = color ?? "BLACK",
                Id = id,
                Name = name,
                Function = function,
                OutputName = outputName
            });
        }
    }

    private static IReadOnlyList<PanelOutput> CompletePhysicalSlots(IReadOnlyList<PanelOutput> outputs)
    {
        var slottedOutputs = outputs
            .Where(output => output.Slot.HasValue)
            .ToArray();
        if (slottedOutputs.Length == 0)
        {
            return outputs;
        }

        // If the stream already carries player-specific slots, anonymous slot rows are
        // usually placeholder/fallback data from the frontend. Keeping them creates a
        // second null-player panel that routes to P1 and can clear the real panel.
        if (slottedOutputs.Any(output => output.Player > 0))
        {
            slottedOutputs = slottedOutputs
                .Where(output => output.Player > 0)
                .ToArray();
        }

        var byPlayerSlot = slottedOutputs
            .GroupBy(PlayerSlotKey)
            .ToDictionary(group => group.Key, group => group.First());

        var players = byPlayerSlot.Keys
            .Select(key => key.Player)
            .Distinct()
            .OrderBy(player => player ?? int.MaxValue)
            .ToArray();
        if (players.Length == 0)
        {
            players = new int?[] { null };
        }

        var complete = players
            .SelectMany(player => Enumerable.Range(1, 8)
                .Select(slot => byPlayerSlot.TryGetValue((player, slot), out var output)
                    ? output
                    : new PanelOutput { Player = player, Slot = slot, Target = $"SLOT:{slot}", Color = "BLACK" }))
            .ToList();

        complete.AddRange(outputs.Where(output => !output.Slot.HasValue));
        return complete;
    }

    private static (int? Player, int Slot) PlayerSlotKey(PanelOutput output) => (output.Player, output.Slot!.Value);

    private static IReadOnlyList<PanelOutput> LoadDynpanelOutputs(string baseDirectory, string? system, string? rom, string? layoutId)
    {
        if (string.IsNullOrWhiteSpace(rom) || string.IsNullOrWhiteSpace(layoutId))
        {
            return Array.Empty<PanelOutput>();
        }

        foreach (var path in CandidateDynpanelPaths(baseDirectory, system, rom))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return ReadDynpanelOutputs(doc.RootElement, layoutId);
            }
            catch
            {
                return Array.Empty<PanelOutput>();
            }
        }

        return Array.Empty<PanelOutput>();
    }

    private static IEnumerable<string> CandidateDynpanelPaths(string baseDirectory, string? system, string rom)
    {
        var apiExposeRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "APIExpose"));
        yield return Path.Combine(apiExposeRoot, "resources", "dynpanels", "games", rom + ".json");
        if (!string.IsNullOrWhiteSpace(system))
        {
            yield return Path.Combine(apiExposeRoot, "resources", "dynpanels", "games", system, rom + ".json");
        }
    }

    private static IReadOnlyList<PanelOutput> ReadDynpanelOutputs(JsonElement root, string layoutId)
    {
        var results = new List<PanelOutput>();
        if (!TryGetPropertyAny(root, out var players, "players") || players.ValueKind != JsonValueKind.Object)
        {
            return results;
        }

        foreach (var playerEntry in players.EnumerateObject())
        {
            if (playerEntry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var player = int.TryParse(playerEntry.Name, out var parsedPlayer) ? parsedPlayer : 0;
            ReadDynpanelButtons(playerEntry.Value, layoutId, player, results);
            ReadDynpanelDeviceInputs(playerEntry.Value, layoutId, player, results);
            ReadDynpanelAxes(playerEntry.Value, layoutId, player, results);

            if (!TryGetPropertyAny(playerEntry.Value, out var systemOutputs, "system_outputs") ||
                systemOutputs.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                continue;
            }

            foreach (var (key, output) in EnumerateNamedObjects(systemOutputs))
            {
                var slots = ReadSlotsForLayout(output, layoutId);
                if (slots.Count == 0)
                {
                    slots = ReadSlotsFromPlayerLayout(playerEntry.Value, layoutId, "system_outputs", key);
                }

                if (slots.Count == 0)
                {
                    results.Add(BuildSystemOutput(output, key, player, null));
                    continue;
                }

                results.AddRange(slots.Select(slot => BuildSystemOutput(output, key, player, slot)));
            }
        }

        return results;
    }

    private static PanelOutput BuildSystemOutput(JsonElement output, string key, int player, int? slot)
    {
        return new PanelOutput
        {
            Player = ReadInt(output, "Player") ?? ReadInt(output, "player") ?? player,
            Slot = slot,
            Target = slot.HasValue ? $"SLOT:{slot.Value}" : null,
            Color = ReadString(output, "Color") ?? ReadString(output, "color") ?? "WHITE",
            Id = ReadString(output, "Id") ?? ReadString(output, "id") ?? key,
            Name = ReadString(output, "Name") ?? ReadString(output, "name"),
            Function = ReadString(output, "Function") ?? ReadString(output, "function"),
            OutputName = ReadString(output, "OutputName") ?? ReadString(output, "outputName") ?? ReadString(output, "output_name") ?? ReadString(output, "Name") ?? ReadString(output, "name")
        };
    }

    private static void ReadDynpanelButtons(JsonElement player, string layoutId, int playerIndex, List<PanelOutput> results)
    {
        if (!TryGetPropertyAny(player, out var buttons, "buttons") ||
            buttons.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return;
        }

        foreach (var (key, button) in EnumerateNamedObjects(buttons))
        {
            AddControlOutputs(results, key, button, layoutId, playerIndex, defaultColor: null, player, "buttons");
        }
    }

    private static void ReadDynpanelDeviceInputs(JsonElement player, string layoutId, int playerIndex, List<PanelOutput> results)
    {
        if (!TryGetPropertyAny(player, out var devices, "devices") || devices.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var device in devices.EnumerateArray())
        {
            if (device.ValueKind != JsonValueKind.Object ||
                !TryGetPropertyAny(device, out var inputs, "inputs") ||
                inputs.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                continue;
            }

            foreach (var (key, input) in EnumerateNamedObjects(inputs))
            {
                AddControlOutputs(results, key, input, layoutId, playerIndex, defaultColor: null, player, "inputs");
            }
        }
    }

    private static void ReadDynpanelAxes(JsonElement player, string layoutId, int playerIndex, List<PanelOutput> results)
    {
        if (!TryGetPropertyAny(player, out var axes, "axes") ||
            axes.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return;
        }

        foreach (var (key, axis) in EnumerateControlObjects(axes, "axis"))
        {
            AddControlOutputs(results, key, axis, layoutId, playerIndex, defaultColor: null, player, "axes");
        }
    }

    private static void AddControlOutputs(List<PanelOutput> results, string key, JsonElement control, string layoutId, int playerIndex, string? defaultColor, JsonElement? player = null, string? layoutGroup = null)
    {
        var slots = ReadSlotsForLayout(control, layoutId);
        if (slots.Count == 0 && player.HasValue && !string.IsNullOrWhiteSpace(layoutGroup))
        {
            slots = ReadSlotsFromPlayerLayout(player.Value, layoutId, layoutGroup, key);
        }

        if (slots.Count == 0)
        {
            return;
        }

        var color = ReadString(control, "Color") ?? ReadString(control, "color") ?? defaultColor;
        if (string.IsNullOrWhiteSpace(color))
        {
            return;
        }

        foreach (var slot in slots)
        {
            results.Add(new PanelOutput
            {
                Player = ReadInt(control, "Player") ?? ReadInt(control, "player") ?? playerIndex,
                Slot = slot,
                Target = $"SLOT:{slot}",
                Color = color,
                Id = ReadString(control, "Id") ?? ReadString(control, "id") ?? key,
                Name = ReadString(control, "Name") ?? ReadString(control, "name") ?? ReadString(control, "label") ?? key,
                Function = ReadString(control, "Function") ?? ReadString(control, "function"),
                OutputName = ReadString(control, "OutputName") ?? ReadString(control, "outputName") ?? ReadString(control, "output_name") ?? ReadString(control, "name")
            });
        }
    }

    private static IReadOnlyList<int> ReadSlotsForLayout(JsonElement output, string layoutId)
    {
        var slots = new List<int>();
        AddSlot(slots, ReadInt(output, "panel_slot"));

        if (TryGetPropertyAny(output, out var directSlots, "panel_slots"))
        {
            AddSlots(slots, directSlots);
        }

        if (TryGetPropertyAny(output, out var slotsByLayout, "slots_by_layout") && slotsByLayout.ValueKind == JsonValueKind.Object)
        {
            AddSlots(slots, GetPropertyCaseInsensitive(slotsByLayout, layoutId));
        }

        if (TryGetPropertyAny(output, out var layouts, "layouts") && layouts.ValueKind == JsonValueKind.Object)
        {
            var layout = GetPropertyCaseInsensitive(layouts, layoutId);
            if (layout.HasValue && layout.Value.ValueKind == JsonValueKind.Object)
            {
                AddSlot(slots, ReadInt(layout.Value, "panel_slot"));
                if (TryGetPropertyAny(layout.Value, out var layoutSlots, "panel_slots"))
                {
                    AddSlots(slots, layoutSlots);
                }
            }
        }

        return slots.Distinct().ToArray();
    }

    private static IReadOnlyList<int> ReadSlotsFromPlayerLayout(JsonElement player, string layoutId, string groupName, string key)
    {
        if (!TryGetPropertyAny(player, out var layouts, "layouts") || layouts.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<int>();
        }

        var layout = GetPropertyCaseInsensitive(layouts, layoutId);
        if (!layout.HasValue || layout.Value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<int>();
        }

        if (!TryGetPropertyAny(layout.Value, out var group, groupName) || group.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<int>();
        }

        var entry = GetPropertyCaseInsensitive(group, key);
        if (!entry.HasValue)
        {
            return Array.Empty<int>();
        }

        var slots = new List<int>();
        AddSlot(slots, ReadInt(entry.Value, "panel_slot"));
        if (TryGetPropertyAny(entry.Value, out var panelSlots, "panel_slots"))
        {
            AddSlots(slots, panelSlots);
        }

        return slots.Distinct().ToArray();
    }

    private static IEnumerable<(string Key, JsonElement Value)> EnumerateNamedObjects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    yield return (property.Name, property.Value);
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var value in node.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var key = ReadString(value, "id") ?? ReadString(value, "name") ?? index.ToString();
                    yield return (key, value);
                }
                index++;
            }
        }
    }

    private static IEnumerable<(string Key, JsonElement Value)> EnumerateControlObjects(JsonElement node, string fallbackPrefix)
    {
        if (node.ValueKind == JsonValueKind.Object && LooksLikeControlObject(node))
        {
            var key = ReadString(node, "id") ?? ReadString(node, "input") ?? ReadString(node, "name") ?? fallbackPrefix;
            yield return (key, node);
            yield break;
        }

        foreach (var item in EnumerateNamedObjects(node))
        {
            yield return item;
        }
    }

    private static bool LooksLikeControlObject(JsonElement node)
    {
        return node.ValueKind == JsonValueKind.Object &&
            (node.TryGetProperty("mame", out _) ||
             node.TryGetProperty("layouts", out _) ||
             node.TryGetProperty("panel_slot", out _) ||
             node.TryGetProperty("slots_by_layout", out _) ||
             node.TryGetProperty("color", out _) ||
             node.TryGetProperty("Color", out _));
    }

    private static int? ReadSlotForLayout(JsonElement output, string layoutId)
    {
        var direct = ReadInt(output, "panel_slot");
        if (direct.HasValue)
        {
            return direct;
        }

        if (TryGetPropertyAny(output, out var slotsByLayout, "slots_by_layout") && slotsByLayout.ValueKind == JsonValueKind.Object)
        {
            var slot = ReadInt(GetPropertyCaseInsensitive(slotsByLayout, layoutId));
            if (slot.HasValue)
            {
                return slot;
            }
        }

        if (TryGetPropertyAny(output, out var layouts, "layouts") && layouts.ValueKind == JsonValueKind.Object)
        {
            var layout = GetPropertyCaseInsensitive(layouts, layoutId);
            if (layout.HasValue && layout.Value.ValueKind == JsonValueKind.Object)
            {
                return ReadInt(layout.Value, "panel_slot");
            }
        }

        return null;
    }

    private static void AddSlot(List<int> slots, int? slot)
    {
        if (slot is >= 1 and <= 8)
        {
            slots.Add(slot.Value);
        }
    }

    private static void AddSlots(List<int> slots, JsonElement? node)
    {
        if (!node.HasValue)
        {
            return;
        }

        var value = node.Value;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                AddSlot(slots, ReadInt(item));
            }

            return;
        }

        AddSlot(slots, ReadInt(value));
    }

    private static JsonElement? FindObject(JsonElement root, string name)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static bool TryGetPropertyAny(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement root, string property) => LedEvent.ReadString(root, property);

    private static string? ReadStringNullable(JsonElement? root, string property)
    {
        return root.HasValue ? LedEvent.ReadString(root.Value, property) : null;
    }

    private static int? ReadInt(JsonElement root, string property) => LedEvent.ReadInt(root, property);

    private static int? ReadInt(JsonElement? node)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var value = node.Value;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }

    private static long? ReadLong(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : null;
    }

    private static JsonElement? GetPropertyCaseInsensitive(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var item in root.EnumerateObject())
        {
            if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return null;
    }

    private static bool IsBlack(string? color) =>
        string.IsNullOrWhiteSpace(color) ||
        color.Equals("BLACK", StringComparison.OrdinalIgnoreCase);

    private static PanelOutput CopyWithColor(PanelOutput output, string color) => new()
    {
        Player = output.Player,
        Slot = output.Slot,
        Target = output.Target,
        Color = color,
        Id = output.Id,
        Name = output.Name,
        Function = output.Function,
        OutputName = output.OutputName
    };

    private static bool SignalMatches(string? value, string normalizedSignal)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var clean = value.Split('#', 2)[0];
        return NormalizeSignal(clean).Equals(normalizedSignal, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSignal(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? ReadStringDeep(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            foreach (var child in root.EnumerateObject())
            {
                var found = ReadStringDeep(child.Value, property);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in root.EnumerateArray())
            {
                var found = ReadStringDeep(child, property);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static long? ReadLongDeep(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            var direct = ReadLong(root, property);
            if (direct.HasValue)
            {
                return direct;
            }

            foreach (var child in root.EnumerateObject())
            {
                var found = ReadLongDeep(child.Value, property);
                if (found.HasValue)
                {
                    return found;
                }
            }
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in root.EnumerateArray())
            {
                var found = ReadLongDeep(child, property);
                if (found.HasValue)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed class PanelOutput
{
    public int? Player { get; init; }
    public int? Slot { get; init; }
    public string? Target { get; init; }
    public string Color { get; init; } = "BLACK";
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Function { get; init; }
    public string? OutputName { get; init; }
}
