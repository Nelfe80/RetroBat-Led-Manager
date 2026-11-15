using LedManager.Core.Configuration;
using LedManager.Core.Effects;
using LedManager.Core.Routing;
using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class LaunchTrackerTests
{
    private const string X = "0123456789abcdef0123456789abcdef";
    private const string Y = "fedcba9876543210fedcba9876543210";

    [Fact]
    public void Es_hook_raw_then_enriched_start_is_one_game()
    {
        var tracker = new LaunchTracker();

        Assert.Equal(LaunchStartKind.New, tracker.OnStarted(null, 0).Kind);           // ui.game.started.raw
        var enriched = tracker.OnStarted(X, 15);                                       // ui.game.started
        Assert.Equal(LaunchStartKind.Duplicate, enriched.Kind);
        Assert.Equal(X, enriched.AdoptedLaunchId);
        Assert.Equal(X, tracker.RunningLaunchId);

        var rawEnd = tracker.OnEnded(null, 60_000);                                   // ui.game.ended.raw
        Assert.Equal(LaunchEndKind.End, rawEnd.Kind);
        Assert.Equal(X, rawEnd.LaunchId);
        Assert.Equal(LaunchEndKind.Duplicate, tracker.OnEnded(X, 60_010).Kind);       // ui.game.ended
    }

    [Fact]
    public void Direct_launch_then_es_hook_is_one_game()
    {
        var tracker = new LaunchTracker();

        Assert.Equal(LaunchStartKind.New, tracker.OnStarted(X, 0).Kind);             // commands.launch
        Assert.Equal(LaunchStartKind.Duplicate, tracker.OnStarted(null, 400).Kind);  // ES hook, .raw
        var hook = tracker.OnStarted(X, 410);                                         // ES hook, same LaunchId
        Assert.Equal(LaunchStartKind.Duplicate, hook.Kind);
        Assert.Null(hook.AdoptedLaunchId);
    }

    [Fact]
    public void The_same_id_is_a_duplicate_however_late()
    {
        var tracker = new LaunchTracker();
        tracker.OnStarted(X, 0);

        Assert.Equal(LaunchStartKind.Duplicate, tracker.OnStarted(X, 90_000).Kind);
    }

    [Fact]
    public void A_new_id_after_the_end_is_a_new_game()
    {
        var tracker = new LaunchTracker();
        tracker.OnStarted(X, 0);
        tracker.OnEnded(X, 1_000);

        var next = tracker.OnStarted(Y, 1_500);
        Assert.Equal(LaunchStartKind.New, next.Kind);
        Assert.Equal(Y, next.LaunchId);
    }

    [Fact]
    public void Another_id_while_running_is_a_new_game_when_the_end_was_lost()
    {
        var tracker = new LaunchTracker();
        tracker.OnStarted(X, 0);

        Assert.Equal(LaunchStartKind.New, tracker.OnStarted(Y, 5_000).Kind);
        Assert.Equal(Y, tracker.RunningLaunchId);
    }

    [Fact]
    public void An_older_api_without_ids_keeps_working()
    {
        var tracker = new LaunchTracker();

        Assert.Equal(LaunchStartKind.New, tracker.OnStarted(null, 0).Kind);
        Assert.Equal(LaunchStartKind.Duplicate, tracker.OnStarted(null, 20).Kind);
        Assert.Equal(LaunchEndKind.End, tracker.OnEnded(null, 30_000).Kind);
        Assert.Equal(LaunchEndKind.Duplicate, tracker.OnEnded(null, 30_020).Kind);

        // next game, then a lost end and a game long after: each one is new
        Assert.Equal(LaunchStartKind.New, tracker.OnStarted(null, 40_000).Kind);
        Assert.Equal(LaunchStartKind.New, tracker.OnStarted(null, 400_000).Kind);
    }

    [Fact]
    public void An_end_without_a_start_seen_is_still_an_end()
    {
        var tracker = new LaunchTracker();

        var end = tracker.OnEnded(X, 0);
        Assert.Equal(LaunchEndKind.End, end.Kind);
        Assert.Equal(X, end.LaunchId);
    }

    [Fact]
    public void An_end_for_another_id_ends_the_running_game_and_says_so()
    {
        var tracker = new LaunchTracker();
        tracker.OnStarted(X, 0);

        var end = tracker.OnEnded(Y, 10_000);
        Assert.Equal(LaunchEndKind.End, end.Kind);
        Assert.Equal(X, end.LaunchId);
        Assert.Equal(Y, end.Mismatch);
        Assert.False(tracker.IsRunning);
    }

    [Fact]
    public void A_late_repeat_of_an_end_is_an_end_again()
    {
        var tracker = new LaunchTracker();
        tracker.OnStarted(X, 0);
        tracker.OnEnded(X, 1_000);

        Assert.Equal(LaunchEndKind.End, tracker.OnEnded(null, 1_000 + LaunchTracker.CompanionWindowMs + 1).Kind);
    }

    [Theory]
    // ES hook (EmulationStationWatcherProvider)
    [InlineData("""{"Type":"ui.game.started","Payload":{"EventName":"game-start","RawArgs":["a"],"Context":{"Running":{"LaunchId":"0123456789abcdef0123456789abcdef"}},"LaunchId":"0123456789abcdef0123456789abcdef"}}""")]
    // direct launch (LancementDirect)
    [InlineData("""{"Type":"ui.game.started","Payload":{"Source":"commands.launch","SystemId":"fbneo","Rom":"19xx","LaunchId":"0123456789abcdef0123456789abcdef"}}""")]
    // MULTIPLAYER
    [InlineData("""{"Type":"ui.game.started","Payload":{"Source":"multiplayer","Room":"r1","LaunchId":"0123456789abcdef0123456789abcdef"}}""")]
    // only in the context
    [InlineData("""{"Type":"ui.game.started","Payload":{"EventName":"game-start","Context":{"Running":{"LaunchId":"0123456789abcdef0123456789abcdef"}}}}""")]
    public void The_launch_id_is_read_on_every_path(string json)
    {
        Assert.Equal(X, LaunchIds.Read(LedEvent.FromJson(json)));
    }

    [Theory]
    [InlineData("""{"Type":"ui.game.started.raw","Payload":{"EventName":"game-start","RawArgs":["a"]}}""")]
    [InlineData("""{"Type":"ui.game.ended","Payload":{"EventName":"game-end","LaunchId":null}}""")]
    [InlineData("""{"Type":"game.start"}""")]
    public void Forms_without_id_give_none(string json)
    {
        Assert.Null(LaunchIds.Read(LedEvent.FromJson(json)));
    }

    [Fact]
    public void The_semantics_keep_their_values_when_the_id_is_adopted()
    {
        var semantics = new SignalSemanticsTracker();
        semantics.BeginLaunch(null);                                // .raw start
        semantics.Observe(new MemorySignal(0x10, "BOMB_FIRED", null, 0));
        semantics.Apply(new SemanticsSnapshot(X, "fbneo", "19xx", "loaded", 1,
            new[]
            {
                new SemanticSignal("players.1.bombs", 1, "counter", null, new[] { new SemanticRef(0x10, "BOMB_FIRED", null) },
                    Array.Empty<SemanticValue>(), 1, 1, 0, 0, null, 3, "qualified", true)
            },
            Array.Empty<SemanticInstrument>()));

        semantics.AdoptLaunchId(X);                                 // enriched start, same game
        Assert.Equal(X, semantics.RunningLaunchId);
        Assert.Equal(0, semantics.Read("players.1.bombs").Value);
    }

    [Theory]
    [InlineData("""{"Type":"ui.game.selected","Payload":{"SystemId":"fbneo","GameId":"19xx","Color":"RED","Slot":3}}""")]
    [InlineData("""{"Type":"ui.game.selected.raw","Payload":{"EventName":"game-selected","RawArgs":["fbneo","19xx.zip"]}}""")]
    [InlineData("""{"Type":"ui.system.selected","Payload":{"SystemId":"fbneo"}}""")]
    [InlineData("""{"Type":"ui.system.selected.raw","Payload":{"EventName":"system-selected"}}""")]
    public void A_replayed_selection_drives_no_effect(string json)
    {
        var evt = LedEvent.FromJson(json);

        Assert.True(evt.IsFrontendSelection);
        Assert.False(evt.IsFrontendGameStart);
        Assert.False(evt.IsPanelState);
        // even if it reached the router or the catalog, nothing would be sent
        Assert.Empty(new CommandRouter(new LedManagerConfig()).RouteEvent(evt));
        var catalog = DefaultEffectCatalog.Load(Path.Combine(AppContext.BaseDirectory, "default.mem.effects.json"), HardwareConfig.AllEnabled);
        Assert.Empty(catalog.Resolve(evt));
    }

    [Fact]
    public void Starts_and_ends_are_not_selections()
    {
        Assert.False(LedEvent.FromJson("""{"Type":"ui.game.started","Payload":{}}""").IsFrontendSelection);
        Assert.False(LedEvent.FromJson("""{"Type":"ui.game.ended","Payload":{}}""").IsFrontendSelection);
        Assert.False(LedEvent.FromJson("""{"Type":"panel.state","Payload":{}}""").IsFrontendSelection);
    }
}
