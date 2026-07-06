using System.Diagnostics;

namespace LedManager.Setup.Serial;

/// <summary>
/// Detects and stops the running LedManager so the wizard can take the COM port.
/// LedManager and the wizard cannot hold the same port at once.
/// </summary>
public static class LedManagerProcess
{
    private static readonly string[] Names = { "LedManager", "PicoCommandSender" };

    public static bool IsRunning()
    {
        return Names.Any(name => Process.GetProcessesByName(name).Length > 0);
    }

    public static void StopAll()
    {
        foreach (var name in Names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
                catch
                {
                    // best effort: a lingering handle is released by the OS shortly after
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        // Give Windows a moment to release the COM handle before we reopen it.
        Thread.Sleep(400);
    }
}
