using System.IO;
using System.IO.Ports;
using System.Text;

namespace LedManager.Setup.Serial;

/// <summary>
/// Installs the panel firmware (fw\*.py) on the Pico over the MicroPython raw REPL —
/// the C# port of tools\deploy-pico-fw.ps1, so the user needs no PowerShell and no
/// technical knowledge: detection failed → one button. Safe-boot first (main.py
/// renamed + reset) so a crashing/old firmware never blocks the upload, then each
/// file is written in base64 chunks and size-verified.
/// A blank Pico (no MicroPython) exposes no serial port: that case goes through
/// BOOTSEL (RPI-RP2 drive) and needs the official MicroPython UF2 once.
/// </summary>
public static class FirmwareInstaller
{
    public sealed record Result(bool Success, string Message);

    private static readonly string[] Files = { "main.py", "hardware_profiles.py", "profiles_db.py" };

    /// <summary>The RPI-RP2 drive appears when the Pico is plugged in with BOOTSEL held.</summary>
    public static string? FindBootselDrive()
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.VolumeLabel.Equals("RPI-RP2", StringComparison.OrdinalIgnoreCase))
                {
                    return drive.RootDirectory.FullName;
                }
            }
        }
        catch
        {
            // drive enumeration is best effort
        }

        return null;
    }

    /// <summary>A MicroPython UF2 shipped in fw\, if the user dropped one there.</summary>
    public static string? FindLocalUf2(string pluginRoot)
    {
        var fw = Path.Combine(pluginRoot, "fw");
        return Directory.Exists(fw) ? Directory.GetFiles(fw, "*.uf2").FirstOrDefault() : null;
    }

    public static Result CopyUf2ToBootsel(string uf2Path, string driveRoot)
    {
        try
        {
            File.Copy(uf2Path, Path.Combine(driveRoot, Path.GetFileName(uf2Path)), overwrite: true);
            return new Result(true, "");
        }
        catch (Exception ex)
        {
            return new Result(false, ex.Message);
        }
    }

    public static async Task<Result> InstallAsync(string pluginRoot, string? preferredPort, IProgress<string> progress)
    {
        var fwDir = Path.Combine(pluginRoot, "fw");
        foreach (var file in Files)
        {
            if (!File.Exists(Path.Combine(fwDir, file)))
            {
                return new Result(false, Localization.L.T(
                    $"Fichier firmware manquant : fw\\{file}.", $"Missing firmware file: fw\\{file}."));
            }
        }

        return await Task.Run(() => InstallCore(fwDir, preferredPort, progress)).ConfigureAwait(false);
    }

    private static Result InstallCore(string fwDir, string? preferredPort, IProgress<string> progress)
    {
        var ports = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(preferredPort) && ports.Remove(preferredPort))
        {
            ports.Insert(0, preferredPort);
        }

        if (ports.Count == 0)
        {
            return new Result(false, Localization.L.T(
                "Aucun port série : si le Pico est neuf (sans MicroPython), branchez-le en maintenant BOOTSEL.",
                "No serial port: if the Pico is blank (no MicroPython), plug it in while holding BOOTSEL."));
        }

        Exception? last = null;
        foreach (var port in ports)
        {
            try
            {
                progress.Report(Localization.L.T($"Connexion à {port}…", $"Connecting to {port}…"));
                InstallOnPort(port, fwDir, progress);
                return new Result(true, Localization.L.T(
                    $"Firmware installé sur {port} ({string.Join(", ", Files)}). Le Pico a redémarré.",
                    $"Firmware installed on {port} ({string.Join(", ", Files)}). The Pico rebooted."));
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        return new Result(false, Localization.L.T(
            $"Installation impossible ({last?.Message}). Si le Pico est neuf, branchez-le en maintenant BOOTSEL "
            + "pour y déposer MicroPython d'abord.",
            $"Install failed ({last?.Message}). If the Pico is blank, plug it in while holding BOOTSEL "
            + "to drop MicroPython on it first."));
    }

    private static void InstallOnPort(string portName, string fwDir, IProgress<string> progress)
    {
        using (var serial = OpenPort(portName))
        {
            EnterRawRepl(serial);

            // safe boot: a running main.py (watchdog, LED loops) would fight the upload
            progress.Report(Localization.L.T("Désactivation temporaire de l'ancien firmware…",
                "Temporarily disabling the old firmware…"));
            TryRawExecNoReply(serial,
                "import os, machine\ntry:\n os.rename('main.py','main.py.deploybak')\nexcept OSError:\n pass\nmachine.reset()");
        }

        Thread.Sleep(2600);

        using var session = OpenPort(portName);
        EnterRawRepl(session);

        foreach (var file in Files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(fwDir, file));
            progress.Report(Localization.L.T($"Copie de {file} ({bytes.Length} octets)…",
                $"Copying {file} ({bytes.Length} bytes)…"));
            RawExec(session, $"f=open('{file}','wb')\nf.close()");

            const int chunkSize = 384;
            for (var offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                var length = Math.Min(chunkSize, bytes.Length - offset);
                var payload = Convert.ToBase64String(bytes, offset, length);
                RawExec(session,
                    $"import ubinascii\nf=open('{file}','ab')\nf.write(ubinascii.a2b_base64('{payload}'))\nf.close()",
                    timeoutMs: 7000);
            }

            var stat = RawExec(session, $"import os\nprint(os.stat('{file}')[6])");
            if (!stat.Contains(bytes.Length.ToString()))
            {
                throw new InvalidOperationException($"size check failed for {file}");
            }
        }

        // cleanup the safe-boot backup, then boot the fresh firmware
        TryRawExecNoReply(session, "import os, machine\ntry:\n os.remove('main.py.deploybak')\nexcept OSError:\n pass\nmachine.reset()");
    }

    private static SerialPort OpenPort(string portName)
    {
        var serial = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 250,
            WriteTimeout = 5000,
            DtrEnable = true,
            RtsEnable = true,
            NewLine = "\n"
        };
        serial.Open();
        Thread.Sleep(500);
        return serial;
    }

    private static void EnterRawRepl(SerialPort serial)
    {
        serial.DiscardInBuffer();
        WriteBytes(serial, new byte[] { 3, 3 }); // Ctrl-C twice: interrupt any loop
        Thread.Sleep(250);
        ReadUntil(serial, ">", 700);

        WriteBytes(serial, new byte[] { 1 }); // Ctrl-A: raw REPL
        var reply = ReadUntil(serial, "raw REPL", 2500);
        if (!reply.Contains("raw REPL"))
        {
            throw new InvalidOperationException("MicroPython raw REPL not reachable");
        }
    }

    private static string RawExec(SerialPort serial, string code, int timeoutMs = 5000)
    {
        serial.DiscardInBuffer();
        WriteText(serial, code);
        WriteBytes(serial, new byte[] { 4 }); // Ctrl-D: execute

        var reply = ReadUntil(serial, "\x04", timeoutMs);
        if (!reply.Contains("OK"))
        {
            throw new InvalidOperationException($"raw exec not accepted: {reply}");
        }

        if (reply.Contains("Traceback") || reply.Contains("Error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"raw exec failed: {reply}");
        }

        return reply;
    }

    private static void TryRawExecNoReply(SerialPort serial, string code)
    {
        try
        {
            serial.DiscardInBuffer();
            WriteText(serial, code);
            WriteBytes(serial, new byte[] { 4 });
            Thread.Sleep(800); // the reset drops the port; no reply expected
        }
        catch
        {
            // resetting closes the link mid-write: expected
        }
    }

    private static string ReadUntil(SerialPort serial, string needle, int timeoutMs)
    {
        var buffer = new StringBuilder();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var chunk = serial.ReadExisting();
                if (chunk.Length > 0)
                {
                    buffer.Append(chunk);
                    if (buffer.ToString().Contains(needle))
                    {
                        break;
                    }
                }
            }
            catch
            {
                // transient read errors while the device settles
            }

            Thread.Sleep(20);
        }

        return buffer.ToString();
    }

    private static void WriteText(SerialPort serial, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        serial.BaseStream.Write(bytes, 0, bytes.Length);
        serial.BaseStream.Flush();
    }

    private static void WriteBytes(SerialPort serial, byte[] bytes)
    {
        serial.BaseStream.Write(bytes, 0, bytes.Length);
        serial.BaseStream.Flush();
    }
}
