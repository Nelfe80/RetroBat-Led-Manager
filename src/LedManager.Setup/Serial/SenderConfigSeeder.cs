using System.IO;
using LedManager.Core.Ini;

namespace LedManager.Setup.Serial;

/// <summary>
/// Makes a sender (P2, P3…) actually configurable by the wizard: a fresh section set
/// ships as documentation-only (DryRun=true, no [GPIO:Pn], firmware profile init).
/// Called once the wizard has detected the Pico on a real port, it seeds the missing
/// pieces with the standard GPIO plan and switches the sender to the same auto-init
/// pipeline as P1. Existing keys are never overwritten - except Port (the port that
/// really answered) and DryRun=false, which configuring the Pico implies.
/// </summary>
public static class SenderConfigSeeder
{
    /// <summary>Standard GPIO plan of the reference kit (materiel page): triplets in
    /// the firmware color tables' G,R,B position order - the color test fixes any
    /// wire-order difference afterwards.</summary>
    private static readonly (string Key, string Value)[] DefaultGpio =
    {
        ("B1", "0,1,2"), ("B2", "3,4,5"), ("B3", "6,7,8"), ("B4", "9,10,11"),
        ("B5", "12,13,14"), ("B6", "15,16,17"), ("B7", "18,19,20"), ("B8", "21,22,26"),
        ("START", "27"), ("SELECT", "28")
    };

    public static bool Ensure(string pluginRoot, string senderId, string portName)
    {
        var iniPath = Path.Combine(pluginRoot, "PicoCommandSender.ini");
        if (!File.Exists(iniPath))
        {
            return false;
        }

        var ini = IniDocument.Load(iniPath);
        var editor = IniEditor.Load(iniPath);
        var changed = false;

        var port = ini.Get($"Serial:{senderId}", "Port", "auto");
        if (!string.IsNullOrWhiteSpace(portName) && !port.Equals(portName, StringComparison.OrdinalIgnoreCase))
        {
            editor.Set($"Serial:{senderId}", "Port", portName);
            changed = true;
        }

        if (ini.GetBool($"Serial:{senderId}", "DryRun", false))
        {
            editor.Set($"Serial:{senderId}", "DryRun", "false");
            changed = true;
        }

        if (!ini.GetBool($"Pico:{senderId}", "AutoInitFromHardware", false))
        {
            editor.Set($"Pico:{senderId}", "AutoInitFromHardware", "true");
            changed = true;
        }

        if (!ini.GetBool($"CommandAdapter:{senderId}", "Enabled", false))
        {
            editor.Set($"CommandAdapter:{senderId}", "Enabled", "true");
            changed = true;
        }

        var hardware = ini.Section($"Hardware:{senderId}");
        if (!hardware.ContainsKey("PanelButtons"))
        {
            editor.Set($"Hardware:{senderId}", "PanelButtons", "8");
            editor.Set($"Hardware:{senderId}", "PanelButtonType", "RGBLED");
            editor.Set($"Hardware:{senderId}", "Start", "LED");
            editor.Set($"Hardware:{senderId}", "Select", "LED");
            editor.Set($"Hardware:{senderId}", "Joystick1", "NONE");
            editor.Set($"Hardware:{senderId}", "Joystick2", "NONE");
            editor.Set($"Hardware:{senderId}", "OnOffInvert", "true");
            changed = true;
        }

        var gpio = ini.Section($"GPIO:{senderId}");
        if (!gpio.ContainsKey("B1"))
        {
            foreach (var (key, value) in DefaultGpio)
            {
                editor.Set($"GPIO:{senderId}", key, value);
            }

            changed = true;
        }

        if (changed)
        {
            editor.Save();
        }

        return changed;
    }
}
