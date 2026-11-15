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
