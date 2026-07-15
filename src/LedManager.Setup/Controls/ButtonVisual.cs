using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Controls;

/// <summary>
/// A round arcade button with a glow that follows the LED color.
/// No bitmap effects (they render in software and steal frame time while a game runs):
/// the glow is a plain gradient ellipse and all brushes are cached and frozen.
/// </summary>
public sealed class ButtonVisual
{
    private static readonly Dictionary<Color, (Brush Dome, Brush Glow)> BrushCache = new();
    private static readonly Brush StrokeBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x48)));
    private static readonly Brush CaptionBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A)));

    private readonly Ellipse _dome;
    private readonly Ellipse _halo;
    private readonly Grid _centerLabel;
    private readonly List<TextBlock> _centerTexts = new();

    public FrameworkElement Root { get; }
    public Color CurrentColor { get; private set; } = PanelColors.Off;

    /// <summary>PlayStation face buttons wear their symbol, like on the pad.</summary>
    private static readonly Dictionary<string, string> FunctionSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cross"] = "✕", ["croix"] = "✕",
        ["circle"] = "○", ["rond"] = "○",
        ["square"] = "□", ["carre"] = "□", ["carré"] = "□",
        ["triangle"] = "△"
    };

    public ButtonVisual(string label, double size, string subLabel = "")
    {
        _halo = new Ellipse
        {
            Width = size * 1.5,
            Height = size * 1.5,
            IsHitTestVisible = false
        };

        _dome = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 3,
            Stroke = StrokeBrush
        };

        // historical function name centered on the dome: white with a black
        // outline drawn as four 1px-offset copies (vector, no bitmap effect)
        _centerLabel = new Grid { IsHitTestVisible = false };
        foreach (var (dx, dy) in new[] { (-1.0, 0.0), (1.0, 0.0), (0.0, -1.0), (0.0, 1.0), (0.0, 0.0) })
        {
            var copy = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                Foreground = dx == 0 && dy == 0 ? Brushes.White : Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(dx, dy, -dx, -dy),
                TextAlignment = TextAlignment.Center
            };
            _centerTexts.Add(copy);
            _centerLabel.Children.Add(copy);
        }

        var layers = new Grid { Width = size * 1.5, Height = size * 1.5 };
        layers.Children.Add(_halo);
        layers.Children.Add(_dome);
        layers.Children.Add(_centerLabel);
        _dome.HorizontalAlignment = HorizontalAlignment.Center;
        _dome.VerticalAlignment = VerticalAlignment.Center;

        var caption = string.IsNullOrEmpty(subLabel) ? label : $"{label} · {subLabel}";
        var text = new TextBlock
        {
            Text = caption,
            Foreground = CaptionBrush,
            FontSize = size >= 70 ? 13 : 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var stack = new StackPanel { Margin = new Thickness(4) };
        stack.Children.Add(layers);
        stack.Children.Add(text);
        Root = stack;

        SetColor(PanelColors.Off);
    }

    /// <summary>Sets the historical function name shown at the dome's center
    /// (A, B, L, R, ✕, ○…). Empty hides it; long names shrink then hide.</summary>
    public void SetCenterLabel(string? function)
    {
        var text = (function ?? "").Trim();
        if (FunctionSymbols.TryGetValue(text, out var symbol))
        {
            text = symbol;
        }

        if (text.Length > 10)
        {
            text = "";
        }

        var fontSize = text.Length <= 2 ? 22.0 : text.Length <= 5 ? 13.0 : 10.0;
        foreach (var copy in _centerTexts)
        {
            copy.Text = text;
            copy.FontSize = fontSize;
        }

        _centerLabel.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetColor(Color color)
    {
        if (color == CurrentColor && _dome.Fill is not null)
        {
            return;
        }

        CurrentColor = color;
        var (dome, glow) = GetBrushes(color);
        _dome.Fill = dome;
        _halo.Fill = glow;
    }

    private static (Brush Dome, Brush Glow) GetBrushes(Color color)
    {
        if (BrushCache.TryGetValue(color, out var cached))
        {
            return cached;
        }

        var isOff = color == PanelColors.Off;

        var dome = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            Center = new Point(0.5, 0.5),
            GradientStops =
            {
                new GradientStop(Lighten(color, isOff ? 0.05 : 0.45), 0.0),
                new GradientStop(color, 0.55),
                new GradientStop(Darken(color, 0.45), 1.0)
            }
        };
        dome.Freeze();

        Brush glow;
        if (isOff)
        {
            glow = Brushes.Transparent;
        }
        else
        {
            var haloBrush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xA0, color.R, color.G, color.B), 0.55),
                    new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1.0)
                }
            };
            haloBrush.Freeze();
            glow = haloBrush;
        }

        var entry = ((Brush)dome, glow);
        BrushCache[color] = entry;
        return entry;
    }

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Color Lighten(Color c, double amount)
    {
        return Color.FromRgb(
            (byte)Math.Min(255, c.R + (255 - c.R) * amount),
            (byte)Math.Min(255, c.G + (255 - c.G) * amount),
            (byte)Math.Min(255, c.B + (255 - c.B) * amount));
    }

    private static Color Darken(Color c, double amount)
    {
        return Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
    }
}
