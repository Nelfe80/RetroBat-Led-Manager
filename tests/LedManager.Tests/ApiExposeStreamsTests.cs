using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

public class ApiExposeStreamsTests
{
    private static readonly Dictionary<string, string> DefaultPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scoring"] = "/ws/scoring",
        ["multiplayer"] = "/ws/multiplayer"
    };

    // GET /api/v1/ws/streams of APIExpose 1.9.185 (ASP.NET camelCase), shortened.
    private const string Catalog1185 = """
        {"firehoseUrl":"ws://127.0.0.1:12345/ws","firehoseNote":"...","streams":[
          {"name":"arcade","url":"ws://127.0.0.1:12345/ws/arcade","eventTypePrefixes":["arcade."],"aliases":[],"retainedEventTypes":[]},
          {"name":"frontend","url":"ws://127.0.0.1:12345/ws/frontend","eventTypePrefixes":["ui."],"aliases":[],"retainedEventTypes":["ui.game.selected"]},
          {"name":"multiplayer","url":"ws://127.0.0.1:12345/ws/multiplayer","eventTypePrefixes":["multiplayer.","netplay."],"aliases":[],"retainedEventTypes":["multiplayer.state"]},
          {"name":"scoring","url":"ws://127.0.0.1:12345/ws/scoring","eventTypePrefixes":["scoring."],"aliases":[],"retainedEventTypes":[]}
        ]}
        """;

    // An API older than 1.9.185: the catalog exists but lists neither stream.
    private const string Catalog1183 = """
        {"firehoseUrl":"ws://127.0.0.1:12345/ws","streams":[{"name":"frontend"},{"name":"ingame"},{"name":"panel"}]}
        """;

    [Fact]
    public void Both_streams_are_opened_when_the_api_announces_them()
    {
        var answer = ApiExposeStreams.Interpret(200, Catalog1185);

        Assert.True(answer.Final);
        var selected = ApiExposeStreams.SelectOptional(answer.Announced, DefaultPaths);
        Assert.Equal(new[] { ("scoring", "/ws/scoring"), ("multiplayer", "/ws/multiplayer") }, selected);
    }

    [Fact]
    public void Nothing_more_is_opened_on_an_older_api()
    {
        var answer = ApiExposeStreams.Interpret(200, Catalog1183);

        Assert.True(answer.Final);
        Assert.Empty(ApiExposeStreams.SelectOptional(answer.Announced, DefaultPaths));
    }

    [Fact]
    public void A_missing_catalog_route_is_a_final_no()
    {
        var answer = ApiExposeStreams.Interpret(404, null);

        Assert.True(answer.Final);
        Assert.Empty(ApiExposeStreams.SelectOptional(answer.Announced, DefaultPaths));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(503)]
    public void Other_failures_ask_again(int status)
    {
        Assert.False(ApiExposeStreams.Interpret(status, null).Final);
    }

    [Fact]
    public void An_empty_path_turns_a_stream_off()
    {
        var announced = ApiExposeStreams.ParseAnnounced(Catalog1185);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scoring"] = "",
            ["multiplayer"] = "/ws/multiplayer"
        };

        Assert.Equal(new[] { ("multiplayer", "/ws/multiplayer") }, ApiExposeStreams.SelectOptional(announced, paths));
    }

    [Theory]
    [InlineData("""{"Streams":[{"Name":"Scoring"}]}""")]
    [InlineData("""[{"name":"scoring"}]""")]
    [InlineData("""["scoring"]""")]
    public void Pascal_case_and_bare_arrays_are_read(string json)
    {
        Assert.Contains("scoring", ApiExposeStreams.ParseAnnounced(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"streams":"scoring"}""")]
    public void An_unreadable_answer_announces_nothing(string json)
    {
        Assert.Empty(ApiExposeStreams.ParseAnnounced(json));
    }

    [Theory]
    [InlineData("ws://127.0.0.1:12345", "http://127.0.0.1:12345/api/v1/ws/streams")]
    [InlineData("ws://127.0.0.1:12345/", "http://127.0.0.1:12345/api/v1/ws/streams")]
    [InlineData("wss://cabinet.local:4443", "https://cabinet.local:4443/api/v1/ws/streams")]
    [InlineData("http://10.0.0.5:12345", "http://10.0.0.5:12345/api/v1/ws/streams")]
    public void The_catalog_is_asked_over_http_on_the_same_host(string baseUrl, string expected)
    {
        var root = ApiExposeLink.HttpBaseUri(baseUrl);

        Assert.NotNull(root);
        Assert.Equal(expected, new Uri(root, ApiExposeStreams.CatalogPath).ToString());
    }

    [Fact]
    public void An_unknown_scheme_gives_no_http_root()
    {
        Assert.Null(ApiExposeLink.HttpBaseUri("ftp://127.0.0.1:12345"));
    }
}
