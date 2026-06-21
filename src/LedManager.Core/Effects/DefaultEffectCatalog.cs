using System.Text.Json;
using System.Text.RegularExpressions;
using LedManager.Core.Configuration;
using LedManager.Core.Runtime;

namespace LedManager.Core.Effects;

public sealed class DefaultEffectCatalog
{
    private static readonly string[] RainbowColors = { "RED", "ORANGE", "YELLOW", "GREEN", "CYAN", "BLUE", "PURPLE" };
    private static readonly Regex AddressableTarget = new(@"^(STRIP|CIRCLE|MATRIX|JOY)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int MaxSlotAnimationStepMs = 100;
    private const int MaxSparkleStepMs = 35;

    private readonly Dictionary<string, List<EffectStep>> _actionRules;
    private readonly Dictionary<string, EffectStep> _genericRules;
    private readonly HardwareConfig _hardware;

    // Per-rule "last fired" timestamps so high-frequency actions (e.g. a level timer
    // that changes almost every frame) can't keep re-triggering their full effect and
    // flooding the command queue.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Key, int StepIndex), long> _lastFiredTicks = new();

    private DefaultEffectCatalog(
        Dictionary<string, List<EffectStep>> actionRules,
        Dictionary<string, EffectStep> genericRules,
        HardwareConfig hardware)
    {
        _actionRules = actionRules;
        _genericRules = genericRules;
        _hardware = hardware;
    }

    public static DefaultEffectCatalog Empty { get; } = new(
        new Dictionary<string, List<EffectStep>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, EffectStep>(StringComparer.OrdinalIgnoreCase),
        HardwareConfig.AllEnabled);

    public static DefaultEffectCatalog Load(string path) => Load(path, HardwareConfig.AllEnabled);

    public static DefaultEffectCatalog Load(string path, HardwareConfig hardware)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var actionRules = new Dictionary<string, List<EffectStep>>(StringComparer.OrdinalIgnoreCase);
        var genericRules = new Dictionary<string, EffectStep>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("actionRules", out var actions) && actions.ValueKind == JsonValueKind.Object)
        {
            foreach (var rule in actions.EnumerateObject())
            {
                if (!rule.Value.TryGetProperty("then", out var then) || then.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                actionRules[rule.Name] = then.EnumerateArray().Select(ParseStep).ToList();
            }
        }

        if (root.TryGetProperty("genericRules", out var generic) && generic.ValueKind == JsonValueKind.Object)
        {
            foreach (var rule in generic.EnumerateObject())
            {
                genericRules[rule.Name] = ParseStep(rule.Value);
            }
        }

        return new DefaultEffectCatalog(actionRules, genericRules, hardware);
    }

    public IReadOnlyList<LedEvent> Resolve(LedEvent source)
    {
        if (!source.Type.Equals("mem.action", StringComparison.OrdinalIgnoreCase) &&
            !source.Stream.Equals("mem", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<LedEvent>();
        }

        var action = source.Action ?? "";
        if (action.Length > 0 && TryGetActionSteps(action, includeFallback: false, out var ruleKey, out var steps))
        {
            var results = new List<LedEvent>();
            for (var i = 0; i < steps.Count; i++)
            {
                if (IsThrottled(ruleKey, i, steps[i].ThrottleMs))
                {
                    continue;
                }

                results.AddRange(ExpandStep(source, steps[i]));
            }

            return results;
        }

        var family = source.Family ?? "";
        if (family.Length > 0 && _genericRules.TryGetValue(family, out var generic))
        {
            if (IsThrottled(family, 0, generic.ThrottleMs))
            {
                return Array.Empty<LedEvent>();
            }

            return ExpandStep(source, generic).ToArray();
        }

        if (action.Length > 0 && TryGetActionSteps(action, includeFallback: true, out var fallbackRuleKey, out var fallbackSteps))
        {
            var results = new List<LedEvent>();
            for (var i = 0; i < fallbackSteps.Count; i++)
            {
                if (IsThrottled(fallbackRuleKey, i, fallbackSteps[i].ThrottleMs))
                {
                    continue;
                }

                results.AddRange(ExpandStep(source, fallbackSteps[i]));
            }

            return results;
        }

        return Array.Empty<LedEvent>();
    }

    private bool TryGetActionSteps(string action, bool includeFallback, out string ruleKey, out List<EffectStep> steps)
    {
        if (_actionRules.TryGetValue(action, out steps!))
        {
            ruleKey = action;
            return true;
        }

        KeyValuePair<string, List<EffectStep>>? fallback = null;
        KeyValuePair<string, List<EffectStep>>? bestWildcard = null;
        var bestPrefixLength = -1;

        foreach (var rule in _actionRules)
        {
            if (!rule.Key.EndsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            if (rule.Key.Length == 1)
            {
                fallback = rule;
                continue;
            }

            var prefix = rule.Key[..^1];
            if (action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > bestPrefixLength)
            {
                bestWildcard = rule;
                bestPrefixLength = prefix.Length;
            }
        }

        if (bestWildcard.HasValue)
        {
            ruleKey = bestWildcard.Value.Key;
            steps = bestWildcard.Value.Value;
            return true;
        }

        if (includeFallback && fallback.HasValue)
        {
            ruleKey = fallback.Value.Key;
            steps = fallback.Value.Value;
            return true;
        }

        ruleKey = action;
        steps = null!;
        return false;
    }

    private bool IsThrottled(string key, int stepIndex, int? throttleMs)
    {
        if (throttleMs is null or <= 0)
        {
            return false;
        }

        var now = Environment.TickCount64;
        var cacheKey = (key, stepIndex);
        if (_lastFiredTicks.TryGetValue(cacheKey, out var last) && now - last < throttleMs.Value)
        {
            return true;
        }

        _lastFiredTicks[cacheKey] = now;
        return false;
    }

    private IEnumerable<LedEvent> ExpandStep(LedEvent source, EffectStep step)
    {
        var effect = step.Effect;
        var color = ResolveColor(source, step);
        var value = Substitute(step.Value ?? source.Value ?? "", source);
        var targets = ResolveTargets(source, step);

        if (Is(effect, "matrix_score"))
        {
            var scoreTarget = step.Target ?? "MATRIX1";
            if (!IsTargetAvailable(scoreTarget, _hardware))
            {
                yield break;
            }

            yield return Clone(source, action: "matrix_score", effect: effect, target: scoreTarget, color: color, value: value);
            yield break;
        }

        foreach (var target in targets)
        {
            foreach (var evt in ExpandTargetEffect(source, step, effect, target, color, value))
            {
                yield return evt;
            }
        }
    }

    private IReadOnlyList<string> ResolveTargets(LedEvent source, EffectStep step)
    {
        var rawTargets = step.Targets.Count > 0 ? step.Targets : new[] { step.Target ?? "ALL_BUTTONS" };
        var targets = ResolveRawTargets(source, rawTargets).ToList();
        if (targets.Count == 0 && step.FallbackTargets.Count > 0)
        {
            targets.AddRange(ResolveRawTargets(source, step.FallbackTargets));
        }

        return targets;
    }

    private IEnumerable<string> ResolveRawTargets(LedEvent source, IEnumerable<string> rawTargets)
    {
        foreach (var rawTarget in rawTargets)
        {
            var target = Substitute(rawTarget, source);
            if (IsCoinGainOnAllButtons(source, target))
            {
                target = "RANDOM_BUTTON";
            }

            foreach (var expanded in ExpandSpecialTarget(target))
            {
                if (!IsUsableTarget(expanded) || !IsTargetAvailable(expanded, _hardware))
                {
                    // Pas de materiel branche pour cette cible (STRIP/CIRCLE/MATRIX/JOY) :
                    // inutile de generer/pousser des commandes que personne ne recevra.
                    continue;
                }

                yield return expanded;
            }
        }
    }

    private static IEnumerable<string> ExpandSpecialTarget(string target)
    {
        if (target.Equals("RANDOM_BUTTON", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("RANDOM_SLOT", StringComparison.OrdinalIgnoreCase))
        {
            yield return "SLOT:" + Random.Shared.Next(1, 9);
            yield break;
        }

        if (target.Equals("RANDOM_COLUMN", StringComparison.OrdinalIgnoreCase))
        {
            var columns = PanelPatternGroups("left_to_right_columns");
            foreach (var slot in columns[Random.Shared.Next(columns.Length)])
            {
                yield return "SLOT:" + slot;
            }
            yield break;
        }

        yield return target;
    }

    private static bool IsUsableTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (!target.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(target["SLOT:".Length..], out var slot) && slot is >= 1 and <= 8;
    }

    private static bool IsTargetAvailable(string target, HardwareConfig hardware)
    {
        var normalized = target.Trim();
        if (normalized.StartsWith("GLOBAL:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["GLOBAL:".Length..];
        }
        else if (normalized.Length > 3 && (normalized[0] == 'P' || normalized[0] == 'p') && char.IsDigit(normalized[1]) && normalized[2] == ':')
        {
            normalized = normalized[3..];
        }

        var match = AddressableTarget.Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var index))
        {
            return true;
        }

        var declared = match.Groups[1].Value.ToUpperInvariant() switch
        {
            "STRIP" => hardware.Strips,
            "CIRCLE" => hardware.Circles,
            "MATRIX" => hardware.Matrices,
            "JOY" => hardware.Joysticks,
            _ => 0
        };

        return index <= declared;
    }

    private static bool IsCoinGainOnAllButtons(LedEvent source, string target)
    {
        return source.Action?.Equals("COIN_GAIN", StringComparison.OrdinalIgnoreCase) == true &&
               (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("ALL", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<LedEvent> ExpandTargetEffect(
        LedEvent source,
        EffectStep step,
        string effect,
        string target,
        string color,
        string value)
    {
        if (Is(effect, "flash_restore"))
        {
            // Une seule commande : le firmware allume "color" puis restaure tout seul
            // la couleur precedente apres durationMs, sans aller-retour PC<->Pico.
            var flashMs = step.OnMs ?? step.DurationMs ?? 80;
            yield return FlashCommand(source, effect, target, color, value, flashMs);
            yield break;
        }

        if (Is(effect, "flash"))
        {
            var times = Math.Max(1, step.Times ?? 1);
            var onMs = step.OnMs ?? step.DurationMs ?? 120;
            var offMs = step.OffMs ?? 80;
            if (Is(source.Action, "COIN_GAIN"))
            {
                // A single quick flash per ring is enough; rings are often collected in
                // rapid succession and longer/repeated flashes pile up visibly.
                times = 1;
                onMs = 80;
                offMs = 60;
            }

            for (var i = 0; i < times; i++)
            {
                foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: onMs))
                {
                    yield return evt;
                }

                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: offMs))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "blink") || Is(effect, "timer_warning"))
        {
            var interval = step.IntervalMs ?? 150;
            var duration = step.DurationMs ?? 900;
            var count = Math.Max(1, duration / Math.Max(1, interval * 2));
            for (var i = 0; i < count; i++)
            {
                foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: interval))
                {
                    yield return evt;
                }

                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: interval))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "pulse"))
        {
            foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: step.DurationMs ?? 250))
            {
                yield return evt;
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "flow_state"))
        {
            var duration = step.DurationMs ?? 150;
            foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: duration))
            {
                yield return evt;
            }

            foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
            {
                yield return evt;
            }
            yield break;
        }

        if (Is(effect, "strobe"))
        {
            var colors = step.Colors.Count > 0
                ? step.Colors.Select(c => Substitute(c, source)).ToArray()
                : new[] { color, "BLACK" };
            var interval = step.IntervalMs ?? 100;
            var duration = step.DurationMs ?? 600;
            var cycles = Math.Max(1, duration / Math.Max(1, interval * colors.Length));
            for (var i = 0; i < cycles; i++)
            {
                foreach (var stepColor in colors)
                {
                    foreach (var evt in TargetCommands(source, effect, target, stepColor, value, delayMs: interval))
                    {
                        yield return evt;
                    }
                }
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "celebrate"))
        {
            var colors = step.Colors.Count > 0
                ? step.Colors.Select(c => Substitute(c, source)).ToArray()
                : new[] { color };
            var stepMs = Math.Max(1, (step.DurationMs ?? 1500) / colors.Length);
            foreach (var stepColor in colors)
            {
                foreach (var evt in TargetCommands(source, effect, target, stepColor, value, delayMs: stepMs))
                {
                    yield return evt;
                }
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "rainbow"))
        {
            var stepMs = Math.Max(1, (step.DurationMs ?? 1400) / RainbowColors.Length);
            foreach (var stepColor in RainbowColors)
            {
                foreach (var evt in TargetCommands(source, effect, target, stepColor, value, delayMs: stepMs))
                {
                    yield return evt;
                }
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "sparkle"))
        {
            var duration = step.DurationMs ?? 800;
            if (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                const int sparkleCount = 4;
                var stepMs = Math.Min(MaxSparkleStepMs, Math.Max(1, duration / sparkleCount / 2));
                foreach (var slot in Enumerable.Range(1, 8).OrderBy(_ => Random.Shared.Next()).Take(sparkleCount))
                {
                    foreach (var evt in TargetCommands(source, effect, "SLOT:" + slot, color, value, delayMs: stepMs))
                    {
                        yield return evt;
                    }

                    foreach (var evt in TargetCommands(source, effect, "SLOT:" + slot, "BLACK", value, delayMs: stepMs))
                    {
                        yield return evt;
                    }
                }

                yield break;
            }

            foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: duration))
            {
                yield return evt;
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "sweep") || Is(effect, "chase"))
        {
            var duration = step.DurationMs ?? 700;
            if (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var passes = Is(effect, "chase") ? 2 : 1;
                var groups = PanelPatternGroups(step.Pattern);
                var stepMs = Math.Min(MaxSlotAnimationStepMs, Math.Max(1, duration / (groups.Length * passes)));
                for (var pass = 0; pass < passes; pass++)
                {
                    foreach (var group in groups)
                    {
                        foreach (var evt in PanelGroupCommands(source, effect, group, color, value, stepMs))
                        {
                            yield return evt;
                        }
                    }
                }

                if (step.Restore)
                {
                    foreach (var evt in TargetCommands(source, effect, "ALL", "BLACK", value, delayMs: 0))
                    {
                        yield return evt;
                    }
                }

                yield break;
            }

            foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: duration))
            {
                yield return evt;
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "column_scan") || Is(effect, "column_wipe") || Is(effect, "column_bounce"))
        {
            if (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var groups = PanelPatternGroups(step.Pattern ?? "left_to_right_columns");
                if (Is(effect, "column_bounce"))
                {
                    groups = BounceGroups(groups);
                }

                var stepMs = ResolveAnimationStepMs(step, groups.Length);
                foreach (var evt in PanelScanCommands(source, effect, groups, color, value, stepMs, step.Restore))
                {
                    yield return evt;
                }

                yield break;
            }

            foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: step.DurationMs ?? 120))
            {
                yield return evt;
            }

            if (step.Restore)
            {
                foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
                {
                    yield return evt;
                }
            }

            yield break;
        }

        if (Is(effect, "column_flash_scan") || Is(effect, "column_flash_bounce"))
        {
            if (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var groups = PanelPatternGroups(step.Pattern ?? "left_to_right_columns");
                if (Is(effect, "column_flash_bounce"))
                {
                    groups = BounceGroups(groups);
                }

                var colors = step.Colors.Count > 0
                    ? step.Colors.Select(c => Substitute(c, source)).ToArray()
                    : new[] { color };
                var passes = Math.Max(1, step.Times ?? 1);
                var stepMs = ResolveAnimationStepMs(step, groups.Length * passes);
                var flashMs = step.OnMs ?? step.DurationMs ?? Math.Max(80, stepMs + 20);

                for (var pass = 0; pass < passes; pass++)
                {
                    for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                    {
                        var frameColor = colors[(pass + groupIndex) % colors.Length];
                        foreach (var evt in PanelFlashGroupCommands(source, effect, groups[groupIndex], frameColor, value, flashMs, stepMs))
                        {
                            yield return evt;
                        }
                    }
                }

                yield break;
            }

            var targetFlashMs = step.OnMs ?? step.DurationMs ?? 90;
            yield return FlashCommand(source, effect, target, color, value, targetFlashMs, delayMs: step.IntervalMs ?? 0);
            yield break;
        }

        if (Is(effect, "column_prism") || Is(effect, "column_bars"))
        {
            if (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var groups = PanelPatternGroups(step.Pattern ?? "left_to_right_columns");
                var colors = step.Colors.Count > 0
                    ? step.Colors.Select(c => Substitute(c, source)).ToArray()
                    : RainbowColors;
                var frames = Math.Max(1, step.Times ?? groups.Length);
                var stepMs = ResolveAnimationStepMs(step, frames);

                for (var frame = 0; frame < frames; frame++)
                {
                    for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                    {
                        var frameColor = colors[(frame + groupIndex) % colors.Length];
                        var delay = groupIndex == groups.Length - 1 ? stepMs : 0;
                        foreach (var evt in PanelGroupCommands(source, effect, groups[groupIndex], frameColor, value, delay))
                        {
                            yield return evt;
                        }
                    }
                }

                if (step.Restore)
                {
                    foreach (var evt in PanelRestoreCommands(source, effect, groups, value))
                    {
                        yield return evt;
                    }
                }

                yield break;
            }

            foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: step.DurationMs ?? 120))
            {
                yield return evt;
            }

            yield break;
        }

        if (Is(effect, "ambient"))
        {
            var ambientColor = (step.Palette ?? "").ToUpperInvariant() switch
            {
                "ATTRACT" => "PURPLE",
                "WEATHER" => "BLUE",
                _ => "CYAN"
            };

            foreach (var evt in TargetCommands(source, effect, target, ambientColor, value, delayMs: 0))
            {
                yield return evt;
            }
            yield break;
        }

        if (Is(effect, "dim") || Is(effect, "restore"))
        {
            foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
            {
                yield return evt;
            }
            yield break;
        }

        foreach (var evt in TargetCommands(source, effect, target, color, value, delayMs: step.DurationMs ?? 0))
        {
            yield return evt;
        }

        if (step.Restore && (step.DurationMs ?? 0) > 0)
        {
            foreach (var evt in TargetCommands(source, effect, target, "BLACK", value, delayMs: 0))
            {
                yield return evt;
            }
        }
    }

    private static IEnumerable<LedEvent> PanelGroupCommands(
        LedEvent source,
        string effect,
        IReadOnlyList<int> slots,
        string color,
        string value,
        int stepMs)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var delay = i == slots.Count - 1 ? stepMs : 0;
            foreach (var evt in TargetCommands(source, effect, "SLOT:" + slots[i], color, value, delayMs: delay))
            {
                yield return evt;
            }
        }
    }

    private static IEnumerable<LedEvent> PanelFlashGroupCommands(
        LedEvent source,
        string effect,
        IReadOnlyList<int> slots,
        string color,
        string value,
        int flashMs,
        int stepMs)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var delay = i == slots.Count - 1 ? stepMs : 0;
            yield return FlashCommand(source, effect, "SLOT:" + slots[i], color, value, flashMs, delay);
        }
    }

    private static IEnumerable<LedEvent> PanelScanCommands(
        LedEvent source,
        string effect,
        IReadOnlyList<IReadOnlyList<int>> groups,
        string color,
        string value,
        int stepMs,
        bool restore)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            foreach (var evt in PanelGroupCommands(source, effect, groups[i], color, value, stepMs))
            {
                yield return evt;
            }

            if (i < groups.Count - 1 || restore)
            {
                foreach (var evt in PanelGroupCommands(source, effect, groups[i], "BLACK", value, 0))
                {
                    yield return evt;
                }
            }
        }
    }

    private static IEnumerable<LedEvent> PanelRestoreCommands(
        LedEvent source,
        string effect,
        IReadOnlyList<IReadOnlyList<int>> groups,
        string value)
    {
        foreach (var group in groups)
        {
            foreach (var evt in PanelGroupCommands(source, effect, group, "BLACK", value, 0))
            {
                yield return evt;
            }
        }
    }

    private static int ResolveAnimationStepMs(EffectStep step, int steps)
    {
        if (step.IntervalMs is > 0)
        {
            return step.IntervalMs.Value;
        }

        return Math.Min(MaxSlotAnimationStepMs, Math.Max(1, (step.DurationMs ?? 700) / Math.Max(1, steps)));
    }

    private static int[][] BounceGroups(int[][] groups)
    {
        if (groups.Length <= 1)
        {
            return groups;
        }

        return groups
            .Concat(groups.Skip(1).SkipLast(1).Reverse())
            .Select(group => group.ToArray())
            .ToArray();
    }

    private static int[][] PanelPatternGroups(string? pattern)
    {
        return (pattern ?? "").Trim().ToLowerInvariant() switch
        {
            "left_to_right_columns" => new[] { new[] { 4, 1 }, new[] { 3, 2 }, new[] { 5, 6 }, new[] { 7, 8 } },
            "right_to_left_columns" => new[] { new[] { 7, 8 }, new[] { 5, 6 }, new[] { 3, 2 }, new[] { 4, 1 } },
            "bottom_to_top_bar" => new[] { new[] { 1, 2, 6, 8 }, new[] { 4, 3, 5, 7 } },
            "top_to_bottom_bar" => new[] { new[] { 4, 3, 5, 7 }, new[] { 1, 2, 6, 8 } },
            "top_row_left_to_right" => new[] { new[] { 4 }, new[] { 3 }, new[] { 5 }, new[] { 7 } },
            "bottom_row_left_to_right" => new[] { new[] { 1 }, new[] { 2 }, new[] { 6 }, new[] { 8 } },
            _ => new[] { new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 }, new[] { 5 }, new[] { 6 }, new[] { 7 }, new[] { 8 } }
        };
    }

    private static LedEvent FlashCommand(LedEvent source, string effect, string target, string color, string value, int flashMs, int delayMs = 0)
    {
        return Clone(source, action: "flash", effect: effect, target: target, color: color, value: value, delayMs: delayMs, flashMs: flashMs);
    }

    private static IEnumerable<LedEvent> TargetCommands(LedEvent source, string effect, string target, string color, string value, int delayMs)
    {
        if (target.Equals("ALL_BUTTONS", StringComparison.OrdinalIgnoreCase))
        {
            yield return Clone(source, action: "all", effect: effect, target: "ALL", color: color, value: value, delayMs: delayMs);
            yield break;
        }

        if (target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            yield return Clone(source, action: "all", effect: effect, target: "ALL", color: color, value: value, delayMs: delayMs);
            yield break;
        }

        yield return Clone(source, action: "set", effect: effect, target: target, color: color, value: value, delayMs: delayMs);
    }

    private static EffectStep ParseStep(JsonElement json)
    {
        var targets = new List<string>();
        if (json.TryGetProperty("targets", out var targetsJson) && targetsJson.ValueKind == JsonValueKind.Array)
        {
            targets.AddRange(targetsJson.EnumerateArray().Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v))!);
        }

        var fallbackTargets = new List<string>();
        if (json.TryGetProperty("fallbackTargets", out var fallbackTargetsJson) && fallbackTargetsJson.ValueKind == JsonValueKind.Array)
        {
            fallbackTargets.AddRange(fallbackTargetsJson.EnumerateArray().Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v))!);
        }

        var colors = new List<string>();
        if (json.TryGetProperty("colors", out var colorsJson) && colorsJson.ValueKind == JsonValueKind.Array)
        {
            colors.AddRange(colorsJson.EnumerateArray().Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v))!);
        }

        return new EffectStep
        {
            Effect = ReadString(json, "effect") ?? "set",
            Target = ReadString(json, "target"),
            Targets = targets,
            FallbackTargets = fallbackTargets,
            Color = ReadString(json, "color"),
            Colors = colors,
            PositiveColor = ReadString(json, "positiveColor"),
            NegativeColor = ReadString(json, "negativeColor"),
            Palette = ReadString(json, "palette"),
            Pattern = ReadString(json, "pattern"),
            Value = ReadString(json, "value"),
            Times = ReadInt(json, "times"),
            OnMs = ReadInt(json, "onMs"),
            OffMs = ReadInt(json, "offMs"),
            IntervalMs = ReadInt(json, "intervalMs"),
            DurationMs = ReadInt(json, "durationMs"),
            Restore = ReadBool(json, "restore"),
            ThrottleMs = ReadInt(json, "throttleMs")
        };
    }

    private static string ResolveColor(LedEvent source, EffectStep step)
    {
        if (!string.IsNullOrWhiteSpace(source.Color))
        {
            return Substitute(source.Color, source);
        }

        if (!string.IsNullOrWhiteSpace(step.Color))
        {
            return Substitute(step.Color, source);
        }

        var action = source.Action ?? "";
        if (action.Contains("HIT", StringComparison.OrdinalIgnoreCase) ||
            action.Contains("LOSE", StringComparison.OrdinalIgnoreCase) ||
            action.Contains("LOW_", StringComparison.OrdinalIgnoreCase) ||
            action.Contains("DAMAGE", StringComparison.OrdinalIgnoreCase))
        {
            return step.NegativeColor ?? "RED";
        }

        return step.PositiveColor ?? "WHITE";
    }

    private static LedEvent Clone(LedEvent source, string action, string effect, string? target, string color, string value, int delayMs = 0, int? flashMs = null)
    {
        return new LedEvent
        {
            Type = source.Type,
            Stream = source.Stream,
            Player = source.Player,
            Sender = source.Sender,
            Action = action,
            Family = source.Family,
            Effect = effect,
            Target = target,
            Slot = ParseSlot(target) ?? source.Slot,
            Color = color,
            State = source.State,
            Value = value,
            Text = source.Text,
            System = source.System,
            Rom = source.Rom,
            DelayMs = delayMs,
            FlashMs = flashMs,
            Payload = source.Payload
        };
    }

    private static int? ParseSlot(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var value = target.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase) ? target["SLOT:".Length..] : target;
        return int.TryParse(value, out var slot) ? slot : null;
    }

    private static string Substitute(string value, LedEvent source)
    {
        return value
            .Replace("${event.player}", source.Player?.ToString() ?? "0", StringComparison.OrdinalIgnoreCase)
            .Replace("${event.target}", source.Target ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("${event.slot}", source.Slot?.ToString() ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("${event.value}", source.Value ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("${event.action}", source.Action ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("${event.color}", source.Color ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("${context.system}", source.System ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("${context.rom}", source.Rom ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement json, string property)
    {
        return json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement json, string property)
    {
        return json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static bool ReadBool(JsonElement json, string property)
    {
        return json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static bool Is(string? value, string expected)
    {
        return value?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;
    }
}

internal sealed class EffectStep
{
    public string Effect { get; init; } = "set";
    public string? Target { get; init; }
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FallbackTargets { get; init; } = Array.Empty<string>();
    public string? Color { get; init; }
    public IReadOnlyList<string> Colors { get; init; } = Array.Empty<string>();
    public string? PositiveColor { get; init; }
    public string? NegativeColor { get; init; }
    public string? Palette { get; init; }
    public string? Pattern { get; init; }
    public string? Value { get; init; }
    public int? Times { get; init; }
    public int? OnMs { get; init; }
    public int? OffMs { get; init; }
    public int? IntervalMs { get; init; }
    public int? DurationMs { get; init; }
    public bool Restore { get; init; }
    public int? ThrottleMs { get; init; }
}
