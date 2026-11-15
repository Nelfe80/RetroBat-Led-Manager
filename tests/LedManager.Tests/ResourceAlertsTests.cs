using LedManager.Core.Configuration;
using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class ResourceAlertsTests
{
    private static readonly string[] Roles = { "bombs", "ammo", "lives" };

    private static SemanticSignal Signal(string id, string kind = "counter", long? min = 0, long? max = null, bool available = true, long? initial = null) =>
        new(id, 1, kind, null,
            new[] { new SemanticRef(0x100, "BOMB_FIRED", null) },
            Array.Empty<SemanticValue>(), 1, 1, 0, min, max, initial, "qualified", available);

    private static SignalValueChange Change(SemanticSignal signal, long? before, long? after) =>
        new(signal, SignalSemantics.Read(signal, before), SignalSemantics.Read(signal, after));

    [Theory]
    [InlineData("players.1.bombs", "bombs", 1)]
    [InlineData("players.2.lives", "lives", 2)]
    [InlineData("players.1.ammo", "ammo", 1)]
    [InlineData("players.1.resources.bombs", "bombs", 1)]
    [InlineData("players.3.Resources.Ammo", "ammo", 3)]
    public void Standard_and_older_ids_give_the_role(string id, string role, int player)
    {
        Assert.True(ResourceRoles.TryParse(Signal(id), Roles, out var parsedRole, out var parsedPlayer));
        Assert.Equal(role, parsedRole);
        Assert.Equal(player, parsedPlayer);
    }

    [Theory]
    [InlineData("players.1.bombs.used")]
    [InlineData("players.1.power.level")]
    [InlineData("players.1.game.sonic_the_hedgehog.rings")]
    [InlineData("stage")]
    [InlineData("players.x.bombs")]
    [InlineData("players.0.bombs")]
    [InlineData("players.1.continues")]
    public void Other_ids_are_not_tracked_resources(string id)
    {
        Assert.False(ResourceRoles.TryParse(Signal(id), Roles, out _, out _));
    }

    [Fact]
    public void Only_counters_and_gauges_are_resources()
    {
        Assert.True(ResourceRoles.TryParse(Signal("players.1.lives", kind: "gauge"), Roles, out _, out _));
        Assert.False(ResourceRoles.TryParse(Signal("players.1.lives", kind: "enum"), Roles, out _, out _));
        Assert.False(ResourceRoles.TryParse(Signal("players.1.lives", kind: "flag"), Roles, out _, out _));
    }

    [Fact]
    public void A_configured_role_is_tracked()
    {
        Assert.True(ResourceRoles.TryParse(Signal("players.1.continues"), new[] { "continues" }, out var role, out _));
        Assert.Equal("continues", role);
    }

    [Fact]
    public void Running_out_raises_once_and_coming_back_clears()
    {
        var tracker = new ResourceAlertTracker();
        var bombs = Signal("players.1.bombs");

        var raised = Assert.Single(tracker.Observe(new[] { Change(bombs, 1, 0) }, Roles));
        Assert.True(raised.Empty);
        Assert.Equal("bombs", raised.Role);
        Assert.Equal(1, raised.Player);

        Assert.Empty(tracker.Observe(new[] { Change(bombs, 0, 0) }, Roles));

        var cleared = Assert.Single(tracker.Observe(new[] { Change(bombs, 0, 1) }, Roles));
        Assert.False(cleared.Empty);
        Assert.Equal(1, cleared.Value);
        Assert.Empty(tracker.Empty);
    }

    [Fact]
    public void A_value_above_zero_raises_nothing()
    {
        var tracker = new ResourceAlertTracker();

        Assert.Empty(tracker.Observe(new[] { Change(Signal("players.1.bombs"), 3, 2) }, Roles));
    }

    [Fact]
    public void The_initial_value_never_raises()
    {
        var tracker = new ResourceAlertTracker();
        var bombs = Signal("players.1.bombs", initial: 0);

        Assert.Empty(tracker.Observe(new[] { Change(bombs, null, null) }, Roles));
    }

    [Fact]
    public void An_unavailable_or_out_of_bounds_value_changes_nothing()
    {
        var tracker = new ResourceAlertTracker();

        Assert.Empty(tracker.Observe(new[] { Change(Signal("players.1.bombs", available: false), 1, 0) }, Roles));
        Assert.Empty(tracker.Observe(new[] { Change(Signal("players.1.bombs", min: 1), 1, 0) }, Roles));

        var bombs = Signal("players.1.bombs", max: 9);
        Assert.Single(tracker.Observe(new[] { Change(bombs, 1, 0) }, Roles));
        // 200 is out of [0, 9]: not a sign the stock came back
        Assert.Empty(tracker.Observe(new[] { Change(bombs, 0, 200) }, Roles));
        Assert.Single(tracker.Empty);
    }

    [Fact]
    public void Players_are_tracked_apart()
    {
        var tracker = new ResourceAlertTracker();

        tracker.Observe(new[] { Change(Signal("players.1.bombs"), 1, 0) }, Roles);
        tracker.Observe(new[] { Change(Signal("players.2.bombs"), 1, 0) }, Roles);
        Assert.Equal(2, tracker.Empty.Count);

        tracker.Observe(new[] { Change(Signal("players.2.bombs"), 0, 2) }, Roles);
        Assert.Equal(("bombs", 1), Assert.Single(tracker.Empty));
    }

    private static PanelState Panel(params PanelOutput[] outputs) => new() { System = "fbneo", Rom = "19xx", Outputs = outputs };

    [Fact]
    public void The_button_whose_function_matches_the_role_is_found()
    {
        var panel = Panel(
            new PanelOutput { Player = 1, Slot = 1, Target = "SLOT:1", Color = "RED", Id = "1", Name = "1", Function = "Shoot" },
            new PanelOutput { Player = 1, Slot = 2, Target = "SLOT:2", Color = "BLUE", Id = "2", Name = "2", Function = "Bomb" },
            new PanelOutput { Player = 2, Slot = 2, Target = "SLOT:2", Color = "BLUE", Id = "2", Name = "2", Function = "Bomb" });

        var p1 = Assert.Single(ResourceButtonMatcher.Find(panel, 1, new[] { "bomb", "grenade" }));
        Assert.Equal(2, p1.Slot);
        Assert.Equal(1, p1.Player);

        var p2 = Assert.Single(ResourceButtonMatcher.Find(panel, 2, new[] { "bomb" }));
        Assert.Equal(2, p2.Player);

        Assert.Empty(ResourceButtonMatcher.Find(panel, 3, new[] { "bomb" }));
    }

    [Fact]
    public void No_words_or_no_function_means_no_button()
    {
        var panel = Panel(
            new PanelOutput { Player = 1, Slot = 1, Target = "SLOT:1", Color = "RED", Function = "Button 1" },
            new PanelOutput { Player = 1, Slot = 2, Target = "SLOT:2", Color = "BLUE" });

        Assert.Empty(ResourceButtonMatcher.Find(panel, 1, Array.Empty<string>()));
        Assert.Empty(ResourceButtonMatcher.Find(panel, 1, new[] { "bomb" }));
        Assert.Empty(ResourceButtonMatcher.Find(null, 1, new[] { "bomb" }));
    }

    [Fact]
    public void A_panel_without_players_serves_player_one()
    {
        var panel = Panel(new PanelOutput { Slot = 3, Target = "SLOT:3", Color = "YELLOW", Function = "Grenade" });

        Assert.Single(ResourceButtonMatcher.Find(panel, 1, new[] { "bomb", "grenade" }));
        Assert.Empty(ResourceButtonMatcher.Find(panel, 2, new[] { "grenade" }));
    }

    [Fact]
    public void A_dynpanel_bomb_button_becomes_the_bomb_target()
    {
        // Shape of APIExpose\resources\dynpanels\games\19xx.json, reduced to what is read.
        var root = Path.Combine(Path.GetTempPath(), "ledmanager-tests-" + Guid.NewGuid().ToString("N"));
        var games = Path.Combine(root, "APIExpose", "resources", "dynpanels", "games");
        var plugin = Path.Combine(root, "LedManager");
        Directory.CreateDirectory(games);
        Directory.CreateDirectory(plugin);
        try
        {
            File.WriteAllText(Path.Combine(games, "19xx.json"), """
                {"schema":"api_expose.panel.v1","rom":"19xx","players":{"1":{
                  "buttons":{"1":{"game_button":1,"function":"Shoot","color":"Red"},"2":{"game_button":2,"function":"Bomb","color":"Blue"}},
                  "layouts":{"8-Button":{"buttons":{"1":{"panel_slot":1},"2":{"panel_slot":2}}}}}}}
                """);
            var panel = new PanelState { System = "fbneo", Rom = "19xx", LayoutId = "8-Button", Outputs = Array.Empty<PanelOutput>() }
                .EnrichFromDynpanel(plugin);

            var button = Assert.Single(ResourceButtonMatcher.Find(panel, 1, InstrumentsConfig.DefaultRoleButtons["bombs"]));
            Assert.Equal(2, button.Slot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_configuration_has_defaults_and_reads_its_sections()
    {
        var path = Path.Combine(Path.GetTempPath(), "ledmanager-tests-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, """
                [Instruments]
                ZeroBlink=false
                BlinkColor=RED
                BlinkOnMs=200
                BlinkIntervalMs=100
                [InstrumentButtons]
                lives=start
                continues=coin, credit
                """);
            var config = LedManagerConfig.Load(path).Instruments;

            Assert.True(config.Enabled);
            Assert.False(config.ZeroBlink);
            Assert.Equal("RED", config.BlinkColor);
            Assert.Equal(200, config.BlinkOnMs);
            Assert.Equal(300, config.BlinkIntervalMs); // never shorter than on + 100
            Assert.Equal(new[] { "start" }, config.RoleButtons["lives"]);
            Assert.Equal(new[] { "coin", "credit" }, config.RoleButtons["continues"]);
            Assert.Equal(new[] { "bomb", "grenade" }, config.RoleButtons["bombs"]);
        }
        finally
        {
            File.Delete(path);
        }

        var defaults = new InstrumentsConfig();
        Assert.True(defaults.ZeroBlink);
        Assert.Empty(defaults.RoleButtons["lives"]);
        Assert.Contains("ammo", defaults.Roles);
    }
}
