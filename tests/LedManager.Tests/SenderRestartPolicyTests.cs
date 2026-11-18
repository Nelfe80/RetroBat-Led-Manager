using LedManager.Core.Runtime;
using Xunit;

namespace LedManager.Tests;

/// <summary>
/// A Pico unplugged: the sender dies at once, and must not be restarted in a tight loop
/// (480 crash reports in 7 days on a cabinet, 18/11/2026).
/// </summary>
public class SenderRestartPolicyTests
{
    private static readonly DateTime T0 = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Early_deaths_wait_longer_each_time_up_to_ten_minutes()
    {
        var p = new SenderRestartPolicy();
        var now = T0;
        var expected = new[] { 5, 30, 120, 600, 600, 600 };
        foreach (var seconds in expected)
        {
            p.OnExit(now, TimeSpan.FromMilliseconds(400), reachedReady: false);
            Assert.Equal(TimeSpan.FromSeconds(seconds), p.CurrentDelay);
            Assert.False(p.CanStart(now + TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1)));
            now += TimeSpan.FromSeconds(seconds);
            Assert.True(p.CanStart(now));
        }
    }

    [Fact]
    public void Only_a_longer_wait_is_worth_a_log_line()
    {
        var p = new SenderRestartPolicy();
        var lines = 0;
        for (var i = 0; i < 20; i++)
            if (p.OnExit(T0.AddMinutes(i * 10), TimeSpan.FromSeconds(1), false)) lines++;
        Assert.Equal(4, lines); // 5 s, 30 s, 2 min, 10 min: then silence
    }

    [Fact]
    public void A_board_that_worked_comes_back_at_once()
    {
        var p = new SenderRestartPolicy();
        p.OnExit(T0, TimeSpan.FromSeconds(1), false);
        p.OnExit(T0.AddSeconds(5), TimeSpan.FromSeconds(1), false);
        Assert.Equal(2, p.Failures);

        Assert.False(p.OnExit(T0.AddMinutes(5), TimeSpan.FromSeconds(1), reachedReady: true));
        Assert.Equal(0, p.Failures);
        Assert.True(p.CanStart(T0.AddMinutes(5)));

        p.OnExit(T0.AddHours(1), TimeSpan.FromHours(1), reachedReady: false);
        Assert.Equal(0, p.Failures);
        Assert.True(p.CanStart(T0.AddHours(1)));
    }

    [Fact]
    public void Ready_resets_the_streak()
    {
        var p = new SenderRestartPolicy();
        p.OnExit(T0, TimeSpan.FromSeconds(1), false);
        p.OnExit(T0.AddSeconds(5), TimeSpan.FromSeconds(1), false);
        p.OnReady();
        p.OnExit(T0.AddMinutes(1), TimeSpan.FromSeconds(1), false);
        Assert.Equal(TimeSpan.FromSeconds(5), p.CurrentDelay);
    }

    [Fact]
    public void A_fresh_policy_starts_at_once()
        => Assert.True(new SenderRestartPolicy().CanStart(T0));
}
