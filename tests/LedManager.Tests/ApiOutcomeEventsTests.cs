using LedManager.Core.Configuration;
using LedManager.Core.Effects;
using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class ApiOutcomeEventsTests
{
    private static LedEvent Parse(string json) => LedEvent.FromJson(json);

    private static string Verdict(string status) => $$$"""
        {"Type":"scoring.verdict","Ts":"2026-11-15T20:00:00Z","NodeId":"cab-01","CorrelationId":"c",
         "Payload":{"SessionId":"s1","RomGroup":"19xx","SystemId":"fbneo","Ruleset":"1cc","Status":"{{{status}}}",
                    "Score":3000000000,"Rank":4,"Continues":0,"DurationMs":1800000}}
        """;

    [Theory]
    [InlineData("published", ApiOutcomeEvents.ScoringPublished)]
    [InlineData("held", ApiOutcomeEvents.ScoringHeld)]
    [InlineData("quarantined", ApiOutcomeEvents.ScoringQuarantined)]
    [InlineData("rejected", ApiOutcomeEvents.ScoringRejected)]
    [InlineData("PUBLISHED", ApiOutcomeEvents.ScoringPublished)]
    public void Each_verdict_status_names_its_moment(string status, string action)
    {
        var evt = Parse(Verdict(status));

        Assert.True(ApiOutcomeEvents.IsOutcomeEvent(evt));
        var outcome = ApiOutcomeEvents.Read(evt);
        Assert.Equal(action, outcome.Action);
        Assert.Contains("score=3000000000", outcome.Summary);
        Assert.Contains("game=fbneo/19xx", outcome.Summary);
    }

    [Fact]
    public void An_unknown_status_plays_nothing()
    {
        Assert.Null(ApiOutcomeEvents.Read(Parse(Verdict("duplicate"))).Action);
    }

    [Fact]
    public void A_match_start_and_end_name_their_moments()
    {
        var started = Parse("""{"Type":"multiplayer.match.started","Payload":{"Room":"r42","Invites":1,"Utc":"2026-11-15T20:00:00Z"}}""");
        var ended = Parse("""{"Type":"multiplayer.match.ended","Payload":{"Room":"r42","Issue":"p1","Coop":false,"FermetureS":8,"Utc":"2026-11-15T20:05:00Z"}}""");

        Assert.Equal(ApiOutcomeEvents.MatchStarted, ApiOutcomeEvents.Read(started).Action);
        var end = ApiOutcomeEvents.Read(ended);
        Assert.Equal(ApiOutcomeEvents.MatchEnded, end.Action);
        Assert.Contains("outcome=p1", end.Summary);
    }

    [Theory]
    [InlineData("""{"Type":"multiplayer.state","Payload":{"rooms":[{"room":"r1"},{"room":"r2"}],"invitations":[],"error":null}}""")]
    [InlineData("""{"Type":"multiplayer.host.started","Payload":{"Room":"r1"}}""")]
    [InlineData("""{"Type":"netplay.guest.host_left","Payload":{}}""")]
    [InlineData("""{"Type":"scoring.partie.depart","Payload":{"Source":"es"}}""")]
    [InlineData("""{"Type":"scoring.run.ended","Payload":{}}""")]
    public void State_messages_play_nothing(string json)
    {
        var evt = Parse(json);

        Assert.True(ApiOutcomeEvents.IsOutcomeEvent(evt));
        Assert.Null(ApiOutcomeEvents.Read(evt).Action);
    }

    [Fact]
    public void The_multiplayer_state_counts_its_rooms()
    {
        var outcome = ApiOutcomeEvents.Read(Parse("""{"Type":"multiplayer.state","Payload":{"rooms":[{"room":"r1"},{"room":"r2"}]}}"""));

        Assert.Equal("state rooms=2", outcome.Summary);
    }

    [Fact]
    public void Other_streams_are_not_outcomes()
    {
        Assert.False(ApiOutcomeEvents.IsOutcomeEvent(Parse("""{"Type":"ui.game.started","Payload":{"LaunchId":"a"}}""")));
        Assert.False(ApiOutcomeEvents.IsOutcomeEvent(Parse("""{"Type":"retroarch.memory.changed","Payload":{"signal":{"Name":"BOMB_FIRED"}}}""")));
    }

    [Fact]
    public void The_shipped_catalog_names_every_moment_and_plays_it()
    {
        var catalog = DefaultEffectCatalog.Load(Path.Combine(AppContext.BaseDirectory, "default.mem.effects.json"), HardwareConfig.AllEnabled);
        var source = Parse(Verdict("published"));

        foreach (var action in ApiOutcomeEvents.Actions)
        {
            Assert.True(catalog.HasExactActionRule(action), action);
            var effect = ApiOutcomeEvents.ToEffectEvent(source, new ApiOutcome("scoring.verdict", action, "", "fbneo", "19xx"), defaultPlayer: 1);
            var steps = catalog.Resolve(effect);
            Assert.NotEmpty(steps);
            // flashes the board restores by itself: safe in ES as in game
            Assert.All(steps, step => Assert.Equal("flash", step.Action));
            Assert.All(steps, step => Assert.Equal(1, step.Player));
        }
    }

    [Fact]
    public void A_catalog_without_the_rule_is_not_borrowed_from_the_wildcard()
    {
        Assert.False(DefaultEffectCatalog.Empty.HasExactActionRule(ApiOutcomeEvents.ScoringPublished));
        var catalog = DefaultEffectCatalog.Load(Path.Combine(AppContext.BaseDirectory, "default.mem.effects.json"), HardwareConfig.AllEnabled);
        // the "*" fallback exists in the shipped catalog: it must not count as a rule
        Assert.False(catalog.HasExactActionRule("SCORING_SOMETHING_ELSE"));
    }
}
