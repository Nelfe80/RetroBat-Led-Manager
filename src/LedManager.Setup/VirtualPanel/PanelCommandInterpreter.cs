using System.Windows.Media;

namespace LedManager.Setup.VirtualPanel;

/// <summary>
/// Translates the generic LedManager commands (SLOT/SET/BATCH/ALL/FLASH/CLEAR/MATRIX…)
/// into visual changes. The interpreter speaks the same dialect as the senders, so the
/// virtual panel shows by construction what the hardware would receive.
/// </summary>
public sealed class PanelCommandInterpreter
{
    /// <summary>slot number (1..N) → color</summary>
    public event Action<int, Color>? SlotChanged;

    /// <summary>named target (START, SELECT, JOY1…) → color</summary>
    public event Action<string, Color>? TargetChanged;

    /// <summary>every panel button at once (ALL / CLEAR)</summary>
    public event Action<Color>? AllChanged;

    /// <summary>matrix text or score to display</summary>
    public event Action<string>? MatrixChanged;

    /// <summary>flash: temporary color on a slot/target, restore after duration</summary>
    public event Action<string, Color, int>? Flashed;

    public void Apply(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var trimmed = command.Trim();
        var upper = trimmed.ToUpperInvariant();

        if (upper.StartsWith("BATCH ", StringComparison.Ordinal))
        {
            foreach (var part in trimmed[6..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Apply(part);
            }

            return;
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return;
        }

        switch (tokens[0].ToUpperInvariant())
        {
            case "CLEAR":
                AllChanged?.Invoke(PanelColors.Off);
                break;

            case "ALL" when tokens.Length >= 2:
                AllChanged?.Invoke(PanelColors.Resolve(tokens[1]));
                break;

            case "ALLPCT" when tokens.Length >= 4:
                AllChanged?.Invoke(PanelColors.FromExtinctionPercent(tokens[1], tokens[2], tokens[3]));
                break;

            case "SLOT" when tokens.Length >= 3 && int.TryParse(tokens[1], out var slot):
                SlotChanged?.Invoke(slot, PanelColors.Resolve(tokens[2]));
                break;

            case "SLOTPWM" when tokens.Length >= 3 && int.TryParse(tokens[1], out var pwmSlot):
                SlotChanged?.Invoke(pwmSlot, PanelColors.Resolve(tokens[2]));
                break;

            case "SET" when tokens.Length >= 3:
                TargetChanged?.Invoke(tokens[1].ToUpperInvariant(), PanelColors.Resolve(tokens[2]));
                break;

            case "FLASH" when tokens.Length >= 4 && int.TryParse(tokens[3], out var durationMs):
                Flashed?.Invoke(tokens[1].ToUpperInvariant(), PanelColors.Resolve(tokens[2]), durationMs);
                break;

            case "MATRIXSCORE" when tokens.Length >= 3:
                MatrixChanged?.Invoke(string.Join(' ', tokens[2..]));
                break;

            case "MATRIXTEXT" when tokens.Length >= 4:
                MatrixChanged?.Invoke(string.Join(' ', tokens[3..]));
                break;
        }
    }
}

public static class PanelColors
{
    public static readonly Color Off = Color.FromRgb(0x22, 0x22, 0x2C);

    private static readonly Dictionary<string, Color> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WHITE"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["BLACK"] = Off,
        ["OFF"] = Off,
        ["RED"] = Color.FromRgb(0xFF, 0x30, 0x30),
        ["GREEN"] = Color.FromRgb(0x30, 0xE8, 0x50),
        ["BLUE"] = Color.FromRgb(0x30, 0x70, 0xFF),
        ["YELLOW"] = Color.FromRgb(0xFF, 0xE8, 0x20),
        ["ORANGE"] = Color.FromRgb(0xFF, 0x8C, 0x00),
        ["CYAN"] = Color.FromRgb(0x20, 0xE8, 0xE8),
        ["MAGENTA"] = Color.FromRgb(0xFF, 0x30, 0xFF),
        ["PINK"] = Color.FromRgb(0xFF, 0x69, 0xB4),
        ["VIOLET"] = Color.FromRgb(0x8A, 0x2B, 0xE2),
        ["PURPLE"] = Color.FromRgb(0xA0, 0x30, 0xD0),
        ["GRAY"] = Color.FromRgb(0xB0, 0xB0, 0xB0),
        ["LIME"] = Color.FromRgb(0xA0, 0xFF, 0x30),
        ["TURQUOISE"] = Color.FromRgb(0x40, 0xE0, 0xD0),
        ["AQUA"] = Color.FromRgb(0x30, 0xD0, 0xFF),
        ["LEMON"] = Color.FromRgb(0xF0, 0xFF, 0x60),
        ["GOLD"] = Color.FromRgb(0xFF, 0xC0, 0x00),
        ["TEAL"] = Color.FromRgb(0x20, 0x90, 0x90)
    };

    public static Color Resolve(string token)
    {
        if (token.StartsWith("PCT:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = token[4..].Split(',');
            if (parts.Length == 3)
            {
                return FromExtinctionPercent(parts[0], parts[1], parts[2]);
            }
        }

        return Named.TryGetValue(token, out var color) ? color : Color.FromRgb(0xFF, 0xFF, 0xFF);
    }

    /// <summary>The Pico firmware speaks in extinction percentages: 0,0,0 = white, 100,100,100 = off.</summary>
    public static Color FromExtinctionPercent(string r, string g, string b)
    {
        return Color.FromRgb(FromPercent(r), FromPercent(g), FromPercent(b));
    }

    private static byte FromPercent(string value)
    {
        if (!double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            return 0;
        }

        pct = Math.Clamp(pct, 0, 100);
        return (byte)Math.Round(255 * (100 - pct) / 100);
    }
}
