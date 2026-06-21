namespace LedManager.Core.Runtime;

public sealed class IngamePanelOverlay
{
    private readonly Dictionary<string, PanelOutput> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private PanelState? _basePanel;
    private bool _fullRestoreSent;
    private string? _lastFullRestoreFingerprint;

    public bool IsActive => _basePanel is not null && !_basePanel.IsEmpty;

    public PanelState? BasePanel => _basePanel;

    public void Begin(PanelState basePanel)
    {
        _basePanel = basePanel;
        _overrides.Clear();
        _fullRestoreSent = false;
        _lastFullRestoreFingerprint = null;
    }

    public void Clear()
    {
        _basePanel = null;
        _overrides.Clear();
        _fullRestoreSent = false;
        _lastFullRestoreFingerprint = null;
    }

    public PanelOutput? FindBaseOutputSignal(string signal) => _basePanel?.FindOutputSignal(signal);

    public IReadOnlyList<PanelOutput> FindBaseOutputSignals(string signal) =>
        _basePanel?.FindOutputSignals(signal) ?? Array.Empty<PanelOutput>();

    public void SetOverride(PanelOutput output, string color)
    {
        foreach (var key in KeysFor(output))
        {
            _overrides[key] = CopyWithColor(output, color);
        }
    }

    public PanelOutput? ClearOverride(PanelOutput output)
    {
        foreach (var key in KeysFor(output))
        {
            _overrides.Remove(key);
        }

        return FindEffectiveOutput(output);
    }

    public PanelOutput? FindEffectiveOutputForEvent(LedEvent evt)
    {
        if (!IsActive)
        {
            return null;
        }

        if (evt.Slot.HasValue)
        {
            return FindEffectiveSlot(evt.Slot.Value);
        }

        var slot = ParseSlotTarget(evt.Target);
        if (slot.HasValue)
        {
            return FindEffectiveSlot(slot.Value);
        }

        if (!string.IsNullOrWhiteSpace(evt.Target))
        {
            return FindEffectiveTarget(evt.Target);
        }

        return null;
    }

    public PanelState? BuildEffectivePanel()
    {
        if (!IsActive || _basePanel is null)
        {
            return null;
        }

        var outputs = _basePanel.Outputs
            .Select(output => FindOverride(output) is { } overlay
                ? CopyWithColor(output, overlay.Color)
                : output)
            .ToList();

        foreach (var overlay in _overrides.Values)
        {
            if (!outputs.Any(output => KeysFor(output).Intersect(KeysFor(overlay), StringComparer.OrdinalIgnoreCase).Any()))
            {
                outputs.Add(overlay);
            }
        }

        return new PanelState
        {
            System = _basePanel.System,
            Rom = _basePanel.Rom,
            PanelId = _basePanel.PanelId,
            LayoutId = _basePanel.LayoutId,
            Sequence = _basePanel.Sequence,
            CapturedAt = _basePanel.CapturedAt,
            Outputs = outputs
        };
    }

    public IngameFullRestorePlan PlanFullRestore()
    {
        var panel = BuildEffectivePanel();
        if (panel is null || panel.IsEmpty)
        {
            return IngameFullRestorePlan.Skip("no-base-panel", "");
        }

        var fingerprint = Fingerprint(panel);
        if (_fullRestoreSent)
        {
            var reason = string.Equals(fingerprint, _lastFullRestoreFingerprint, StringComparison.OrdinalIgnoreCase)
                ? "duplicate"
                : "already-sent";
            _lastFullRestoreFingerprint = fingerprint;
            return IngameFullRestorePlan.Skip(reason, fingerprint);
        }

        _fullRestoreSent = true;
        _lastFullRestoreFingerprint = fingerprint;
        return IngameFullRestorePlan.Send("session-first-all-off", fingerprint, panel);
    }

    public static bool IsOffColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        return color.Equals("BLACK", StringComparison.OrdinalIgnoreCase) ||
            color.Equals("OFF", StringComparison.OrdinalIgnoreCase) ||
            color.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            color.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
            color.Equals("NO", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllTarget(LedEvent evt)
    {
        var target = evt.Target ?? "";
        return target.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
            evt.Action?.Equals("all", StringComparison.OrdinalIgnoreCase) == true;
    }

    private PanelOutput? FindEffectiveOutput(PanelOutput output)
    {
        if (output.Slot.HasValue)
        {
            return FindEffectiveSlot(output.Slot.Value);
        }

        return !string.IsNullOrWhiteSpace(output.Target)
            ? FindEffectiveTarget(output.Target)
            : null;
    }

    private PanelOutput? FindEffectiveSlot(int slot)
    {
        var key = SlotKey(slot);
        if (_overrides.TryGetValue(key, out var overlay))
        {
            return overlay;
        }

        return _basePanel?.Outputs.FirstOrDefault(output => output.Slot == slot);
    }

    private PanelOutput? FindEffectiveTarget(string target)
    {
        var normalized = TargetKey(target);
        if (_overrides.TryGetValue(normalized, out var overlay))
        {
            return overlay;
        }

        return _basePanel?.Outputs.FirstOrDefault(output =>
            !string.IsNullOrWhiteSpace(output.Target) &&
            TargetKey(output.Target).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private PanelOutput? FindOverride(PanelOutput output)
    {
        foreach (var key in KeysFor(output))
        {
            if (_overrides.TryGetValue(key, out var overlay))
            {
                return overlay;
            }
        }

        return null;
    }

    private static IEnumerable<string> KeysFor(PanelOutput output)
    {
        if (output.Slot.HasValue)
        {
            yield return SlotKey(output.Slot.Value);
        }

        if (!string.IsNullOrWhiteSpace(output.Target))
        {
            yield return TargetKey(output.Target);
        }
    }

    private static int? ParseSlotTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var normalized = target.Trim();
        if (normalized.Length > 3 && (normalized[0] == 'P' || normalized[0] == 'p') && char.IsDigit(normalized[1]) && normalized[2] == ':')
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["SLOT:".Length..];
        }

        return int.TryParse(normalized, out var slot) ? slot : null;
    }

    private static string SlotKey(int slot) => "slot:" + slot;

    private static string TargetKey(string target)
    {
        var normalized = target.Trim();
        if (normalized.Length > 3 && (normalized[0] == 'P' || normalized[0] == 'p') && char.IsDigit(normalized[1]) && normalized[2] == ':')
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith("GLOBAL:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["GLOBAL:".Length..];
        }

        return "target:" + normalized.ToUpperInvariant();
    }

    private static string Fingerprint(PanelState panel)
    {
        return string.Join("|", panel.Outputs
            .Select(output => $"{output.Player?.ToString() ?? "-"}:{output.Slot?.ToString() ?? TargetKey(output.Target ?? output.OutputName ?? output.Id ?? "-")}:{normalize(output.Color)}")
            .Order(StringComparer.OrdinalIgnoreCase));

        static string normalize(string? value) => string.IsNullOrWhiteSpace(value) ? "BLACK" : value.Trim().ToUpperInvariant();
    }

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
}

public sealed class IngameFullRestorePlan
{
    private IngameFullRestorePlan(bool shouldSend, string reason, string fingerprint, PanelState? panel)
    {
        ShouldSend = shouldSend;
        Reason = reason;
        Fingerprint = fingerprint;
        Panel = panel;
    }

    public bool ShouldSend { get; }
    public string Reason { get; }
    public string Fingerprint { get; }
    public PanelState? Panel { get; }

    public static IngameFullRestorePlan Send(string reason, string fingerprint, PanelState panel) => new(true, reason, fingerprint, panel);

    public static IngameFullRestorePlan Skip(string reason, string fingerprint) => new(false, reason, fingerprint, null);
}
