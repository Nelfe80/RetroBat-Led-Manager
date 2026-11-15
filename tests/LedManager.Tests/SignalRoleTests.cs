using System.Text.Json;
using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class SignalRoleTests
{
    private static readonly string[] Roles = { "bombs", "ammo", "lives" };

    private static SemanticSignal ParseOne(string signalJson)
    {
        var json = "{\"Type\":\"deck.semantics\",\"Payload\":{\"Schema\":\"nelfeplay.deck.semantics/1\",\"LaunchId\":\"a\",\"Status\":\"loaded\",\"Signals\":[" + signalJson + "]}}";
        using var doc = JsonDocument.Parse(json);
        return Assert.Single(SignalSemantics.Parse(doc.RootElement)!.Signals);
    }

    private static string Json(string id, string? roleField, string kind = "counter", string player = "1") =>
        "{\"Id\":\"" + id + "\",\"Player\":" + player + ",\"Kind\":\"" + kind + "\"," + (roleField is null ? "" : roleField + ",") +
        "\"From\":[{\"Address\":\"0x10\",\"Action\":\"A\",\"Player\":null}],\"Min\":0}";

    [Fact]
    public void Given_role_is_read_and_wins_over_the_id()
    {
        var signal = ParseOne(Json("players.1.munitions", "\"Role\":\"bombs\""));
        Assert.True(signal.RoleGiven);
        Assert.Equal("bombs", signal.Role);
        Assert.True(ResourceRoles.TryParse(signal, Roles, out var role, out var player));
        Assert.Equal("bombs", role);
        Assert.Equal(1, player);
    }

    [Fact]
    public void Role_is_read_without_regard_to_case_of_name_or_value()
    {
        var signal = ParseOne(Json("custom.stock", "\"role\":\"Ammo\""));
        Assert.True(ResourceRoles.TryParse(signal, Roles, out var role, out _));
        Assert.Equal("ammo", role);
    }

    [Fact]
    public void Null_role_is_exact_a_bombs_used_signal_is_never_the_stock()
    {
        var signal = ParseOne(Json("players.1.bombs.used", "\"Role\":null"));
        Assert.True(signal.RoleGiven);
        Assert.Null(signal.Role);
        Assert.False(ResourceRoles.TryParse(signal, Roles, out _, out _));
    }

    [Fact]
    public void Null_role_also_cancels_a_stock_looking_id()
    {
        var signal = ParseOne(Json("players.1.bombs", "\"Role\":null"));
        Assert.False(ResourceRoles.TryParse(signal, Roles, out _, out _));
    }

    [Fact]
    public void Absent_role_keeps_the_deduction_from_the_id()
    {
        var stock = ParseOne(Json("players.1.bombs", null));
        Assert.False(stock.RoleGiven);
        Assert.True(ResourceRoles.TryParse(stock, Roles, out var role, out _));
        Assert.Equal("bombs", role);
        Assert.True(ResourceRoles.TryParse(ParseOne(Json("players.1.resources.lives", null)), Roles, out role, out _));
        Assert.Equal("lives", role);
        Assert.False(ResourceRoles.TryParse(ParseOne(Json("players.1.bombs.used", null)), Roles, out _, out _));
    }

    [Fact]
    public void Role_outside_the_tracked_list_is_ignored()
    {
        var signal = ParseOne(Json("players.1.life", "\"Role\":\"health\""));
        Assert.Equal("health", signal.Role);
        Assert.False(ResourceRoles.TryParse(signal, Roles, out _, out _));
        Assert.True(ResourceRoles.TryParse(signal, new[] { "health" }, out var role, out _));
        Assert.Equal("health", role);
    }

    [Fact]
    public void Given_role_still_needs_a_counter_or_a_gauge_and_a_player()
    {
        var level = ParseOne(Json("players.1.bombs", "\"Role\":\"bombs\"", kind: "level"));
        Assert.False(ResourceRoles.TryParse(level, Roles, out _, out _));

        var noPlayer = ParseOne(Json("shared.stock", "\"Role\":\"bombs\"", player: "null"));
        Assert.False(ResourceRoles.TryParse(noPlayer, Roles, out _, out _));

        var player2 = ParseOne(Json("stock.p2", "\"Role\":\"bombs\"", player: "2"));
        Assert.True(ResourceRoles.TryParse(player2, Roles, out _, out var p));
        Assert.Equal(2, p);
    }

    [Fact]
    public void Given_role_drives_the_empty_stock_alert()
    {
        var signal = ParseOne(Json("custom.stock", "\"Role\":\"bombs\""));
        var tracker = new ResourceAlertTracker();
        var alerts = tracker.Observe(
            new[] { new SignalValueChange(signal, SignalSemantics.Read(signal, 2), SignalSemantics.Read(signal, 0)) }, Roles);
        var alert = Assert.Single(alerts);
        Assert.Equal("bombs", alert.Role);
        Assert.True(alert.Empty);
    }
}
