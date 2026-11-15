using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class PanelStateInputsTests
{
    private static readonly string[] Bomb = { "bomb", "grenade" };

    private static PanelState FromJson(string json) => PanelState.FromPanelEvent(LedEvent.FromJson(json));

    private const string PanelStateJson = """
        {"Type":"panel.state","Payload":{"ActivePanel":{"Id":"p","Slots":[],
          "ControlMap":{"Inputs":[
            {"Slot":1,"Player":1,"Function":"Shoot","Color":"RED","DeviceType":"button"},
            {"Slot":3,"Player":1,"Function":"Bomb","Color":"YELLOW","DeviceType":"button"},
            {"Slot":2,"Player":2,"Function":"Grenade launcher","Color":"BLUE","DeviceType":"button"},
            {"Slot":0,"Player":1,"Function":"","Color":"WHITE","DeviceType":"joystick"}
          ]}}}}
        """;

    [Fact]
    public void Panel_state_inputs_are_read()
    {
        var panel = FromJson(PanelStateJson);
        Assert.Equal(4, panel.ControlInputs.Count);
        var bomb = panel.ControlInputs.Single(i => i.Slot == 3);
        Assert.Equal("Bomb", bomb.Function);
        Assert.Equal("YELLOW", bomb.Color);
        Assert.Equal("button", bomb.DeviceType);
        Assert.Equal(1, bomb.Player);
    }

    [Fact]
    public void Role_button_is_found_from_panel_state_inputs_alone()
    {
        var panel = FromJson(PanelStateJson);
        var p1 = Assert.Single(ResourceButtonMatcher.Find(panel, 1, Bomb));
        Assert.Equal(3, p1.Slot);
        Assert.Equal("YELLOW", p1.Color);
        var p2 = Assert.Single(ResourceButtonMatcher.Find(panel, 2, Bomb));
        Assert.Equal(2, p2.Slot);
        Assert.Empty(ResourceButtonMatcher.Find(panel, 1, new[] { "reload" }));
    }

    [Fact]
    public void Dedicated_file_outputs_win_over_panel_state_inputs()
    {
        var panel = new PanelState
        {
            Outputs = new[] { new PanelOutput { Player = 1, Slot = 5, Target = "SLOT:5", Color = "GREEN", Function = "Bomb" } },
            ControlInputs = new[] { new PanelControlInput(3, 1, "Bomb", "YELLOW", "button") }
        };
        var button = Assert.Single(ResourceButtonMatcher.Find(panel, 1, Bomb));
        Assert.Equal(5, button.Slot);
    }

    [Fact]
    public void Panel_state_inputs_are_used_when_the_file_names_no_such_button()
    {
        var panel = new PanelState
        {
            Outputs = new[] { new PanelOutput { Player = 1, Slot = 5, Target = "SLOT:5", Function = "Jump" } },
            ControlInputs = new[] { new PanelControlInput(3, 1, "Bomb", "YELLOW", "button") }
        };
        Assert.Equal(3, Assert.Single(ResourceButtonMatcher.Find(panel, 1, Bomb)).Slot);
    }

    [Fact]
    public void Inputs_survive_the_dynpanel_enrichment_and_a_missing_dynpanel()
    {
        var panel = FromJson(PanelStateJson);
        var enriched = panel.EnrichFromDynpanel(Path.Combine(Path.GetTempPath(), "no_such_led_dir"));
        Assert.Equal(panel.ControlInputs.Count, enriched.ControlInputs.Count);
    }

    [Fact]
    public void A_panel_without_control_map_has_no_inputs()
    {
        var panel = FromJson("{\"Type\":\"panel.state\",\"Payload\":{\"ActivePanel\":{\"Id\":\"p\",\"Slots\":[]}}}");
        Assert.Empty(panel.ControlInputs);
        Assert.Empty(ResourceButtonMatcher.Find(panel, 1, Bomb));
    }
}
