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

    private PicoSenderHost(Process process)
    {
        _process = process;
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

            // Drain output so the pipe never blocks; the wizard does not need replies.
            _ = Task.Run(async () =>
            {
                try { while (await process.StandardOutput.ReadLineAsync() is not null) { } } catch { }
            });
            _ = Task.Run(async () =>
            {
                try { while (await process.StandardError.ReadLineAsync() is not null) { } } catch { }
            });

            return new PicoSenderHost(process);
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
