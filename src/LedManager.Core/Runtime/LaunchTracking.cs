using System.Text.Json;

namespace LedManager.Core.Runtime;

/// <summary>
/// The launch identifier of APIExpose 1.9.185 (ECR-08): 32 hex characters carried by
/// ui.game.started / ui.game.ended (<c>Payload.LaunchId</c>, also
/// <c>Payload.Context.Running.LaunchId</c> on the ES hook path) and by every deck.*
/// message. The .raw forms and older APIs carry none.
/// </summary>
public static class LaunchIds
{
    public static string? Read(LedEvent evt) => Read(evt.Payload);

    public static string? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var payload = Get(root, "Payload") is { ValueKind: JsonValueKind.Object } inner ? inner : root;
        var direct = Text(Get(payload, "LaunchId"));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Trim();
        }

        var running = Get(Get(Get(payload, "Context"), "Running"), "LaunchId");
        var nested = Text(running);
        return string.IsNullOrWhiteSpace(nested) ? null : nested.Trim();
    }

    /// <summary>Short form for the log (the id is not a secret, only long).</summary>
    public static string Short(string? launchId) =>
        string.IsNullOrWhiteSpace(launchId) ? "-" : launchId.Length <= 8 ? launchId : launchId[..8];

    private static string? Text(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static JsonElement? Get(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}

public enum LaunchStartKind
{
    /// <summary>A new game: start effects and panel snapshot.</summary>
    New,

    /// <summary>The game already running announced again: nothing replayed.</summary>
    Duplicate
}

public enum LaunchEndKind
{
    /// <summary>The game ends: restore the panel, forget what belonged to it.</summary>
    End,

    /// <summary>The same end announced again (.raw then enriched form): nothing replayed.</summary>
    Duplicate
}

/// <param name="AdoptedLaunchId">Set when a start without id (the .raw form, sent first by the
/// ES hook) is now known under this id.</param>
public sealed record LaunchStart(LaunchStartKind Kind, string? LaunchId, string? AdoptedLaunchId = null);

/// <param name="LaunchId">The launch that ends (the running one when known, else the id carried).</param>
public sealed record LaunchEnd(LaunchEndKind Kind, string? LaunchId, string? Mismatch = null);

/// <summary>
/// Tells a new game from the same game announced twice (ECR-08). APIExpose sends one
/// LaunchId for a second start of the same file before its end (direct launch then ES
/// hook), and the ES hook sends <c>ui.game.started.raw</c> (no id) just before
/// <c>ui.game.started</c>. Rules:
/// a start with the id of the running game is a duplicate; a start without id, or the
/// first id of a game started without one, within <see cref="CompanionWindowMs"/> of that
/// start, is its companion; anything else is a new game (a lost end included). An end
/// closes the running game; the same end repeated within the window is a duplicate.
/// Pure state: the caller gives the clock.
/// </summary>
public sealed class LaunchTracker
{
    public const long CompanionWindowMs = 10_000;

    private bool _running;
    private string _runningId = "";
    private long _startedAtMs;
    private bool _recentlyEnded;
    private string _endedId = "";
    private long _endedAtMs;

    public bool IsRunning => _running;

    /// <summary>Id of the running game; empty when it started without one; null between games.</summary>
    public string? RunningLaunchId => _running ? _runningId : null;

    public LaunchStart OnStarted(string? launchId, long nowMs)
    {
        var id = Normalize(launchId);
        if (_running)
        {
            if (id.Length > 0 && _runningId.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return new LaunchStart(LaunchStartKind.Duplicate, _runningId);
            }

            var companion = nowMs - _startedAtMs is >= 0 and < CompanionWindowMs;
            if (companion && id.Length == 0)
            {
                return new LaunchStart(LaunchStartKind.Duplicate, NullIfEmpty(_runningId));
            }

            if (companion && _runningId.Length == 0)
            {
                _runningId = id;
                return new LaunchStart(LaunchStartKind.Duplicate, id, AdoptedLaunchId: id);
            }
        }

        _running = true;
        _runningId = id;
        _startedAtMs = nowMs;
        _recentlyEnded = false;
        return new LaunchStart(LaunchStartKind.New, NullIfEmpty(id));
    }

    public LaunchEnd OnEnded(string? launchId, long nowMs)
    {
        var id = Normalize(launchId);
        if (!_running)
        {
            var repeated = _recentlyEnded &&
                nowMs - _endedAtMs is >= 0 and < CompanionWindowMs &&
                (id.Length == 0 || _endedId.Length == 0 || _endedId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (repeated)
            {
                return new LaunchEnd(LaunchEndKind.Duplicate, NullIfEmpty(_endedId.Length > 0 ? _endedId : id));
            }

            // an end without a start seen here (daemon started during the game): still an end
            Remember(id, nowMs);
            return new LaunchEnd(LaunchEndKind.End, NullIfEmpty(id));
        }

        // The API closes the launch it has open: a different id means a start or an end
        // was missed here. The game is over all the same; the mismatch is reported.
        string? mismatch = id.Length > 0 && _runningId.Length > 0 && !_runningId.Equals(id, StringComparison.OrdinalIgnoreCase)
            ? id
            : null;
        var ended = _runningId.Length > 0 ? _runningId : id;
        _running = false;
        _runningId = "";
        Remember(ended, nowMs);
        return new LaunchEnd(LaunchEndKind.End, NullIfEmpty(ended), mismatch);
    }

    private void Remember(string id, long nowMs)
    {
        _recentlyEnded = true;
        _endedId = id;
        _endedAtMs = nowMs;
    }

    private static string Normalize(string? launchId) => launchId?.Trim() ?? "";

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
