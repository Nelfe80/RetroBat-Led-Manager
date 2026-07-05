using LedManager.Core.Configuration;
using LedManager.Core.Runtime;

namespace LedManager.Core.Routing;

public sealed class CommandRouter
{
    private readonly LedManagerConfig _config;

    public CommandRouter(LedManagerConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<RoutedCommand> RouteEvent(LedEvent evt)
    {
        if (evt.IsFrontendGameStart || evt.IsFrontendGameEnd || evt.IsPanelState || evt.IsHiscore)
        {
            return Array.Empty<RoutedCommand>();
        }

        if (evt.IsLiveScore)
        {
            var scoreEvent = evt.WithValue(evt.Value ?? LedEvent.ReadString(evt.Payload, "score") ?? "");
            return new[] { BuildCommand(scoreEvent, _config.Templates.MatrixScore, "GLOBAL:MATRIX1", "GREEN") };
        }

        if (Is(evt.Effect, "matrix_score") || Is(evt.Action, "matrix_score"))
        {
            return new[] { BuildCommand(evt, _config.Templates.MatrixScore, evt.Target ?? "GLOBAL:MATRIX1", evt.Color ?? "GREEN") };
        }

        if (Is(evt.Action, "clear"))
        {
            return new[] { BuildCommand(evt, _config.Templates.Clear, evt.Target, evt.Color) };
        }

        if (Is(evt.Action, "all"))
        {
            return new[] { BuildCommand(evt, _config.Templates.All, evt.Target, evt.Color ?? "WHITE") };
        }

        if (Is(evt.Effect, "flash_restore") && evt.FlashMs.HasValue)
        {
            return new[] { BuildCommand(evt, _config.Templates.Flash, evt.Target, evt.Color ?? "YELLOW", evt.FlashMs) };
        }

        if (evt.Slot.HasValue)
        {
            return new[] { BuildCommand(evt, _config.Templates.SetSlot, evt.Target, evt.Color ?? "WHITE") };
        }

        if (!string.IsNullOrWhiteSpace(evt.Target))
        {
            return new[] { BuildCommand(evt, _config.Templates.SetOutput, evt.Target, evt.Color ?? "WHITE") };
        }

        return Array.Empty<RoutedCommand>();
    }

    public IReadOnlyList<RoutedCommand> RoutePanelState(PanelState state)
    {
        var commands = new List<RoutedCommand>();
        foreach (var group in state.Outputs
            .Where(IsRoutablePanelOutput)
            .GroupBy(o => (SenderId: ResolveSender(o.Player, o.Target), o.Player)))
        {
            var items = group
                .OrderByDescending(IsBlackOutput)
                .ThenBy(IsPanelShadeOutput)
                .ThenBy(output => output.Slot ?? int.MaxValue)
                .Select(ToPanelBatchItem)
                .Where(s => s.Length > 0)
                .ToArray();
            if (items.Length > 0)
            {
                commands.Add(new RoutedCommand(
                    group.Key.SenderId,
                    $"BATCH {string.Join(';', items)}",
                    group.Key.Player,
                    null,
                    IsPanelUpdate: true,
                    Sequence: state.Sequence));
            }
        }

        return commands;
    }

    private bool IsRoutablePanelOutput(PanelOutput output)
    {
        return output.Player is not int player ||
            player <= 0 ||
            player <= Math.Max(1, _config.Hardware.PanelPlayers);
    }

    private string ToBatchItem(PanelOutput output)
    {
        if (output.Slot.HasValue)
        {
            return $"SLOT {output.Slot.Value} {NormalizeColor(output.Color)}";
        }

        return string.IsNullOrWhiteSpace(output.Target)
            ? ""
            : $"{NormalizeTarget(output.Target)} {NormalizeColor(output.Color)}";
    }

    private string ToPanelBatchItem(PanelOutput output)
    {
        if (output.Slot.HasValue)
        {
            return $"SLOTPWM {output.Slot.Value} {NormalizeColor(output.Color)}";
        }

        return ToBatchItem(output);
    }

    private RoutedCommand BuildCommand(LedEvent evt, string template, string? target, string? color, int? flashMs = null)
    {
        var senderId = ResolveSender(evt, target);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["player"] = (evt.Player ?? 0).ToString(),
            ["sender"] = senderId,
            ["senderName"] = _config.Senders.TryGetValue(senderId, out var sender) ? sender.Name : senderId,
            ["target"] = NormalizeTarget(target ?? evt.Target ?? evt.Slot?.ToString() ?? ""),
            ["targetPlayer"] = (evt.Player ?? 0).ToString(),
            ["slot"] = evt.Slot?.ToString() ?? "",
            ["color"] = NormalizeColor(color ?? evt.Color ?? "WHITE"),
            ["state"] = evt.State ?? evt.Value ?? "",
            ["value"] = evt.Value ?? "",
            ["text"] = evt.Text ?? "",
            ["durationMs"] = (flashMs ?? evt.FlashMs)?.ToString() ?? "0",
            ["payload"] = evt.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "" : evt.Payload.GetRawText(),
            ["system"] = evt.System ?? "",
            ["rom"] = evt.Rom ?? ""
        };

        return new RoutedCommand(senderId, ApplyTemplate(template, values), evt.Player, target ?? evt.Target);
    }

    private string ResolveSender(LedEvent evt, string? target)
    {
        return !string.IsNullOrWhiteSpace(evt.Sender) ? evt.Sender : ResolveSender(evt.Player, target ?? evt.Target);
    }

    private string ResolveSender(int? player, string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            var normalized = target.Trim();
            if (normalized.StartsWith("GLOBAL:", StringComparison.OrdinalIgnoreCase))
            {
                return _config.GlobalSenderId;
            }

            if (normalized.Length > 3 && normalized[0] == 'P' && char.IsDigit(normalized[1]) && normalized[2] == ':')
            {
                player = normalized[1] - '0';
            }

            var key = normalized.Split(':', 2)[0];
            if (_config.TargetRouting.TryGetValue(key, out var routed))
            {
                return routed;
            }
        }

        var p = player.GetValueOrDefault();
        if (_config.PlayerRouting.TryGetValue(p, out var senderId))
        {
            return senderId;
        }

        if (p > 0)
        {
            return $"UNMAPPED_PLAYER_{p}";
        }

        if (p <= 0 && _config.Senders.ContainsKey(_config.GlobalSenderId))
        {
            return _config.GlobalSenderId;
        }

        if (_config.Senders.ContainsKey(_config.DefaultSenderId))
        {
            return _config.DefaultSenderId;
        }

        return _config.Senders.Keys.FirstOrDefault() ?? _config.DefaultSenderId;
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result.Trim();
    }

    private static string NormalizeTarget(string value)
    {
        var target = value.Trim();
        if (target.Length > 3 && target[0] == 'P' && char.IsDigit(target[1]) && target[2] == ':')
        {
            target = target[3..];
        }

        if (target.StartsWith("GLOBAL:", StringComparison.OrdinalIgnoreCase))
        {
            target = target["GLOBAL:".Length..];
        }

        return target.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase)
            ? target["SLOT:".Length..].ToUpperInvariant()
            : target.ToUpperInvariant();
    }

    private static string NormalizeColor(string value) => string.IsNullOrWhiteSpace(value) ? "BLACK" : value.Trim().ToUpperInvariant();

    private static bool IsBlackOutput(PanelOutput output) =>
        string.IsNullOrWhiteSpace(output.Color) ||
        output.Color.Equals("BLACK", StringComparison.OrdinalIgnoreCase) ||
        output.Color.Equals("OFF", StringComparison.OrdinalIgnoreCase) ||
        output.Color.Equals("0", StringComparison.OrdinalIgnoreCase);

    private static bool IsPanelShadeOutput(PanelOutput output)
    {
        var color = NormalizeColor(output.Color);
        return color is not ("BLACK" or "WHITE" or "PINK" or "CYAN" or "YELLOW" or "BLUE" or "RED" or "GREEN");
    }

    private static bool Is(string? value, string expected) => value?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;
}

public sealed record RoutedCommand(string SenderId, string Command, int? Player, string? Target, bool IsPanelUpdate = false, long Sequence = 0);

internal static class LedEventExtensions
{
    public static LedEvent WithValue(this LedEvent evt, string value)
    {
        return new LedEvent
        {
            Type = evt.Type,
            Stream = evt.Stream,
            Player = evt.Player,
            Sender = evt.Sender,
            Action = evt.Action,
            Family = evt.Family,
            Effect = evt.Effect,
            Target = evt.Target,
            Slot = evt.Slot,
            Color = evt.Color,
            State = evt.State,
            Value = value,
            Text = evt.Text,
            System = evt.System,
            Rom = evt.Rom,
            DelayMs = evt.DelayMs,
            FlashMs = evt.FlashMs,
            Payload = evt.Payload
        };
    }
}
