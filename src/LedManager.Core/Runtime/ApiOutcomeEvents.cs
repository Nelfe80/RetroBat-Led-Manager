using System.Text.Json;

namespace LedManager.Core.Runtime;

/// <summary>
/// What a /ws/scoring or /ws/multiplayer message means for the panel. A certification
/// verdict and the start or end of a MULTIPLAYER match become a named moment
/// (<c>SCORING_PUBLISHED</c>, <c>MULTIPLAYER_MATCH_STARTED</c>...) that goes through the
/// effect catalog like any .MEM action, so its look lives in default.mem.effects.json
/// and not in code. Everything else on those streams is state, only logged.
/// </summary>
public static class ApiOutcomeEvents
{
    public const string ScoringPublished = "SCORING_PUBLISHED";
    public const string ScoringHeld = "SCORING_HELD";
    public const string ScoringQuarantined = "SCORING_QUARANTINED";
    public const string ScoringRejected = "SCORING_REJECTED";
    public const string MatchStarted = "MULTIPLAYER_MATCH_STARTED";
    public const string MatchEnded = "MULTIPLAYER_MATCH_ENDED";

    public static IReadOnlyList<string> Actions { get; } = new[]
    {
        ScoringPublished, ScoringHeld, ScoringQuarantined, ScoringRejected, MatchStarted, MatchEnded
    };

    /// <summary>A message of the scoring or multiplayer streams (also when read from a replayed file).</summary>
    public static bool IsOutcomeEvent(LedEvent evt)
    {
        var type = evt.Type ?? "";
        return type.StartsWith("scoring.", StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith("multiplayer.", StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith("netplay.", StringComparison.OrdinalIgnoreCase);
    }

    public static ApiOutcome Read(LedEvent evt)
    {
        var type = (evt.Type ?? "").Trim();
        var payload = PayloadOf(evt.Payload);

        if (type.Equals("scoring.verdict", StringComparison.OrdinalIgnoreCase))
        {
            var status = (ReadString(payload, "Status") ?? "").Trim().ToLowerInvariant();
            var action = status switch
            {
                "published" => ScoringPublished,
                "held" => ScoringHeld,
                "quarantined" => ScoringQuarantined,
                "rejected" => ScoringRejected,
                _ => null
            };
            var game = $"{ReadString(payload, "SystemId") ?? "-"}/{ReadString(payload, "RomGroup") ?? "-"}";
            var summary = $"verdict status={(status.Length > 0 ? status : "-")} game={game} ruleset={ReadString(payload, "Ruleset") ?? "-"} " +
                $"score={ReadLong(payload, "Score")?.ToString() ?? "-"} rank={ReadLong(payload, "Rank")?.ToString() ?? "-"}";
            return new ApiOutcome(type, action, summary, ReadString(payload, "SystemId"), ReadString(payload, "RomGroup"));
        }

        if (type.Equals("multiplayer.match.started", StringComparison.OrdinalIgnoreCase))
        {
            var summary = $"match started room={ReadString(payload, "Room") ?? "-"} guests={ReadLong(payload, "Invites")?.ToString() ?? "-"}";
            return new ApiOutcome(type, MatchStarted, summary, null, null);
        }

        if (type.Equals("multiplayer.match.ended", StringComparison.OrdinalIgnoreCase))
        {
            var summary = $"match ended room={ReadString(payload, "Room") ?? "-"} outcome={ReadString(payload, "Issue") ?? "-"} " +
                $"coop={ReadString(payload, "Coop") ?? "-"} closeInS={ReadLong(payload, "FermetureS")?.ToString() ?? "-"}";
            return new ApiOutcome(type, MatchEnded, summary, null, null);
        }

        if (type.Equals("multiplayer.state", StringComparison.OrdinalIgnoreCase))
        {
            var rooms = TryGetProperty(payload, "rooms", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.GetArrayLength().ToString()
                : "-";
            return new ApiOutcome(type, null, $"state rooms={rooms}", null, null);
        }

        return new ApiOutcome(type, null, type, null, null);
    }

    /// <summary>
    /// The moment handed to the effect catalog: a mem.action on the stream it came from,
    /// for the default player (a verdict or a match concerns the cabinet, not one player).
    /// </summary>
    public static LedEvent ToEffectEvent(LedEvent source, ApiOutcome outcome, int defaultPlayer)
    {
        return new LedEvent
        {
            Type = "mem.action",
            Stream = source.Stream,
            Player = defaultPlayer,
            Action = outcome.Action,
            System = outcome.System ?? source.System,
            Rom = outcome.Rom ?? source.Rom,
            Payload = source.Payload
        };
    }

    private static JsonElement PayloadOf(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            TryGetProperty(root, "Payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object)
        {
            return payload;
        }

        return root;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

/// <summary>A scoring/multiplayer message read: the catalog action to play (null = state only) and a log line.</summary>
public sealed record ApiOutcome(string Type, string? Action, string Summary, string? System, string? Rom);
