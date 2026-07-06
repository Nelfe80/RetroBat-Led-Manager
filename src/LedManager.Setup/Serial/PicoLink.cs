using System.IO.Ports;

namespace LedManager.Setup.Serial;

/// <summary>
/// Direct serial link to the Pico for the setup wizard. Used only while LedManager
/// is stopped (both cannot hold the same COM port). System.IO.Ports is a compiled
/// managed API — unlike the runtime's old PowerShell bridge it does not trip
/// antivirus ClickFix heuristics.
/// </summary>
public sealed class PicoLink : IDisposable
{
    private readonly SerialPort _port;
    private readonly object _ioLock = new();

    public string PortName { get; }
    public string? FirmwareName { get; private set; }
    public string? FirmwareVersion { get; private set; }

    private PicoLink(SerialPort port, string portName)
    {
        _port = port;
        PortName = portName;
    }

    /// <summary>
    /// Opens a port and confirms a Pico firmware answers (PING → PONG, then VERSION).
    /// Returns null if the port is busy or nothing recognizable answers.
    /// </summary>
    public static PicoLink? TryOpen(string portName, int baudRate = 115200, int bootDelayMs = 800)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 400,
                WriteTimeout = 2000,
                NewLine = "\n",
                DtrEnable = false,
                RtsEnable = false
            };
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            Thread.Sleep(bootDelayMs);

            var link = new PicoLink(port, portName);
            if (!link.Handshake())
            {
                link.Dispose();
                return null;
            }

            return link;
        }
        catch
        {
            port?.Dispose();
            return null;
        }
    }

    private bool Handshake()
    {
        // PING then VERSION; accept either PONG or a VERSION/DYNAMIC PANEL banner.
        var ping = Exchange("PING", TimeSpan.FromMilliseconds(600));
        var version = Exchange("VERSION", TimeSpan.FromMilliseconds(600));

        var banner = version ?? ping ?? "";
        if (banner.Contains("VERSION", StringComparison.OrdinalIgnoreCase))
        {
            var parts = banner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                FirmwareName = parts[1];
                FirmwareVersion = parts[2];
            }
        }

        return (ping?.Contains("PONG", StringComparison.OrdinalIgnoreCase) ?? false)
               || banner.Contains("VERSION", StringComparison.OrdinalIgnoreCase)
               || banner.Contains("DYNAMIC", StringComparison.OrdinalIgnoreCase)
               || banner.Contains("READY", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sends a command and returns the first non-empty reply line within the timeout, if any.</summary>
    public string? Exchange(string command, TimeSpan timeout)
    {
        lock (_ioLock)
        {
            try
            {
                _port.DiscardInBuffer();
                _port.WriteLine(command);
            }
            catch
            {
                return null;
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var line = _port.ReadLine().Trim();
                    if (line.Length > 0)
                    {
                        return line;
                    }
                }
                catch (TimeoutException)
                {
                    // keep polling until the caller's deadline
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }

    /// <summary>Fire-and-forget command (SLOT/SET/ALL…), no reply expected.</summary>
    public void Send(string command)
    {
        lock (_ioLock)
        {
            try
            {
                _port.WriteLine(command);
            }
            catch
            {
                // wizard surfaces link loss via the next Exchange
            }
        }
    }

    public void Dispose()
    {
        lock (_ioLock)
        {
            try
            {
                if (_port.IsOpen)
                {
                    _port.Close();
                }
            }
            catch
            {
                // already closed
            }

            _port.Dispose();
        }
    }
}
