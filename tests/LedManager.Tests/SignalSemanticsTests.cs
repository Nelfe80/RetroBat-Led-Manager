using System.Text.Json;
using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class SignalSemanticsTests
{
    private const string LaunchA = "5d1c0000000000000000000000000000";
    private const string LaunchB = "7a200000000000000000000000000000";

    // The deck.semantics example of wiki/asyncapi.yaml (19xx, older resource id form),
    // plus a standard-vocabulary lives counter.
    private static string Semantics(string launchId, string bombsId = "players.1.resources.bombs") => $$$"""
        {"Type":"deck.semantics","Payload":{
          "Schema":"nelfeplay.deck.semantics/1","LaunchId":"{{{launchId}}}","SystemId":"fbneo","Rom":"19xx",
          "MemRom":"19xx-the-war-against-destiny","Status":"loaded","Version":2,"Revision":3,
          "Signals":[
            {"Id":"{{{bombsId}}}","Player":1,"Kind":"counter","Unit":"bomb","Label":"Bombs","Short":"BOMB",
             "From":[{"Address":"0xFF8305","Action":"BOMB_FIRED","Player":null},{"Address":"0xFF8305","Action":"AMMO_GAIN","Player":1}],
             "Values":[],"Scale":{"Num":1,"Den":1,"Add":0},"Min":0,"Max":null,"Initial":3,"Quality":"qualified","Available":true},
            {"Id":"players.1.power.level","Player":1,"Kind":"level","Label":"Shot","Short":"SHOT",
             "From":[{"Address":"0xFF8337","Action":"WEAPON_STATE","Player":1}],
             "Values":[{"Raw":0,"Value":1,"Label":"Normal"},{"Raw":255,"Value":2,"Label":"Boosted"}],
             "Scale":{"Num":1,"Den":1,"Add":0},"Min":1,"Max":2,"Initial":1,"Quality":"qualified","Available":true},
            {"Id":"progress.stageId","Player":null,"Kind":"level","Label":"Stage",
             "From":[{"Address":"0xFF4DD4","Action":"PROGRESSION_STAGE","Player":null}],
             "Values":[],"Scale":{"Num":1,"Den":1,"Add":1},"Min":1,"Max":7,"Initial":1,"Quality":"qualified","Available":true},
            {"Id":"players.1.bomb.power","Player":1,"Kind":"level","Label":"Bomb power",
             "From":[{"Address":"0xFF830F","Action":"RESOURCE_LOSE","Player":null}],
             "Values":[],"Scale":{"Num":1,"Den":1,"Add":0},"Quality":"unproven","Available":false},
            {"Id":"players.1.lives","Player":1,"Kind":"counter","Label":"Lives",
             "From":[{"Address":"0xFF8300","Action":"LOSE_LIFE","Player":1},{"Address":"0xFF8300","Action":"GAIN_LIFE","Player":1}],
             "Values":[],"Scale":{"Num":1,"Den":1,"Add":0},"Min":0,"Max":99,"Initial":2,"Quality":"qualified","Available":true}
          ],
          "Instruments":[
            {"Id":"p1.bombs","Label":"Bombs","Player":1,"Show":"counter","Signals":["{{{bombsId}}}"]},
            {"Id":"stage","Label":"Stage","Player":null,"Show":"level","Signals":["progress.stageId"]}
          ]}}
        """;

    private static SemanticsSnapshot Snapshot(string launchId, string bombsId = "players.1.resources.bombs")
    {
        using var doc = JsonDocument.Parse(Semantics(launchId, bombsId));
        return SignalSemantics.Parse(doc.RootElement)!;
    }

    private static MemorySignal Mem(string address, string action, int? player, long raw) =>
        new(SignalSemantics.ParseAddress(address)!.Value, action, player, raw);

    private static SignalSemanticsTracker Running(string launchId)
    {
        var tracker = new SignalSemanticsTracker();
        tracker.BeginLaunch(launchId);
        Assert.Equal(SemanticsAcceptance.Accepted, tracker.Apply(Snapshot(launchId)));
        return tracker;
    }

    [Fact]
    public void The_asyncapi_example_is_read_in_full()
    {
        var snapshot = Snapshot(LaunchA);

        Assert.True(snapshot.IsLoaded);
        Assert.Equal(LaunchA, snapshot.LaunchId);
        Assert.Equal(3, snapshot.Revision);
        Assert.Equal(5, snapshot.Signals.Count);
        Assert.Equal(2, snapshot.Instruments.Count);
        var bombs = snapshot.Signals[0];
        Assert.Equal(0xFF8305, bombs.From[0].Address);
        Assert.Null(bombs.From[0].Player);
        Assert.Equal(1, bombs.From[1].Player);
        Assert.False(snapshot.Signals[3].Available);
    }

    [Fact]
    public void Another_schema_is_not_read()
    {
        using var doc = JsonDocument.Parse("""{"Type":"deck.semantics","Payload":{"Schema":"nelfeplay.deck.semantics/2","Status":"loaded"}}""");

        Assert.Null(SignalSemantics.Parse(doc.RootElement));
    }

    [Fact]
    public void Before_any_event_the_value_is_the_initial_one()
    {
        var tracker = Running(LaunchA);

        var reading = tracker.Read("players.1.resources.bombs");
        Assert.False(reading.Observed);
        Assert.True(reading.Valid);
        Assert.Equal(3, reading.Value);
    }

    [Fact]
    public void A_line_without_player_accepts_any_player_and_a_line_with_one_only_that_player()
    {
        var tracker = Running(LaunchA);

        // BOMB_FIRED is quoted without player: the event of player 2 still counts
        Assert.Single(tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", 2, 2)));
        Assert.Equal(2, tracker.Read("players.1.resources.bombs").Value);

        // AMMO_GAIN is quoted for player 1: an event without player does not match it
        Assert.Empty(tracker.Observe(Mem("0xFF8305", "AMMO_GAIN", null, 4)));
        Assert.Equal(2, tracker.Read("players.1.resources.bombs").Value);

        var change = Assert.Single(tracker.Observe(Mem("0xFF8305", "AMMO_GAIN", 1, 3)));
        Assert.Equal(2, change.Previous.Value);
        Assert.Equal(3, change.Current.Value);
    }

    [Fact]
    public void The_last_event_of_any_line_gives_the_value()
    {
        var tracker = Running(LaunchA);

        tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 0));
        tracker.Observe(Mem("0xFF8305", "AMMO_GAIN", 1, 1));
        Assert.Equal(1, tracker.Read("players.1.resources.bombs").Value);

        tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 0));
        Assert.Equal(0, tracker.Read("players.1.resources.bombs").Value);
    }

    [Fact]
    public void Address_and_action_must_both_match()
    {
        var tracker = Running(LaunchA);

        Assert.Empty(tracker.Observe(Mem("0xFF8306", "BOMB_FIRED", null, 0)));
        Assert.Empty(tracker.Observe(Mem("0xFF8305", "LOSE_LIFE", null, 0)));
        Assert.Equal(3, tracker.Read("players.1.resources.bombs").Value);
    }

    [Fact]
    public void Enumerations_scale_and_bounds_shape_the_value()
    {
        var tracker = Running(LaunchA);

        tracker.Observe(Mem("0xFF8337", "WEAPON_STATE", 1, 255));
        var shot = tracker.Read("players.1.power.level");
        Assert.Equal(2, shot.Value);
        Assert.Equal("Boosted", shot.Label);
        Assert.True(shot.Valid);

        tracker.Observe(Mem("0xFF8337", "WEAPON_STATE", 1, 7));
        Assert.False(tracker.Read("players.1.power.level").Valid); // 7 > Max 2

        tracker.Observe(Mem("0xFF4DD4", "PROGRESSION_STAGE", null, 0));
        Assert.Equal(1, tracker.Read("progress.stageId").Value); // raw 0 + Add 1

        tracker.Observe(Mem("0xFF4DD4", "PROGRESSION_STAGE", null, 9));
        Assert.False(tracker.Read("progress.stageId").Valid); // 10 > Max 7
    }

    [Fact]
    public void An_unproven_signal_is_never_valid()
    {
        var tracker = Running(LaunchA);

        tracker.Observe(Mem("0xFF830F", "RESOURCE_LOSE", null, 3));
        var reading = tracker.Read("players.1.bomb.power");
        Assert.True(reading.Observed);
        Assert.False(reading.Valid);
    }

    [Fact]
    public void An_event_seen_before_the_semantics_still_counts()
    {
        var tracker = new SignalSemanticsTracker();
        tracker.BeginLaunch(LaunchA);
        Assert.Empty(tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 1)));

        tracker.Apply(Snapshot(LaunchA));
        Assert.Equal(1, tracker.Read("players.1.resources.bombs").Value);
    }

    [Fact]
    public void Semantics_for_another_launch_wait_for_that_launch()
    {
        var tracker = Running(LaunchA);

        Assert.Equal(SemanticsAcceptance.OtherLaunch, tracker.Apply(Snapshot(LaunchB, "players.1.bombs")));
        Assert.Equal(LaunchA, tracker.Current!.LaunchId);

        // B's semantics overtook the end of A (two streams): the end keeps them for B
        Assert.True(tracker.EndLaunch(LaunchA));
        Assert.Equal(LaunchB, tracker.Pending!.LaunchId);
        tracker.BeginLaunch(LaunchB);
        Assert.Equal(LaunchB, tracker.Current!.LaunchId);
        Assert.Contains(tracker.Signals, s => s.Id == "players.1.bombs");
    }

    [Fact]
    public void Semantics_received_before_the_start_are_kept_by_that_start()
    {
        var tracker = new SignalSemanticsTracker();
        Assert.Equal(SemanticsAcceptance.Accepted, tracker.Apply(Snapshot(LaunchA)));

        tracker.BeginLaunch(LaunchA);
        Assert.Equal(LaunchA, tracker.Current!.LaunchId);
        Assert.NotEmpty(tracker.Signals);
    }

    [Fact]
    public void A_start_of_a_new_launch_drops_the_old_semantics_and_values()
    {
        var tracker = Running(LaunchA);
        tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 0));

        tracker.BeginLaunch(LaunchB);
        Assert.Null(tracker.Current);

        tracker.Apply(Snapshot(LaunchB));
        Assert.False(tracker.Read("players.1.resources.bombs").Observed);
    }

    [Fact]
    public void A_pending_semantics_is_taken_when_its_launch_starts()
    {
        var tracker = Running(LaunchA);
        tracker.Apply(Snapshot(LaunchB));

        tracker.BeginLaunch(LaunchB);
        Assert.Equal(LaunchB, tracker.Current!.LaunchId);
        Assert.Null(tracker.Pending);
    }

    [Fact]
    public void The_end_of_another_launch_clears_nothing_and_the_right_end_clears_everything()
    {
        var tracker = Running(LaunchA);
        tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 0));

        Assert.False(tracker.EndLaunch(LaunchB));
        Assert.Equal(0, tracker.Read("players.1.resources.bombs").Value);

        Assert.True(tracker.EndLaunch(LaunchA));
        Assert.Null(tracker.Current);
        Assert.Null(tracker.RunningLaunchId);
        Assert.Empty(tracker.Signals);
    }

    [Fact]
    public void The_same_reading_twice_changes_nothing_and_a_new_revision_replaces_it()
    {
        var tracker = Running(LaunchA);

        // carried by both /ws/ingame and /ws/arcade
        Assert.Equal(SemanticsAcceptance.Unchanged, tracker.Apply(Snapshot(LaunchA)));

        var reread = Snapshot(LaunchA, "players.1.bombs") with { Revision = 4 };
        Assert.Equal(SemanticsAcceptance.Accepted, tracker.Apply(reread));
        Assert.Contains(tracker.Signals, s => s.Id == "players.1.bombs");
    }

    [Fact]
    public void A_new_reading_is_evaluated_with_the_values_already_measured()
    {
        var tracker = Running(LaunchA);
        tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 0));

        tracker.Apply(Snapshot(LaunchA, "players.1.bombs") with { Revision = 9 });
        var alerts = new ResourceAlertTracker().Observe(tracker.ReadAll(), new[] { "bombs" });

        var alert = Assert.Single(alerts);
        Assert.True(alert.Empty);
        Assert.Equal("players.1.bombs", alert.SignalId);
    }

    [Fact]
    public void An_absent_semantics_keeps_no_signal()
    {
        var tracker = new SignalSemanticsTracker();
        using var doc = JsonDocument.Parse($$$"""{"Type":"deck.semantics","Payload":{"Schema":"nelfeplay.deck.semantics/1","LaunchId":"{{{LaunchA}}}","Status":"absent","Signals":[],"Instruments":[]}}""");

        tracker.Apply(SignalSemantics.Parse(doc.RootElement)!);
        Assert.Empty(tracker.Signals);
        Assert.Empty(tracker.Observe(Mem("0xFF8305", "BOMB_FIRED", null, 0)));
    }

    [Theory]
    [InlineData("0xFF8305", 0xFF8305)]
    [InlineData("0XFF8305", 0xFF8305)]
    [InlineData("FF8305", 0xFF8305)]
    [InlineData("0x00FF8305", 0xFF8305)]
    [InlineData(" 0xfe13 ", 0xFE13)]
    public void Addresses_compare_as_numbers(string text, long expected)
    {
        Assert.Equal(expected, SignalSemantics.ParseAddress(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zz")]
    public void A_blank_or_foreign_address_is_none(string? text)
    {
        Assert.Null(SignalSemantics.ParseAddress(text));
    }

    [Fact]
    public void A_retroarch_wrapper_event_is_read()
    {
        // retroarch.memory.changed as published by RetroArchWrapperProvider
        using var doc = JsonDocument.Parse("""
            {"Type":"retroarch.memory.changed","Payload":{"Source":"retroarch.wrapper.pipe","SystemId":"fbneo","Rom":"19xx",
             "signal":{"Key":"ACTION.BOMB_FIRED","Channel":"ACTION","Name":"BOMB_FIRED","Address":"0xFF8305","RawValueHex":"0x02","Value":2,"Player":1}}}
            """);

        var memory = SignalSemantics.ReadMemory(doc.RootElement);
        Assert.Equal(new MemorySignal(0xFF8305, "BOMB_FIRED", 1, 2), memory);
    }

    [Fact]
    public void A_mame_lua_event_takes_its_player_from_the_payload()
    {
        // ingame.memory.changed as published by MameLuaIngameProvider (no Player in signal)
        using var doc = JsonDocument.Parse("""
            {"Type":"ingame.memory.changed","Payload":{"signal":{"Channel":"ACTION","Name":"LOSE_LIFE","Address":"0xFF8300","Value":0},
             "actionType":"LOSE_LIFE","Value":0,"player":2}}
            """);

        Assert.Equal(new MemorySignal(0xFF8300, "LOSE_LIFE", 2, 0), SignalSemantics.ReadMemory(doc.RootElement));
    }

    [Fact]
    public void Semantics_and_memory_events_are_recognised()
    {
        Assert.True(SignalSemantics.IsSemanticsEvent(LedEvent.FromJson(Semantics(LaunchA))));
        Assert.True(SignalSemantics.IsMemoryEvent(LedEvent.FromJson("""{"Type":"retroarch.memory.changed","Payload":{"signal":{"Name":"X"}}}""")));
        Assert.True(SignalSemantics.IsMemoryEvent(LedEvent.FromJson("""{"Type":"ingame.memory.changed","Payload":{"signal":{"Name":"X"}}}""")));
        Assert.False(SignalSemantics.IsMemoryEvent(LedEvent.FromJson("""{"Type":"ingame.action","Payload":{"signal":{"Name":"X"}}}""")));
    }
}
