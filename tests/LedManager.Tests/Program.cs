using LedManager.Core.Configuration;
using LedManager.Core.Effects;
using LedManager.Core.Routing;
using LedManager.Core.Runtime;

var tests = new LedManagerCoreTests();
tests.RoutePlayersToDedicatedSenders();
tests.RouteGlobalMatrixScoreToGlobalSender();
tests.RouteIngameScoreButIgnoreHiscore();
tests.BatchPanelStateBySender();
tests.BatchPanelStateTurnsBlackSlotsOffFirst();
tests.BatchPanelStateCarriesSequence();
tests.IngameOverlayRestoresBaseSlotColor();
tests.IngameOverlayKeepsActiveOverrideOnFullRestore();
tests.IngameOverlayDoesNotTreatMissingColorAsOff();
tests.IngameOverlayAllowsOnlyFirstFullRestorePerSession();
tests.IngameOverlayFullRestoreResetsOnNewSession();
tests.ResolveDefaultMemCatalogAction();
tests.ResolveDefaultMemCatalogFamilyFallback();
tests.ParseWrapperSignalEventAsMemAction();
tests.ResolveWrapperSignalEventActionRule();
tests.ParseRetroarchActionEventAsMemAction();
tests.ParseApiExposePayloadColorAndFamily();
tests.ResolveMemPayloadColorOverridesCatalogDefault();
tests.ResolveRetroarchActionObjectDestroyedFallback();
tests.ResolveWildcardTransformationVariant();
tests.ResolveUnmappedMemActionWithCatalogFallback();
tests.ResolvePanelSweepUsesPhysicalColumns();
tests.EnrichDynpanelReadsPlayerLayoutButtonSlots();
tests.ResolveSystemTimerUsesStartSelectTelemetry();
Console.WriteLine("All LedManager core tests passed.");

internal sealed class LedManagerCoreTests
{
    public void RoutePlayersToDedicatedSenders()
    {
        var router = new CommandRouter(Config());

        var p1 = router.RouteEvent(LedEvent.FromJson("""{"type":"panel.event.changed","player":1,"slot":1,"color":"red"}""")).Single();
        var p2 = router.RouteEvent(LedEvent.FromJson("""{"type":"panel.event.changed","player":2,"slot":1,"color":"blue"}""")).Single();

        Equal("P1", p1.SenderId);
        Equal("SLOT 1 RED", p1.Command);
        Equal("P2", p2.SenderId);
        Equal("SLOT 1 BLUE", p2.Command);
    }

    public void RouteGlobalMatrixScoreToGlobalSender()
    {
        var router = new CommandRouter(Config());
        var command = router.RouteEvent(LedEvent.FromJson("""{"type":"arcade.output.changed","effect":"matrix_score","target":"GLOBAL:MATRIX1","value":"12345","color":"green"}""")).Single();

        Equal("GLOBAL", command.SenderId);
        Equal("MATRIXSCORE MATRIX1 12345 GREEN", command.Command);
    }

    public void RouteIngameScoreButIgnoreHiscore()
    {
        var router = new CommandRouter(Config());

        var live = router.RouteEvent(LedEvent.FromJson("""{"stream":"ingame","type":"ingame.score.changed","score":9876}""")).Single();
        var durable = router.RouteEvent(LedEvent.FromJson("""{"stream":"hiscore","type":"hiscore.updated","score":9999}"""));

        Equal("GLOBAL", live.SenderId);
        Equal("MATRIXSCORE MATRIX1 9876 GREEN", live.Command);
        Equal(0, durable.Count);
    }

    public void BatchPanelStateBySender()
    {
        var router = new CommandRouter(Config());
        var state = new PanelState
        {
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Color = "red" },
                new PanelOutput { Player = 1, Slot = 2, Color = "blue" },
                new PanelOutput { Player = 2, Slot = 1, Color = "green" }
            }
        };

        var commands = router.RoutePanelState(state).OrderBy(c => c.SenderId).ToArray();
        Equal(2, commands.Length);
        Equal("P1", commands[0].SenderId);
        Equal("BATCH SLOTPWM 1 RED;SLOTPWM 2 BLUE", commands[0].Command);
        Equal("P2", commands[1].SenderId);
        Equal("BATCH SLOTPWM 1 GREEN", commands[1].Command);
    }

    public void BatchPanelStateTurnsBlackSlotsOffFirst()
    {
        var router = new CommandRouter(Config());
        var state = new PanelState
        {
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Color = "gray" },
                new PanelOutput { Player = 1, Slot = 2, Color = "gray" },
                new PanelOutput { Player = 1, Slot = 6, Color = "black" }
            }
        };

        var command = router.RoutePanelState(state).Single();
        Equal("BATCH SLOTPWM 6 BLACK;SLOTPWM 1 GRAY;SLOTPWM 2 GRAY", command.Command);
    }

    public void BatchPanelStateCarriesSequence()
    {
        var router = new CommandRouter(Config());
        var state = new PanelState
        {
            Sequence = 42,
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Color = "gray" }
            }
        };

        var command = router.RoutePanelState(state).Single();
        Equal(true, command.IsPanelUpdate);
        Equal(42L, command.Sequence);
    }

    public void IngameOverlayRestoresBaseSlotColor()
    {
        var overlay = new IngamePanelOverlay();
        var basePanel = new PanelState
        {
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Target = "SLOT:1", Color = "YELLOW", OutputName = "lamp" },
                new PanelOutput { Player = 1, Slot = 2, Target = "SLOT:2", Color = "BLUE" }
            }
        };

        overlay.Begin(basePanel);
        var output = overlay.FindBaseOutputSignal("lamp")!;
        overlay.SetOverride(output, "RED");
        var restored = overlay.ClearOverride(output)!;

        Equal("YELLOW", restored.Color);
        Equal(1, restored.Slot);
    }

    public void IngameOverlayKeepsActiveOverrideOnFullRestore()
    {
        var overlay = new IngamePanelOverlay();
        var basePanel = new PanelState
        {
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Target = "SLOT:1", Color = "YELLOW" },
                new PanelOutput { Player = 1, Slot = 2, Target = "SLOT:2", Color = "BLUE" }
            }
        };

        overlay.Begin(basePanel);
        overlay.SetOverride(basePanel.Outputs[1], "RED");
        var effective = overlay.BuildEffectivePanel()!;

        Equal("YELLOW", effective.Outputs.Single(output => output.Slot == 1).Color);
        Equal("RED", effective.Outputs.Single(output => output.Slot == 2).Color);
    }

    public void IngameOverlayDoesNotTreatMissingColorAsOff()
    {
        Equal(false, IngamePanelOverlay.IsOffColor(null));
        Equal(false, IngamePanelOverlay.IsOffColor(""));
        Equal(true, IngamePanelOverlay.IsOffColor("BLACK"));
        Equal(true, IngamePanelOverlay.IsOffColor("OFF"));
    }

    public void IngameOverlayAllowsOnlyFirstFullRestorePerSession()
    {
        var overlay = new IngamePanelOverlay();
        overlay.Begin(new PanelState
        {
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Target = "SLOT:1", Color = "YELLOW" },
                new PanelOutput { Player = 1, Slot = 2, Target = "SLOT:2", Color = "BLUE" }
            }
        });

        var first = overlay.PlanFullRestore();
        var second = overlay.PlanFullRestore();

        Equal(true, first.ShouldSend);
        Equal("session-first-all-off", first.Reason);
        Equal(false, second.ShouldSend);
        Equal("duplicate", second.Reason);
    }

    public void IngameOverlayFullRestoreResetsOnNewSession()
    {
        var overlay = new IngamePanelOverlay();
        var panel = new PanelState
        {
            Outputs = new[]
            {
                new PanelOutput { Player = 1, Slot = 1, Target = "SLOT:1", Color = "YELLOW" }
            }
        };

        overlay.Begin(panel);
        Equal(true, overlay.PlanFullRestore().ShouldSend);
        Equal(false, overlay.PlanFullRestore().ShouldSend);

        overlay.Begin(panel);
        Equal(true, overlay.PlanFullRestore().ShouldSend);
    }

    public void ResolveDefaultMemCatalogAction()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json");
        var router = new CommandRouter(Config());
        var expanded = catalog.Resolve(LedEvent.FromJson("""{"stream":"mem","type":"mem.action","action":"COIN_GAIN","family":"scoring.collectibles","player":1,"value":10}"""));
        var routed = expanded.SelectMany(router.RouteEvent).ToArray();

        // Un pickup allume une colonne physique en jaune; le firmware restaure
        // chaque slot tout seul apres durationMs.
        Equal(2, routed.Length);
        Equal("P1", routed[0].SenderId);
        Equal(true, routed.All(command => command.Command.StartsWith("FLASH ", StringComparison.OrdinalIgnoreCase)));
        Equal(true, routed.All(command => command.Command.Contains(" YELLOW ", StringComparison.OrdinalIgnoreCase)));
    }

    public void ResolveDefaultMemCatalogFamilyFallback()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json");
        var router = new CommandRouter(Config());
        var expanded = catalog.Resolve(LedEvent.FromJson("""{"stream":"mem","type":"mem.action","action":"UNKNOWN_HEALTH_DROP","family":"resources.health","player":1}"""));
        var routed = expanded.SelectMany(router.RouteEvent).ToArray();

        Equal(1, routed.Length);
        Equal("P1", routed[0].SenderId);
        Equal("ALL GREEN", routed[0].Command);
    }

    public void ParseWrapperSignalEventAsMemAction()
    {
        var evt = LedEvent.FromJson("""
            {
                "Type": "retroarch.wrapper.changed",
                "Ts": "2026-06-11T05:55:12.0242118Z",
                "NodeId": "cab-01",
                "CorrelationId": "682c004d-5b3f-473c-a38c-e21dbbd8ed71",
                "Payload": {
                    "Source": "retroarch.wrapper.pipe",
                    "SystemId": "gbc",
                    "Rom": "sonic-the-hedgehog-usa-europe",
                    "signal": {
                        "Channel": "ACTION",
                        "Name": "PAUSE_OFF",
                        "Value": 0
                    }
                }
            }
            """);

        Equal("mem", evt.Stream);
        Equal("PAUSE_OFF", evt.Action);
        Equal("0", evt.Value);
        Equal("gbc", evt.System);
        Equal("sonic-the-hedgehog-usa-europe", evt.Rom);
    }

    public void ResolveWrapperSignalEventActionRule()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json");
        var evt = LedEvent.FromJson("""
            {
                "Type": "retroarch.wrapper.changed",
                "Ts": "2026-06-11T05:55:12.0242118Z",
                "NodeId": "cab-01",
                "CorrelationId": "682c004d-5b3f-473c-a38c-e21dbbd8ed71",
                "Payload": {
                    "Source": "retroarch.wrapper.pipe",
                    "SystemId": "gbc",
                    "Rom": "sonic-the-hedgehog-usa-europe",
                    "signal": {
                        "Channel": "ACTION",
                        "Name": "PAUSE_OFF",
                        "Value": 0
                    }
                }
            }
            """);

        var expanded = catalog.Resolve(evt);

        if (expanded.Count == 0)
        {
            throw new InvalidOperationException("Expected wrapper signal event to resolve to at least one effect step.");
        }
    }

    public void ParseRetroarchActionEventAsMemAction()
    {
        var evt = LedEvent.FromJson("""
            {
              "Type": "retroarch.action",
              "Payload": {
                "Source": "retroarch.wrapper.pipe",
                "SystemId": "nes",
                "Rom": "super-mario-bros-3",
                "DefinitionFile": "E:\\RetroBat\\plugins\\APIExpose\\resources\\ram\\nes\\super-mario-bros-3.MEM",
                "actionType": "OBJECT_DESTROYED",
                "sourceCategory": "Enemy defeated by stomp/shell chain hit",
                "Value": 1,
                "Rate": 1,
                "Address": "0x0005F4",
                "RawValueHex": "0x01"
              }
            }
            """);

        Equal("retroarch.action", evt.Type);
        Equal("mem", evt.Stream);
        Equal("OBJECT_DESTROYED", evt.Action);
        Equal("1", evt.Value);
        Equal("nes", evt.System);
        Equal("super-mario-bros-3", evt.Rom);
    }

    public void ParseApiExposePayloadColorAndFamily()
    {
        var evt = LedEvent.FromJson("""
            {
              "Type": "ingame.action",
              "Payload": {
                "Source": "mame.lua",
                "SystemId": "arcade",
                "Rom": "1943",
                "actionType": "OBJECT_DESTROYED",
                "Value": 100,
                "color": "orange",
                "family": "world_interaction.objects",
                "signal": {
                  "Name": "OBJECT_DESTROYED",
                  "Color": "orange",
                  "Family": "world_interaction.objects"
                }
              }
            }
            """);

        Equal("mem", evt.Stream);
        Equal("OBJECT_DESTROYED", evt.Action);
        Equal("orange", evt.Color);
        Equal("world_interaction.objects", evt.Family);
        Equal("arcade", evt.System);
        Equal("1943", evt.Rom);
    }

    public void ResolveMemPayloadColorOverridesCatalogDefault()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json", new HardwareConfig());
        var router = new CommandRouter(Config());
        var routed = catalog.Resolve(LedEvent.FromJson("""
            {
              "Type": "ingame.action",
              "Payload": {
                "Source": "mame.lua",
                "SystemId": "arcade",
                "Rom": "1943",
                "actionType": "OBJECT_DESTROYED",
                "Value": 100,
                "color": "orange",
                "family": "world_interaction.objects"
              }
            }
            """))
            .SelectMany(router.RouteEvent)
            .ToArray();

        Equal(true, routed.Length >= 1);
        Equal(true, routed.Any(command => command.Command.Contains(" ORANGE ", StringComparison.OrdinalIgnoreCase)));
        Equal(false, routed.Any(command => command.Command.Contains(" BLUE ", StringComparison.OrdinalIgnoreCase)));
    }

    public void ResolveRetroarchActionObjectDestroyedFallback()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json", new HardwareConfig());
        var router = new CommandRouter(Config());
        var evt = LedEvent.FromJson("""
            {
              "Type": "retroarch.action",
              "Payload": {
                "Source": "retroarch.wrapper.pipe",
                "SystemId": "nes",
                "Rom": "super-mario-bros-3",
                "actionType": "OBJECT_DESTROYED",
                "sourceCategory": "Enemy defeated by stomp/shell chain hit",
                "Value": 1
              }
            }
            """);

        var routed = catalog.Resolve(evt).SelectMany(router.RouteEvent).ToArray();

        Equal(true, routed.Length >= 2);
        Equal(true, routed.All(command => command.Command.StartsWith("FLASH ", StringComparison.OrdinalIgnoreCase)));
        Equal(true, routed.Any(command => command.Command.Contains(" RED ", StringComparison.OrdinalIgnoreCase)));
        Equal(true, routed.Any(command => command.Command.Contains(" ORANGE ", StringComparison.OrdinalIgnoreCase)));
    }

    public void ResolveWildcardTransformationVariant()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json", new HardwareConfig());
        var expanded = catalog.Resolve(LedEvent.FromJson("""{"stream":"mem","type":"mem.action","action":"TRANSFORMATION_SUPER","player":1}""")).ToArray();
        var targets = expanded.Take(4).Select(evt => evt.Target).ToArray();
        var colors = expanded.Take(4).Select(evt => evt.Color).ToArray();

        Equal("SLOT:4", targets[0]);
        Equal("SLOT:1", targets[1]);
        Equal("SLOT:3", targets[2]);
        Equal("SLOT:2", targets[3]);
        Equal("YELLOW", colors[0]);
        Equal("YELLOW", colors[1]);
        Equal("CYAN", colors[2]);
        Equal("CYAN", colors[3]);
    }

    public void ResolveUnmappedMemActionWithCatalogFallback()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json", new HardwareConfig());
        var router = new CommandRouter(Config());
        var routed = catalog.Resolve(LedEvent.FromJson("""{"stream":"mem","type":"mem.action","action":"ATTACKING","player":1}"""))
            .SelectMany(router.RouteEvent)
            .ToArray();

        Equal(1, routed.Length);
        StartsWith("FLASH ", routed[0].Command);
        Contains(" CYAN ", routed[0].Command);
    }

    public void ResolvePanelSweepUsesPhysicalColumns()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json", new HardwareConfig());
        var expanded = catalog.Resolve(LedEvent.FromJson("""{"stream":"mem","type":"mem.action","action":"NEW_LEVEL","player":1}""")).ToArray();
        var targets = expanded.Take(6).Select(evt => evt.Target).ToArray();
        var colors = expanded.Take(6).Select(evt => evt.Color).ToArray();

        Equal("SLOT:4", targets[0]);
        Equal("SLOT:1", targets[1]);
        Equal("SLOT:3", targets[2]);
        Equal("SLOT:2", targets[3]);
        Equal("SLOT:5", targets[4]);
        Equal("SLOT:6", targets[5]);
        Equal("CYAN", colors[0]);
        Equal("CYAN", colors[1]);
        Equal("CYAN", colors[4]);
        Equal("CYAN", colors[5]);
    }

    public void EnrichDynpanelReadsPlayerLayoutButtonSlots()
    {
        var panel = new PanelState
        {
            System = "mame",
            Rom = "llander",
            LayoutId = "8-Button"
        }.EnrichFromDynpanel(Path.Combine(PluginRoot(), "LedManager"));

        var slot8 = panel.Outputs.Single(output => output.Player == 1 && output.Slot == 8);
        Equal("Red", slot8.Color);
        Equal("Abort", slot8.Function);
    }

    public void ResolveSystemTimerUsesStartSelectTelemetry()
    {
        var catalog = DefaultEffectCatalog.Load("default.mem.effects.json", new HardwareConfig());
        var router = new CommandRouter(Config());
        var routed = catalog.Resolve(LedEvent.FromJson("""{"stream":"mem","type":"mem.action","action":"LEVEL_TIMER","family":"system.timer","player":1}"""))
            .SelectMany(router.RouteEvent)
            .Select(command => command.Command)
            .ToArray();

        Equal(true, routed.Any(command => command.StartsWith("SET START ", StringComparison.OrdinalIgnoreCase)));
        Equal(true, routed.Any(command => command.StartsWith("SET SELECT ", StringComparison.OrdinalIgnoreCase)));
    }

    private static LedManagerConfig Config()
    {
        return new LedManagerConfig
        {
            DefaultSenderId = "P1",
            GlobalSenderId = "GLOBAL",
            Senders = new Dictionary<string, CommandSenderConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["P1"] = new CommandSenderConfig { Id = "P1", Player = 1, DryRun = true },
                ["P2"] = new CommandSenderConfig { Id = "P2", Player = 2, DryRun = true },
                ["GLOBAL"] = new CommandSenderConfig { Id = "GLOBAL", Player = 0, DryRun = true }
            },
            PlayerRouting = new Dictionary<int, string>
            {
                [0] = "GLOBAL",
                [1] = "P1",
                [2] = "P2"
            },
            Hardware = new HardwareConfig { PanelPlayers = 2 }
        };
    }

    private static string PluginRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "LedManager")) &&
                Directory.Exists(Path.Combine(dir.FullName, "APIExpose")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate plugins root containing LedManager and APIExpose.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void StartsWith(string expected, string actual)
    {
        if (!actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected '{actual}' to start with '{expected}'.");
        }
    }

    private static void EndsWith(string expected, string actual)
    {
        if (!actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected '{actual}' to end with '{expected}'.");
        }
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
        }
    }
}
