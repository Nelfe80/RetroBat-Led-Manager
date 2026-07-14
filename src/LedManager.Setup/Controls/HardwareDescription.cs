using System.IO;
using LedManager.Core.Ini;

namespace LedManager.Setup.Controls;

/// <summary>
/// A configurable Pico as the user sees it: its sender id (P1, P2…) plus the serial
/// port it answers on. The label is the identity shown everywhere: "P1 · COM5".
/// </summary>
public sealed record PicoIdentity(string SenderId, string Port, int Player)
{
    public string Label => $"{SenderId} · {Port}";
}

/// <summary>
/// Reads the panel hardware description from the plugin ini files. Data-driven:
/// the reference kit is one profile among others (WS2812, matrices, other boards…).
/// One instance describes ONE Pico (sender); the setup can list and switch senders.
/// </summary>
public sealed record HardwareDescription(
    string SenderId,
    int ButtonCount,
    bool HasStart,
    bool HasSelect,
    int MirrorPort,
    string SerialPort,
    int BaudRate)
{
    public string PicoLabel => $"{SenderId} · {SerialPort}";

    public static string? FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LedManager.ini")))
            {
                return dir.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// All player Picos declared in PicoCommandSender.ini ([Identity:*] with
    /// Role=player), in player order. Always contains at least P1.
    /// </summary>
    public static IReadOnlyList<PicoIdentity> ListPicos(string? pluginRoot = null)
    {
        var picos = new List<PicoIdentity>();
        var root = pluginRoot ?? FindPluginRoot();
        var senderIni = root is null ? null : Path.Combine(root, "PicoCommandSender.ini");
        if (senderIni is not null && File.Exists(senderIni))
        {
            var ini = IniDocument.Load(senderIni);
            foreach (var section in ini.Sections)
            {
                if (!section.StartsWith("Identity:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var senderId = ini.Get(section, "SenderId", section["Identity:".Length..]);
                var role = ini.Get(section, "Role", "player");
                if (!role.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var port = ini.Get($"Serial:{senderId}", "Port", "auto");
                var player = ini.GetInt(section, "Player", picos.Count + 1);
                picos.Add(new PicoIdentity(senderId, port, player));
            }
        }

        if (picos.Count == 0)
        {
            picos.Add(new PicoIdentity("P1", "COM3", 1));
        }

        return picos.OrderBy(p => p.Player).ToList();
    }

    public static HardwareDescription Load(string? pluginRoot = null, string senderId = "P1")
    {
        var buttonCount = 8;
        var hasStart = true;
        var hasSelect = true;
        var mirrorPort = 12377;
        var serialPort = "COM3";
        var baudRate = 115200;

        var root = pluginRoot ?? FindPluginRoot();
        if (root is null)
        {
            return new HardwareDescription(senderId, buttonCount, hasStart, hasSelect, mirrorPort, serialPort, baudRate);
        }

        var senderIni = Path.Combine(root, "PicoCommandSender.ini");
        if (File.Exists(senderIni))
        {
            var ini = IniDocument.Load(senderIni);
            buttonCount = Math.Clamp(ini.GetInt($"Hardware:{senderId}", "PanelButtons", 8), 1, 16);
            hasStart = !string.Equals(ini.Get($"Hardware:{senderId}", "Start", "LED"), "NONE", StringComparison.OrdinalIgnoreCase);
            hasSelect = !string.Equals(ini.Get($"Hardware:{senderId}", "Select", "LED"), "NONE", StringComparison.OrdinalIgnoreCase);
            serialPort = ini.Get($"Serial:{senderId}", "Port", "COM3");
            baudRate = ini.GetInt($"Serial:{senderId}", "BaudRate", 115200);
        }

        var managerIni = Path.Combine(root, "LedManager.ini");
        if (File.Exists(managerIni))
        {
            var ini = IniDocument.Load(managerIni);
            mirrorPort = ini.GetInt("VirtualPanel", "Port", 12377);
        }

        return new HardwareDescription(senderId, buttonCount, hasStart, hasSelect, mirrorPort, serialPort, baudRate);
    }
}
