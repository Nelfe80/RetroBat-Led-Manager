namespace LedManager.Core.Runtime;

/// <summary>
/// When a dead PicoCommandSender may be started again.
///
/// A sender that cannot open its serial port (Pico unplugged, port taken) dies within a
/// second. Restarting it on the next command meant a crash every few seconds for as long
/// as the board stayed unplugged: hundreds of Windows Error Reports a day, audio crackle
/// and stutter on the cabinet. A sender that dies young now waits longer each time
/// (5 s, 30 s, 2 min, then every 10 min); one that ran for a while, or that reached READY,
/// is a working board that dropped once, and comes back at once.
/// Pure: the caller gives the times, the policy answers.
/// </summary>
public sealed class SenderRestartPolicy
{
    /// <summary>A sender that lived less than this before dying never really started.</summary>
    public static readonly TimeSpan ShortLife = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan[] Steps =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10)
    };

    private DateTime _notBefore = DateTime.MinValue;

    /// <summary>Early deaths in a row (0: the last run was healthy).</summary>
    public int Failures { get; private set; }

    /// <summary>The wait applied after the last death.</summary>
    public TimeSpan CurrentDelay { get; private set; }

    /// <summary>
    /// The sender died. <paramref name="lived"/>: how long it ran; <paramref name="reachedReady"/>:
    /// it opened its board. Returns true when this death starts or lengthens a waiting streak,
    /// the only moment worth a log line.
    /// </summary>
    public bool OnExit(DateTime now, TimeSpan lived, bool reachedReady)
    {
        if (reachedReady || lived >= ShortLife)
        {
            Failures = 0;
            CurrentDelay = TimeSpan.Zero;
            _notBefore = now;
            return false;
        }

        var before = CurrentDelay;
        Failures++;
        CurrentDelay = Steps[Math.Min(Failures, Steps.Length) - 1];
        _notBefore = now + CurrentDelay;
        return CurrentDelay != before;
    }

    /// <summary>May the sender be started now?</summary>
    public bool CanStart(DateTime now) => now >= _notBefore;

    /// <summary>The board answered (READY): the next death starts from scratch.</summary>
    public void OnReady()
    {
        Failures = 0;
        CurrentDelay = TimeSpan.Zero;
    }
}
