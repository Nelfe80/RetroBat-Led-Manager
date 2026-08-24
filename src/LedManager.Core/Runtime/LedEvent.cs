using System.Text.Json;

namespace LedManager.Core.Runtime;

public sealed class LedEvent
{
    public string Type { get; init; } = "";
    public string Stream { get; init; } = "";
    public int? Player { get; init; }
    public string? Sender { get; init; }
    public string? Action { get; init; }
    public string? Family { get; init; }
    public string? Effect { get; init; }
    public string? Target { get; init; }
    public int? Slot { get; init; }
    public string? Color { get; init; }
    public string? State { get; init; }
    public string? Value { get; init; }
    public string? Text { get; init; }
    public string? System { get; init; }
    public string? Rom { get; init; }
    public int DelayMs { get; init; }
    public int? FlashMs { get; init; }
    public JsonElement Payload { get; init; }

    public bool IsFrontendGameStart =>
        Type.Equals("ui.game.started.raw", StringComparison.OrdinalIgnoreCase) ||
        Type.Equals("ui.game.started", StringComparison.OrdinalIgnoreCase) ||
        Type.Equals("game.start", StringComparison.OrdinalIgnoreCase);

    public bool IsFrontendGameEnd =>
        Type.Equals("ui.game.ended.raw", StringComparison.OrdinalIgnoreCase) ||
        Type.Equals("ui.game.ended", StringComparison.OrdinalIgnoreCase) ||
        Type.Equals("game.end", StringComparison.OrdinalIgnoreCase);

    /// <summary>EmulationStation exited (APIExpose 1.3.5+) - authoritative shutdown signal.</summary>
    public bool IsFrontendStopped =>
        Type.Equals("ui.frontend.stopped", StringComparison.OrdinalIgnoreCase);

    public bool IsPanelState =>
        Type.Equals("panel.state", StringComparison.OrdinalIgnoreCase) ||
        Type.Equals("panel.current.changed", StringComparison.OrdinalIgnoreCase) ||
        // Real APIExpose /ws/panel pushes carry no "type"/"stream" wrapper at all - they're
        // raw {ActivePanel: {...}, ActiveLayout: {...}, ...} objects on the "panel" stream.
        (Stream.Equals("panel", StringComparison.OrdinalIgnoreCase) && HasActivePanel);

    private bool HasActivePanel =>
        GetObject(Payload, "ActivePanel").HasValue || GetObject(Payload, "activePanel").HasValue;

    public bool IsLiveScore =>
        Stream.Equals("ingame", StringComparison.OrdinalIgnoreCase) &&
        (Type.Contains("score", StringComparison.OrdinalIgnoreCase) || Action?.Contains("SCORE", StringComparison.OrdinalIgnoreCase) == true);

    public bool IsHiscore =>
        Stream.Equals("hiscore", StringComparison.OrdinalIgnoreCase) ||
        Type.StartsWith("hiscore.", StringComparison.OrdinalIgnoreCase);

    public static LedEvent FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromElement(doc.RootElement);
    }

    public static LedEvent FromElement(JsonElement root)
    {
        // Real APIExpose wrapper events (retroarch.wrapper.changed / retroarch.memory.changed)
        // arrive as an EventEnvelope: {"Type": "...", "Payload": { SystemId, Rom, "signal": { Channel, Name, Value, ... } }}.
        // Unwrap the Payload and treat its nested "signal" as an implicit mem.action so
        // DefaultEffectCatalog.Resolve can match on signal.Name against the existing actionRules.
        JsonElement? payload = GetObject(root, "payload") ?? GetObject(root, "Payload");
        var signalSource = payload ?? root;
        JsonElement? signal = GetObject(signalSource, "signal") ?? GetObject(signalSource, "Signal");
        var payloadAction = payload.HasValue
            ? ReadString(payload.Value, "actionType") ?? ReadString(payload.Value, "ActionType")
            : null;
        var payloadFamily = payload.HasValue
            ? ReadString(payload.Value, "family") ?? ReadString(payload.Value, "Family")
            : null;
        var payloadColor = payload.HasValue
            ? ReadString(payload.Value, "color") ?? ReadString(payload.Value, "Color")
            : null;
        var payloadPlayer = payload.HasValue
            ? ReadInt(payload.Value, "player") ?? ReadInt(payload.Value, "Player")
            : null;
        var hasMemPayload = signal.HasValue || !string.IsNullOrWhiteSpace(payloadAction);

        var type = ReadString(root, "type") ?? ReadString(root, "Type") ?? (hasMemPayload ? "mem.action" : "");
        var stream = ReadString(root, "stream") ?? ReadString(root, "Stream") ?? (hasMemPayload ? "mem" : InferStream(type));

        return new LedEvent
        {
            Type = type,
            Stream = stream,
            Player = ReadInt(root, "player") ?? ReadInt(root, "Player") ?? payloadPlayer
                ?? (signal.HasValue ? ReadInt(signal.Value, "Player") ?? ReadInt(signal.Value, "player") : null),
            Sender = ReadString(root, "sender") ?? ReadString(root, "Sender"),
            Action = ReadString(root, "action") ?? ReadString(root, "Action") ?? payloadAction ?? (signal.HasValue ? ReadString(signal.Value, "Name") : null),
            Family = ReadString(root, "family") ?? ReadString(root, "Family") ?? payloadFamily ?? (signal.HasValue ? ReadString(signal.Value, "Family") ?? ReadString(signal.Value, "family") : null),
            Effect = ReadString(root, "effect") ?? ReadString(root, "Effect"),
            Target = ReadString(root, "target") ?? ReadString(root, "Target"),
            Slot = ReadInt(root, "slot") ?? ReadInt(root, "Slot"),
            Color = ReadString(root, "color") ?? ReadString(root, "Color") ?? payloadColor ?? (signal.HasValue ? ReadString(signal.Value, "Color") ?? ReadString(signal.Value, "color") : null),
            State = ReadString(root, "state") ?? ReadString(root, "State"),
            Value = ReadString(root, "value") ?? ReadString(root, "Value") ?? ReadString(root, "score") ?? (payload.HasValue ? ReadString(payload.Value, "Value") : null) ?? (signal.HasValue ? ReadString(signal.Value, "Value") : null),
            Text = ReadString(root, "text") ?? ReadString(root, "Text"),
            System = ReadString(root, "system") ?? ReadString(root, "System") ?? ReadString(signalSource, "SystemId") ?? ReadString(signalSource, "systemId"),
            Rom = ReadString(root, "rom") ?? ReadString(root, "Rom") ?? ReadString(root, "game") ?? ReadString(signalSource, "Rom") ?? ReadString(signalSource, "rom") ?? ReadString(signalSource, "game"),
            DelayMs = ReadInt(root, "delayMs") ?? ReadInt(root, "DelayMs") ?? 0,
            Payload = root.Clone()
        };
    }

    private static JsonElement? GetObject(JsonElement root, string property)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : (JsonElement?)null;
    }

    public static string? ReadString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static int? ReadInt(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }

    private static string InferStream(string type)
    {
        if (type.StartsWith("ui.", StringComparison.OrdinalIgnoreCase))
        {
            return "frontend";
        }

        var dot = type.IndexOf('.');
        return dot > 0 ? type[..dot] : "";
    }
}
