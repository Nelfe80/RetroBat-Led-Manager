using System.Diagnostics;
using System.IO;

namespace LedManager.Setup.Serial;

/// <summary>
/// Runs PicoCommandSender.exe as the wizard's LED driver: it already reads
/// PicoCommandSender.ini, initializes the Pico GPIO profile and translates generic
/// commands (SLOT/SET/ALL…) — so the wizard reuses the exact runtime pipeline
/// instead of re-implementing the firmware protocol. LedManager must be stopped
/// first (both cannot share the COM port).
/// </summary>
public sealed class PicoSenderHost : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PicoSenderHost(Process process)
    {
        _process = process;
    }

    /// <summary>
    /// Completes when the sender prints "READY sender=…" — i.e. the firmware GPIO
    /// profile is initialized and commands will actually light LEDs. Replaces a
    /// blind delay (the ini StartupDelayMs can be many seconds). Always await with
    /// a safety timeout in case the sender never reports ready.
    /// </summary>
    public Task WaitForReadyAsync(TimeSpan timeout)
    {
        return Task.WhenAny(_ready.Task, Task.Delay(timeout));
    }

    public static PicoSenderHost? Start(string pluginRoot, string sender = "P1")
    {
        var exe = Path.Combine(pluginRoot, "PicoCommandSender.exe");
        if (!File.Exists(exe))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = pluginRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("daemon");
            psi.ArgumentList.Add("--ini");
            psi.ArgumentList.Add("PicoCommandSender.ini");
            psi.ArgumentList.Add("--sender");
            psi.ArgumentList.Add(sender);

            var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var host = new PicoSenderHost(process);

            // Drain output so the pipe never blocks, and detect the READY signal.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardOutput.ReadLineAsync() is { } line)
                    {
                        if (line.Contains("READY sender=", StringComparison.OrdinalIgnoreCase))
                        {
                            host._ready.TrySetResult();
                        }
                    }
                }
                catch { }
            });
            _ = Task.Run(async () =>
            {
                try { while (await process.StandardError.ReadLineAsync() is not null) { } } catch { }
            });

            return host;
        }
        catch
        {
            return null;
        }
    }

    public bool IsAlive => !_process.HasExited;

    public void Send(string command)
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine(command);
                _process.StandardInput.Flush();
            }
        }
        catch
        {
            // surfaced by IsAlive on the next poll
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Close(); } catch { }
                if (!_process.WaitForExit(1500))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            _process.Dispose();
        }
    }
}
