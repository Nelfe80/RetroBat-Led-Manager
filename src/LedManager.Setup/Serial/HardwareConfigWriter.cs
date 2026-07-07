using System.IO;

namespace LedManager.Setup.Serial;

/// <summary>
/// Writes the validated hardware facts into PicoCommandSender.ini at the end of the
/// wizard: the COM port that actually answered, the panel composition, and a
/// PostInitDelayMs based on the measured firmware init time instead of the shipped
/// conservative guess. Comment-preserving, .bak before write.
/// </summary>
public static class HardwareConfigWriter
{
    public sealed record Result(bool Success, string Message);

    public static Result Apply(
        string pluginRoot,
        string sender,
        string? portName,
        int? measuredInitMs,
        int buttonCount,
        bool hasStart,
        bool hasSelect)
    {
        var iniPath = Path.Combine(pluginRoot, "PicoCommandSender.ini");
        if (!File.Exists(iniPath))
        {
            return new Result(false, "PicoCommandSender.ini introuvable.");
        }

        var editor = IniEditor.Load(iniPath);
        var written = new List<string>();

        if (!string.IsNullOrWhiteSpace(portName))
        {
            editor.Set($"Serial:{sender}", "Port", portName);
            written.Add($"Port={portName}");
        }

        if (measuredInitMs is { } measured && measured > 0)
        {
            // 25% headroom over the measured firmware init, never below 500 ms:
            // slightly generous beats a panel that misses its first commands.
            var delay = Math.Max(500, (int)(measured * 1.25));
            editor.Set($"Serial:{sender}", "PostInitDelayMs", delay.ToString());
            written.Add($"PostInitDelayMs={delay} (mesuré {measured} ms)");
        }

        editor.Set($"Hardware:{sender}", "PanelButtons", buttonCount.ToString());
        editor.Set($"Hardware:{sender}", "Start", hasStart ? "LED" : "NONE");
        editor.Set($"Hardware:{sender}", "Select", hasSelect ? "LED" : "NONE");
        written.Add($"PanelButtons={buttonCount}, Start={(hasStart ? "LED" : "NONE")}, Select={(hasSelect ? "LED" : "NONE")}");

        editor.Save();
        return new Result(true,
            "Configuration enregistrée dans PicoCommandSender.ini (sauvegarde .bak créée) :\n   • "
            + string.Join("\n   • ", written));
    }
}
