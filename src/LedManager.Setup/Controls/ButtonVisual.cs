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

    public FrameworkElement Root { get; }
    public Color CurrentColor { get; private set; } = PanelColors.Off;

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

        var layers = new Grid { Width = size * 1.5, Height = size * 1.5 };
        layers.Children.Add(_halo);
        layers.Children.Add(_dome);
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
