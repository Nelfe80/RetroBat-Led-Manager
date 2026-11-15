namespace LedManager.Core.Runtime;

/// <summary>
/// The generic "Instrument" layer of the panel: one signal per role, one behaviour.
/// A resource role (bombs, ammo, lives...) is read from the standard .SMEM id
/// <c>players.&lt;n&gt;.&lt;role&gt;</c> (and the older <c>players.&lt;n&gt;.resources.&lt;role&gt;</c>),
/// for a counter or a gauge. Nothing here is specific to a genre or a game.
/// </summary>
public static class ResourceRoles
{
    /// <summary>
    /// The role and player of a resource signal, or false when the id is not
    /// <c>players.N.role</c> / <c>players.N.resources.role</c> with a tracked role
    /// (so <c>players.1.bombs.used</c> is not the bomb stock).
    /// </summary>
    public static bool TryParse(SemanticSignal signal, IEnumerable<string> roles, out string role, out int player)
    {
        role = "";
        player = 0;
        if (!signal.Kind.Equals("counter", StringComparison.OrdinalIgnoreCase) &&
            !signal.Kind.Equals("gauge", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (signal.RoleGiven)
        {
            // the API's role is exact: null means "not a resource", never a guess from the id
            var given = signal.Role is null
                ? null
                : roles.FirstOrDefault(r => r.Equals(signal.Role, StringComparison.OrdinalIgnoreCase));
            var givenPlayer = signal.Player ?? PlayerFromId(signal.Id);
            if (given is null || givenPlayer is not >= 1)
            {
                return false;
            }

            role = given.ToLowerInvariant();
            player = givenPlayer.Value;
            return true;
        }

        var parts = signal.Id.Split('.');
        string candidate;
        if (parts.Length == 3 && parts[0].Equals("players", StringComparison.OrdinalIgnoreCase))
        {
            candidate = parts[2];
        }
        else if (parts.Length == 4 && parts[0].Equals("players", StringComparison.OrdinalIgnoreCase) &&
                 parts[2].Equals("resources", StringComparison.OrdinalIgnoreCase))
        {
            candidate = parts[3];
        }
        else
        {
            return false;
        }

        if (!int.TryParse(parts[1], out player) || player < 1)
        {
            return false;
        }

        var match = roles.FirstOrDefault(r => r.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        role = match.ToLowerInvariant();
        return true;
    }

    private static int? PlayerFromId(string id)
    {
        var parts = id.Split('.');
        return parts.Length >= 2 && parts[0].Equals("players", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(parts[1], out var n) ? n : null;
    }
}

/// <summary>A resource of a player crossed zero: <see cref="Empty"/> = it just ran out, false = it came back.</summary>
public sealed record ResourceAlert(string Role, int Player, string SignalId, bool Empty, long? Value);

/// <summary>
/// Which resources are empty right now. Raised when an observed, valid value reaches
/// zero; cleared when a valid value goes back above zero. An unknown, unavailable or
/// out-of-bounds value changes nothing, and the .SMEM <c>Initial</c> never raises one:
/// only what the game itself said.
/// </summary>
public sealed class ResourceAlertTracker
{
    private readonly HashSet<(string Role, int Player)> _empty = new();

    public IReadOnlyCollection<(string Role, int Player)> Empty => _empty;

    public IReadOnlyList<ResourceAlert> Observe(IEnumerable<SignalValueChange> changes, IEnumerable<string> roles)
    {
        var roleList = roles as IReadOnlyCollection<string> ?? roles.ToArray();
        var alerts = new List<ResourceAlert>();
        foreach (var change in changes)
        {
            if (!ResourceRoles.TryParse(change.Signal, roleList, out var role, out var player))
            {
                continue;
            }

            var current = change.Current;
            if (!current.Observed || !current.Valid || current.Value is not long value)
            {
                continue;
            }

            var key = (role, player);
            if (value <= 0)
            {
                if (_empty.Add(key))
                {
                    alerts.Add(new ResourceAlert(role, player, change.Signal.Id, true, value));
                }
            }
            else if (_empty.Remove(key))
            {
                alerts.Add(new ResourceAlert(role, player, change.Signal.Id, false, value));
            }
        }

        return alerts;
    }

    public void Clear() => _empty.Clear();
}

/// <summary>
/// Finds the panel buttons whose function matches a resource role, from the words the
/// configuration gives for that role ("bomb", "grenade"...). Compared on letters and
/// digits only, case-insensitive, against the dynpanel <c>function</c>, then the name
/// and id of the control. A role without words (lives, by default) has no button.
/// </summary>
public static class ResourceButtonMatcher
{
    public static IReadOnlyList<PanelOutput> Find(PanelState? panel, int player, IReadOnlyList<string> words)
    {
        if (panel is null || words.Count == 0)
        {
            return Array.Empty<PanelOutput>();
        }

        var normalizedWords = words.Select(Normalize).Where(w => w.Length > 0).ToArray();
        if (normalizedWords.Length == 0)
        {
            return Array.Empty<PanelOutput>();
        }

        // the dedicated panel file (outputs enriched from the dynpanel) wins when it names a button
        var fromOutputs = FindInOutputs(panel.Outputs, player, normalizedWords);
        if (fromOutputs.Count > 0)
        {
            return fromOutputs;
        }

        // otherwise the functions published by panel.state (ControlMap.Inputs)
        return panel.ControlInputs
            .Where(input => input.Slot is >= 1 and <= 8)
            .Where(input => input.Player == player || (input.Player is null or <= 0 && player == 1))
            .Where(input => Normalize(input.Function) is { Length: > 0 } text &&
                            normalizedWords.Any(word => text.Contains(word, StringComparison.Ordinal)))
            .GroupBy(input => (input.Player, input.Slot))
            .Select(group => group.First())
            .OrderBy(input => input.Slot)
            .Select(input => new PanelOutput
            {
                Player = input.Player,
                Slot = input.Slot,
                Target = $"SLOT:{input.Slot}",
                Color = string.IsNullOrWhiteSpace(input.Color) ? "BLACK" : input.Color!,
                Function = input.Function
            })
            .ToArray();
    }

    private static IReadOnlyList<PanelOutput> FindInOutputs(IReadOnlyList<PanelOutput> source, int player, string[] normalizedWords)
    {
        return source
            .Where(output => output.Slot is >= 1 and <= 8)
            .Where(output => output.Player == player || (output.Player is null or <= 0 && player == 1))
            .Where(output => new[] { output.Function, output.Name, output.Id }
                .Select(Normalize)
                .Any(text => text.Length > 0 && normalizedWords.Any(word => text.Contains(word, StringComparison.Ordinal))))
            .GroupBy(output => (output.Player, output.Slot))
            .Select(group => group.First())
            .OrderBy(output => output.Slot)
            .ToArray();
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
