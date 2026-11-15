using System.Globalization;
using System.Text.Json;

namespace LedManager.Core.Runtime;

/// <summary>
/// deck.semantics (nelfeplay.deck.semantics/1): what the .MEM signals of the launched
/// game mean, read from its .SMEM by APIExpose. Published on /ws/ingame and /ws/arcade
/// since 1.9.185 for every game that has a .SMEM, Deck or not.
/// </summary>
public sealed record SemanticsSnapshot(
    string LaunchId,
    string? SystemId,
    string? Rom,
    string Status,
    long Revision,
    IReadOnlyList<SemanticSignal> Signals,
    IReadOnlyList<SemanticInstrument> Instruments)
{
    public bool IsLoaded => Status.Equals("loaded", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A .MEM line quoted by a signal: address (as a number), action, player (null = any player).</summary>
public sealed record SemanticRef(long Address, string Action, int? Player);

/// <summary>Enumeration entry: exact raw value, or raw range, giving a value and a label.</summary>
public sealed record SemanticValue(long? Raw, long? RawMin, long? RawMax, long? Value, string? Label)
{
    public bool Matches(long raw) =>
        Raw.HasValue ? Raw.Value == raw : RawMin.HasValue && RawMax.HasValue && raw >= RawMin.Value && raw <= RawMax.Value;
}

public sealed record SemanticSignal(
    string Id,
    int? Player,
    string Kind,
    string? Label,
    IReadOnlyList<SemanticRef> From,
    IReadOnlyList<SemanticValue> Values,
    long ScaleNum,
    long ScaleDen,
    long ScaleAdd,
    long? Min,
    long? Max,
    long? Initial,
    string Quality,
    bool Available);

public sealed record SemanticInstrument(string Id, string? Label, int? Player, string Show, IReadOnlyList<string> Signals);

/// <summary>
/// The value of a signal as displayed. <see cref="Observed"/> = at least one of its .MEM
/// lines spoke during this launch (otherwise the value is the .SMEM <c>Initial</c>).
/// <see cref="Valid"/> = false when unavailable (unproven), unknown or out of [Min, Max].
/// </summary>
public readonly record struct SignalReading(bool Observed, long? Raw, long? Value, string? Label, bool Valid)
{
    public static SignalReading Unknown { get; } = new(false, null, null, null, false);
}

public sealed record SignalValueChange(SemanticSignal Signal, SignalReading Previous, SignalReading Current);

/// <summary>A memory event reduced to what the semantics need.</summary>
public readonly record struct MemorySignal(long Address, string Action, int? Player, long Raw);

public static class SignalSemantics
{
    /// <summary>The only schema read: another number would mean another contract.</summary>
    public const string Schema = "nelfeplay.deck.semantics/1";

    public static bool IsSemanticsEvent(LedEvent evt) =>
        (evt.Type ?? "").Equals("deck.semantics", StringComparison.OrdinalIgnoreCase);

    public static bool IsMemoryEvent(LedEvent evt)
    {
        var type = evt.Type ?? "";
        return type.Equals("retroarch.memory.changed", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ingame.memory.changed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a deck.semantics envelope (or its bare payload). Null when it is not one.</summary>
    public static SemanticsSnapshot? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var payload = Get(root, "Payload") is { ValueKind: JsonValueKind.Object } inner ? inner : root;
        var schema = String(payload, "Schema");
        if (schema is not null && !schema.Trim().Equals(Schema, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var status = String(payload, "Status") ?? "";
        if (status.Length == 0)
        {
            return null;
        }

        var signals = new List<SemanticSignal>();
        if (Get(payload, "Signals") is { ValueKind: JsonValueKind.Array } signalArray)
        {
            foreach (var item in signalArray.EnumerateArray())
            {
                if (ParseSignal(item) is { } signal)
                {
                    signals.Add(signal);
                }
            }
        }

        var instruments = new List<SemanticInstrument>();
        if (Get(payload, "Instruments") is { ValueKind: JsonValueKind.Array } instrumentArray)
        {
            foreach (var item in instrumentArray.EnumerateArray())
            {
                var id = String(item, "Id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var ids = Get(item, "Signals") is { ValueKind: JsonValueKind.Array } list
                    ? list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray()
                    : Array.Empty<string>();
                instruments.Add(new SemanticInstrument(id, String(item, "Label"), Int(item, "Player"), String(item, "Show") ?? "", ids));
            }
        }

        return new SemanticsSnapshot(
            String(payload, "LaunchId") ?? "",
            String(payload, "SystemId"),
            String(payload, "Rom"),
            status,
            Long(payload, "Revision") ?? 0,
            signals,
            instruments);
    }

    /// <summary>
    /// Reads a retroarch.memory.changed / ingame.memory.changed event: <c>signal.Address</c>,
    /// <c>signal.Name</c> (the action), <c>signal.Player</c> (else the payload <c>player</c>
    /// of the MAME Lua bridge) and <c>signal.Value</c> (raw).
    /// </summary>
    public static MemorySignal? ReadMemory(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var payload = Get(root, "Payload") is { ValueKind: JsonValueKind.Object } inner ? inner : root;
        if (Get(payload, "signal") is not { ValueKind: JsonValueKind.Object } signal)
        {
            return null;
        }

        var address = ParseAddress(String(signal, "Address"));
        var action = String(signal, "Name");
        var raw = Long(signal, "Value") ?? Long(payload, "Value");
        if (address is null || string.IsNullOrWhiteSpace(action) || raw is null)
        {
            return null;
        }

        return new MemorySignal(address.Value, action.Trim(), Int(signal, "Player") ?? Int(payload, "player"), raw.Value);
    }

    /// <summary>"0xFF8305", "0XFF8305", "FF8305" -> 0xFF8305; compared as numbers, as the .SMEM says.</summary>
    public static long? ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        var end = 0;
        while (end < value.Length && Uri.IsHexDigit(value[end]))
        {
            end++;
        }

        return end > 0 && long.TryParse(value[..end], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address)
            ? address
            : null;
    }

    /// <summary>
    /// Displayed value of a signal from the raw value of its last line: the matching
    /// <c>Values</c> entry (its value, else the raw), otherwise raw * Num / Den + Add;
    /// outside [Min, Max] it is not shown.
    /// </summary>
    public static SignalReading Read(SemanticSignal signal, long? raw)
    {
        if (raw is null)
        {
            var initialValid = signal.Available && signal.Initial.HasValue && InBounds(signal, signal.Initial.Value);
            return new SignalReading(false, null, signal.Initial, null, initialValid);
        }

        long value;
        string? label = null;
        var entry = signal.Values.FirstOrDefault(v => v.Matches(raw.Value));
        if (entry is not null)
        {
            value = entry.Value ?? raw.Value;
            label = entry.Label;
        }
        else
        {
            var den = signal.ScaleDen == 0 ? 1 : signal.ScaleDen;
            value = raw.Value * signal.ScaleNum / den + signal.ScaleAdd;
        }

        return new SignalReading(true, raw, value, label, signal.Available && InBounds(signal, value));
    }

    private static bool InBounds(SemanticSignal signal, long value) =>
        (!signal.Min.HasValue || value >= signal.Min.Value) && (!signal.Max.HasValue || value <= signal.Max.Value);

    private static SemanticSignal? ParseSignal(JsonElement item)
    {
        var id = String(item, "Id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var from = new List<SemanticRef>();
        if (Get(item, "From") is { ValueKind: JsonValueKind.Array } refs)
        {
            foreach (var r in refs.EnumerateArray())
            {
                var address = ParseAddress(String(r, "Address"));
                var action = String(r, "Action");
                if (address.HasValue && !string.IsNullOrWhiteSpace(action))
                {
                    from.Add(new SemanticRef(address.Value, action.Trim(), Int(r, "Player")));
                }
            }
        }

        var values = new List<SemanticValue>();
        if (Get(item, "Values") is { ValueKind: JsonValueKind.Array } entries)
        {
            foreach (var v in entries.EnumerateArray())
            {
                values.Add(new SemanticValue(Long(v, "Raw"), Long(v, "RawMin"), Long(v, "RawMax"), Long(v, "Value"), String(v, "Label")));
            }
        }

        var scale = Get(item, "Scale") is { ValueKind: JsonValueKind.Object } s ? s : default;
        return new SemanticSignal(
            id.Trim(),
            Int(item, "Player"),
            String(item, "Kind") ?? "",
            String(item, "Label"),
            from,
            values,
            scale.ValueKind == JsonValueKind.Object ? Long(scale, "Num") ?? 1 : 1,
            scale.ValueKind == JsonValueKind.Object ? Long(scale, "Den") ?? 1 : 1,
            scale.ValueKind == JsonValueKind.Object ? Long(scale, "Add") ?? 0 : 0,
            Long(item, "Min"),
            Long(item, "Max"),
            Long(item, "Initial"),
            String(item, "Quality") ?? "",
            Bool(item, "Available") ?? true);
    }

    private static JsonElement? Get(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? String(JsonElement element, string name) => Get(element, name) switch
    {
        { ValueKind: JsonValueKind.String } v => v.GetString(),
        { ValueKind: JsonValueKind.Number } v => v.GetRawText(),
        _ => null
    };

    private static long? Long(JsonElement element, string name) => Get(element, name) switch
    {
        { ValueKind: JsonValueKind.Number } v when v.TryGetInt64(out var n) => n,
        { ValueKind: JsonValueKind.String } v when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null
    };

    private static int? Int(JsonElement element, string name) => Long(element, name) is { } n && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;

    private static bool? Bool(JsonElement element, string name) => Get(element, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null
    };
}

/// <summary>
/// Keeps the semantics of the running launch and the current value of each of its
/// signals. A signal's value is the raw value of the LAST event of any of its .MEM lines
/// (same address, same action, same player; a line without player accepts any player).
/// Events are remembered per line, so a value seen before the semantics arrived (the
/// streams race each other) still counts. Pure state: no I/O, no clock.
/// </summary>
public sealed class SignalSemanticsTracker
{
    private readonly Dictionary<(long Address, string Action, int Player), (long Raw, long Seq)> _lines = new();
    private long _seq;

    /// <summary>The launch announced by ui.game.started; null between two games.</summary>
    public string? RunningLaunchId { get; private set; }

    /// <summary>The semantics in force (any Status), null when none was received for this launch.</summary>
    public SemanticsSnapshot? Current { get; private set; }

    /// <summary>Semantics received for a launch that has not started here yet.</summary>
    public SemanticsSnapshot? Pending { get; private set; }

    public IReadOnlyList<SemanticSignal> Signals =>
        Current is { IsLoaded: true } current ? current.Signals : Array.Empty<SemanticSignal>();

    public IReadOnlyList<SemanticInstrument> Instruments =>
        Current is { IsLoaded: true } current ? current.Instruments : Array.Empty<SemanticInstrument>();

    /// <summary>A game starts (or its start is confirmed) under this launch id.</summary>
    public void BeginLaunch(string? launchId)
    {
        var id = launchId?.Trim() ?? "";
        if (RunningLaunchId is not null && !RunningLaunchId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            // another launch: what the previous one measured is not this game's
            _lines.Clear();
        }

        RunningLaunchId = id;
        if (Current is not null && Current.LaunchId.Length > 0 && id.Length > 0 &&
            !Current.LaunchId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            Current = null;
        }

        if (Pending is not null && id.Length > 0 && Pending.LaunchId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            Current = Pending;
        }

        Pending = null;
    }

    /// <summary>
    /// The running game, started without id (the .raw form comes first), is now known
    /// under this id: same game, so the values measured so far are kept.
    /// </summary>
    public void AdoptLaunchId(string launchId)
    {
        var id = launchId.Trim();
        if (id.Length == 0 || !string.IsNullOrEmpty(RunningLaunchId))
        {
            return;
        }

        RunningLaunchId = id;
        if (Current is not null && Current.LaunchId.Length > 0 && !Current.LaunchId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            Current = null;
        }

        if (Pending is not null && Pending.LaunchId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            Current = Pending;
            Pending = null;
        }
    }

    /// <summary>
    /// The game ends. An end that names another launch than the running one is not
    /// this game's (false, nothing cleared).
    /// </summary>
    public bool EndLaunch(string? launchId)
    {
        var id = launchId?.Trim() ?? "";
        if (id.Length > 0 && !string.IsNullOrEmpty(RunningLaunchId) &&
            !RunningLaunchId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // the next game's semantics may come (on /ws/ingame) before this end (on /ws/frontend)
        var next = Pending is not null && !Pending.LaunchId.Equals(id, StringComparison.OrdinalIgnoreCase) ? Pending : null;
        Reset();
        Pending = next;
        return true;
    }

    public void Reset()
    {
        RunningLaunchId = null;
        Current = null;
        Pending = null;
        _lines.Clear();
    }

    /// <summary>
    /// Takes a deck.semantics. Kept for the running launch (or when no launch is known yet);
    /// one that names another launch waits in <see cref="Pending"/> until that launch starts.
    /// </summary>
    public SemanticsAcceptance Apply(SemanticsSnapshot snapshot)
    {
        if (!string.IsNullOrEmpty(RunningLaunchId) && snapshot.LaunchId.Length > 0 &&
            !RunningLaunchId.Equals(snapshot.LaunchId, StringComparison.OrdinalIgnoreCase))
        {
            Pending = snapshot;
            return SemanticsAcceptance.OtherLaunch;
        }

        if (Current is not null &&
            Current.LaunchId.Equals(snapshot.LaunchId, StringComparison.OrdinalIgnoreCase) &&
            Current.Revision == snapshot.Revision &&
            Current.Status.Equals(snapshot.Status, StringComparison.OrdinalIgnoreCase))
        {
            // the same reading again: carried by both /ws/ingame and /ws/arcade, and resent on reconnect
            return SemanticsAcceptance.Unchanged;
        }

        Current = snapshot;
        return SemanticsAcceptance.Accepted;
    }

    /// <summary>Every signal as it reads now, as changes from unknown: to re-evaluate after a new reading.</summary>
    public IReadOnlyList<SignalValueChange> ReadAll() =>
        Signals.Select(signal => new SignalValueChange(signal, SignalReading.Unknown, Read(signal))).ToArray();

    /// <summary>Records a memory event and returns the signals whose value it touched.</summary>
    public IReadOnlyList<SignalValueChange> Observe(MemorySignal memory)
    {
        var signals = Signals.Where(signal => signal.From.Any(r => Matches(r, memory))).ToArray();
        var before = signals.Select(Read).ToArray();
        _lines[(memory.Address, memory.Action.ToUpperInvariant(), memory.Player ?? 0)] = (memory.Raw, ++_seq);

        var changes = new List<SignalValueChange>(signals.Length);
        for (var i = 0; i < signals.Length; i++)
        {
            changes.Add(new SignalValueChange(signals[i], before[i], Read(signals[i])));
        }

        return changes;
    }

    public SignalReading Read(string id)
    {
        var signal = Signals.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return signal is null ? SignalReading.Unknown : Read(signal);
    }

    public SignalReading Read(SemanticSignal signal)
    {
        long? raw = null;
        var bestSeq = -1L;
        foreach (var reference in signal.From)
        {
            foreach (var line in _lines)
            {
                if (line.Key.Address != reference.Address ||
                    !line.Key.Action.Equals(reference.Action, StringComparison.OrdinalIgnoreCase) ||
                    (reference.Player.HasValue && line.Key.Player != reference.Player.Value))
                {
                    continue;
                }

                if (line.Value.Seq > bestSeq)
                {
                    bestSeq = line.Value.Seq;
                    raw = line.Value.Raw;
                }
            }
        }

        return SignalSemantics.Read(signal, raw);
    }

    private static bool Matches(SemanticRef reference, MemorySignal memory) =>
        reference.Address == memory.Address &&
        reference.Action.Equals(memory.Action, StringComparison.OrdinalIgnoreCase) &&
        (!reference.Player.HasValue || reference.Player == memory.Player);
}

public enum SemanticsAcceptance
{
    Accepted,
    Unchanged,
    OtherLaunch
}
