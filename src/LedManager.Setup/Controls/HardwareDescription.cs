using System.IO;
using LedManager.Core.Ini;

namespace LedManager.Setup.Controls;

/// <summary>
/// Reads the panel hardware description from the plugin ini files. Data-driven:
/// the reference kit is one profile among others (WS2812, matrices, other boards…).
/// </summary>
public sealed record HardwareDescription(
    int ButtonCount,
    bool HasStart,
    bool HasSelect,
    int MirrorPort,
    string SerialPort,
    int BaudRate)
{
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

    public static HardwareDescription Load(string? pluginRoot = null)
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
            return new HardwareDescription(buttonCount, hasStart, hasSelect, mirrorPort, serialPort, baudRate);
        }

        var senderIni = Path.Combine(root, "PicoCommandSender.ini");
        if (File.Exists(senderIni))
        {
            var ini = IniDocument.Load(senderIni);
            buttonCount = Math.Clamp(ini.GetInt("Hardware:P1", "PanelButtons", 8), 1, 16);
            hasStart = !string.Equals(ini.Get("Hardware:P1", "Start", "LED"), "NONE", StringComparison.OrdinalIgnoreCase);
            hasSelect = !string.Equals(ini.Get("Hardware:P1", "Select", "LED"), "NONE", StringComparison.OrdinalIgnoreCase);
            serialPort = ini.Get("Serial:P1", "Port", "COM3");
            baudRate = ini.GetInt("Serial:P1", "BaudRate", 115200);
        }

        var managerIni = Path.Combine(root, "LedManager.ini");
        if (File.Exists(managerIni))
        {
            var ini = IniDocument.Load(managerIni);
            mirrorPort = ini.GetInt("VirtualPanel", "Port", 12377);
        }

        return new HardwareDescription(buttonCount, hasStart, hasSelect, mirrorPort, serialPort, baudRate);
    }
}
