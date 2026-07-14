using System.IO;
using System.Windows;
using System.Windows.Media;
using LedManager.Setup.Serial;

namespace LedManager.Setup;

/// <summary>
/// Theme engine. The XAML shell consumes the token brushes through
/// DynamicResource, so <see cref="Apply"/> retints it live; the code-built views
/// route their colors through <see cref="Brush"/>/<see cref="Text"/>, whose remap
/// table translates the dark palette into the light one — views are rebuilt on
/// theme change. Panel and patch-bay canvases deliberately stay dark in both
/// themes (<see cref="Viewport"/>): LEDs and cables need a dark plate, like the
/// viewport of any node editor. Choice persisted in LedManager.ini [Setup] Theme.
/// </summary>
public static class Ui
{
    public static bool IsLight { get; private set; }

    /// <summary>Dark plate for panel / patch-bay canvases, identical in both themes.</summary>
    public static SolidColorBrush Viewport { get; } = Frozen(C(0x15, 0x15, 0x1F));

    private static readonly Dictionary<Color, Color> LightMap = new()
    {
        [C(0xE8, 0xE8, 0xF0)] = C(0x1E, 0x1E, 0x28), // primary text
        [C(0xB8, 0xB8, 0xC6)] = C(0x4A, 0x4A, 0x5A), // body text
        [C(0x8A, 0x8A, 0x9A)] = C(0x70, 0x70, 0x7E), // muted text
        [C(0x1D, 0x1D, 0x2A)] = C(0xFF, 0xFF, 0xFF), // card surface
        [C(0x2E, 0x2E, 0x44)] = C(0xE7, 0xE8, 0xF2), // chip / badge surface
        [C(0x16, 0x16, 0x20)] = C(0xEF, 0xF0, 0xF6), // inset surface
        [C(0x1A, 0x1A, 0x26)] = C(0xFF, 0xFF, 0xFF), // field surface
        [C(0x26, 0x26, 0x36)] = C(0xEA, 0xEB, 0xF3), // control surface
        [C(0x3A, 0x3A, 0x52)] = C(0xCE, 0xCF, 0xE0), // border
        [C(0x30, 0xE8, 0x50)] = C(0x1F, 0xA8, 0x3C), // ok green
        [C(0xE8, 0x5C, 0x5C)] = C(0xC9, 0x3A, 0x3A), // error red
        [C(0xE8, 0x30, 0x30)] = C(0xD4, 0x2B, 0x2B), // red
        [C(0xD0, 0x40, 0x40)] = C(0xC0, 0x30, 0x30), // red (monitor)
        [C(0x30, 0x60, 0xE8)] = C(0x2B, 0x50, 0xD0), // blue
        [C(0x20, 0xE8, 0xE8)] = C(0x0F, 0x9E, 0xAE), // cyan (actions)
        [C(0xE8, 0x8A, 0x5A)] = C(0xC4, 0x62, 0x2F), // warning orange
    };

    private static readonly Dictionary<string, Color> DarkTokens = new()
    {
        ["AppBackground"] = C(0x0F, 0x0F, 0x17),
        ["SidebarBackground"] = C(0x0B, 0x0B, 0x12),
        ["PanelBackground"] = C(0x1D, 0x1D, 0x2A),
        ["CardBackground"] = C(0x17, 0x17, 0x22),
        ["AppForeground"] = C(0xE8, 0xE8, 0xF0),
        ["AppMuted"] = C(0x8A, 0x8A, 0x9A),
        ["ControlBackground"] = C(0x26, 0x26, 0x36),
        ["ControlHover"] = C(0x32, 0x32, 0x4A),
        ["ControlBorder"] = C(0x3A, 0x3A, 0x52),
        ["FieldBackground"] = C(0x1A, 0x1A, 0x26),
        ["HairlineBrush"] = C(0x22, 0x22, 0x2F),
        ["AccentSoft"] = C(0xB1, 0x8C, 0xFF),
        ["NavHover"] = C(0x18, 0x18, 0x24),
        ["NavChecked"] = C(0x1E, 0x1E, 0x2E),
    };

    private static readonly Dictionary<string, Color> LightTokens = new()
    {
        ["AppBackground"] = C(0xF1, 0xF2, 0xF7),
        ["SidebarBackground"] = C(0xFA, 0xFB, 0xFE),
        ["PanelBackground"] = C(0xFF, 0xFF, 0xFF),
        ["CardBackground"] = C(0xFF, 0xFF, 0xFF),
        ["AppForeground"] = C(0x1E, 0x1E, 0x28),
        ["AppMuted"] = C(0x70, 0x70, 0x7E),
        ["ControlBackground"] = C(0xEA, 0xEB, 0xF3),
        ["ControlHover"] = C(0xDE, 0xDF, 0xEC),
        ["ControlBorder"] = C(0xCE, 0xCF, 0xE0),
        ["FieldBackground"] = C(0xFF, 0xFF, 0xFF),
        ["HairlineBrush"] = C(0xE3, 0xE4, 0xEE),
        ["AccentSoft"] = C(0x8A, 0x2B, 0xE2),
        ["NavHover"] = C(0xEC, 0xEC, 0xF4),
        ["NavChecked"] = C(0xE9, 0xE2, 0xF8),
    };

    public static void Initialize(string? pluginRoot)
    {
        IsLight = ReadSetting(pluginRoot).Equals("light", StringComparison.OrdinalIgnoreCase);
        Apply();
    }

    public static void Toggle(string? pluginRoot)
    {
        IsLight = !IsLight;
        Apply();
        try
        {
            var ini = IniEditor.Load(Path.Combine(pluginRoot ?? ".", "LedManager.ini"));
            ini.Set("Setup", "Theme", IsLight ? "light" : "dark");
            ini.Save();
        }
        catch
        {
            // the theme still applies for the session
        }
    }

    /// <summary>Theme-aware brush for the code-built views (frozen).</summary>
    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(Remap(color));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Text(byte r, byte g, byte b) => Brush(Color.FromRgb(r, g, b));

    public static Color Remap(Color color)
        => IsLight && LightMap.TryGetValue(color, out var mapped) ? mapped : color;

    private static void Apply()
    {
        var resources = Application.Current.Resources;
        foreach (var (key, color) in IsLight ? LightTokens : DarkTokens)
        {
            resources[key] = Frozen(color);
        }
    }

    private static string ReadSetting(string? pluginRoot)
    {
        try
        {
            var path = Path.Combine(pluginRoot ?? ".", "LedManager.ini");
            if (!File.Exists(path))
            {
                return "dark";
            }

            string? section = null;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    section = line.Trim('[', ']');
                    continue;
                }

                if (!string.Equals(section, "Setup", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals("Theme", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(eq + 1)..].Split(';', '#')[0].Trim();
                }
            }
        }
        catch
        {
            // unreadable ini: dark default
        }

        return "dark";
    }

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
