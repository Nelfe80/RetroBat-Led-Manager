using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using LedManager.Core.Ini;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup;

public partial class MainWindow : Window
{
    private readonly VirtualPanelClient _client;
    private readonly PanelCommandInterpreter _interpreter = new();
    private readonly Dictionary<int, ButtonVisual> _slots = new();
    private readonly Dictionary<string, ButtonVisual> _targets = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();

        var (buttonCount, hasStart, hasSelect, port) = LoadHardwareDescription();
        BuildPanel(buttonCount, hasStart, hasSelect);

        _interpreter.SlotChanged += (slot, color) => Dispatch(() => SetSlot(slot, color));
        _interpreter.TargetChanged += (target, color) => Dispatch(() => SetTarget(target, color));
        _interpreter.AllChanged += color => Dispatch(() => SetAll(color));
        _interpreter.MatrixChanged += text => Dispatch(() => MatrixText.Text = text);
        _interpreter.Flashed += (target, color, ms) => Dispatch(() => Flash(target, color, ms));

        _client = new VirtualPanelClient(port: port);
        _client.ConnectionChanged += connected => Dispatch(() => OnConnectionChanged(connected));
        _client.MessageReceived += evt => Dispatch(() => OnMessage(evt));
        _client.Start();

        Closed += (_, _) => _client.Dispose();
    }

    private void Dispatch(Action action)
    {
        Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    /// <summary>
    /// The panel drawing is data-driven: button count from PicoCommandSender.ini,
    /// mirror port from LedManager.ini. The reference kit is one profile among others.
    /// </summary>
    private static (int ButtonCount, bool HasStart, bool HasSelect, int Port) LoadHardwareDescription()
    {
        var buttonCount = 8;
        var hasStart = true;
        var hasSelect = true;
        var port = 12377;

        var root = FindPluginRoot();
        if (root is null)
        {
            return (buttonCount, hasStart, hasSelect, port);
        }

        var senderIni = System.IO.Path.Combine(root, "PicoCommandSender.ini");
        if (File.Exists(senderIni))
        {
            var ini = IniDocument.Load(senderIni);
            buttonCount = Math.Clamp(ini.GetInt("Hardware:P1", "PanelButtons", 8), 1, 16);
            hasStart = !string.Equals(ini.Get("Hardware:P1", "Start", "LED"), "NONE", StringComparison.OrdinalIgnoreCase);
            hasSelect = !string.Equals(ini.Get("Hardware:P1", "Select", "LED"), "NONE", StringComparison.OrdinalIgnoreCase);
        }

        var managerIni = System.IO.Path.Combine(root, "LedManager.ini");
        if (File.Exists(managerIni))
        {
            var ini = IniDocument.Load(managerIni);
            port = ini.GetInt("VirtualPanel", "Port", 12377);
        }

        return (buttonCount, hasStart, hasSelect, port);
    }

    private static string? FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "LedManager.ini")))
            {
                return dir.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void BuildPanel(int buttonCount, bool hasStart, bool hasSelect)
    {
        ButtonsGrid.Rows = buttonCount > 4 ? 2 : 1;
        for (var slot = 1; slot <= buttonCount; slot++)
        {
            var visual = new ButtonVisual($"B{slot}", 84);
            _slots[slot] = visual;
            ButtonsGrid.Children.Add(visual.Root);
        }

        if (hasStart)
        {
            var start = new ButtonVisual("START", 56);
            _targets["START"] = start;
            StartButtonHost.Content = start.Root;
        }

        if (hasSelect)
        {
            var select = new ButtonVisual("SELECT", 56);
            _targets["SELECT"] = select;
            SelectButtonHost.Content = select.Root;
        }
    }

    private void OnConnectionChanged(bool connected)
    {
        StatusDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(0x30, 0xE8, 0x50) : Color.FromRgb(0xD0, 0x40, 0x40));
        StatusText.Text = connected
            ? "Connecté à LedManager — le panel reflète en direct ce que le matériel reçoit."
            : "En attente de LedManager (127.0.0.1) — lancez RetroBat ou LedManager.exe…";
        if (!connected)
        {
            SetAll(PanelColors.Off);
            MatrixText.Text = "";
        }
    }

    private void OnMessage(VirtualPanelEvent evt)
    {
        _interpreter.Apply(evt.Command);
        Log($"[{evt.Sender}] {evt.Command}");
    }

    private void SetSlot(int slot, Color color)
    {
        if (_slots.TryGetValue(slot, out var visual))
        {
            visual.SetColor(color);
        }
    }

    private void SetTarget(string target, Color color)
    {
        if (_targets.TryGetValue(target, out var visual))
        {
            visual.SetColor(color);
        }
        else if (target.StartsWith("B", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(target.AsSpan(1), out var slot))
        {
            SetSlot(slot, color);
        }
    }

    private void SetAll(Color color)
    {
        foreach (var visual in _slots.Values)
        {
            visual.SetColor(color);
        }
    }

    private void Flash(string target, Color color, int durationMs)
    {
        ButtonVisual? visual = null;
        if (int.TryParse(target, out var slot))
        {
            _slots.TryGetValue(slot, out visual);
        }
        else
        {
            _targets.TryGetValue(target, out visual);
        }

        if (visual is null)
        {
            return;
        }

        var previous = visual.CurrentColor;
        visual.SetColor(color);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, durationMs)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            visual.SetColor(previous);
        };
        timer.Start();
    }

    private void Log(string line)
    {
        LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {line}");
        while (LogList.Items.Count > 300)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }
}

/// <summary>A round arcade button with a glow that follows the LED color.</summary>
internal sealed class ButtonVisual
{
    private readonly Ellipse _dome;
    private readonly DropShadowEffect _glow;

    public FrameworkElement Root { get; }
    public Color CurrentColor { get; private set; } = PanelColors.Off;

    public ButtonVisual(string label, double size)
    {
        _glow = new DropShadowEffect
        {
            Color = PanelColors.Off,
            BlurRadius = 24,
            ShadowDepth = 0,
            Opacity = 0.0
        };

        _dome = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 3,
            Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x48)),
            Effect = _glow
        };

        var text = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A)),
            FontSize = size >= 70 ? 13 : 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(_dome);
        stack.Children.Add(text);
        Root = stack;

        SetColor(PanelColors.Off);
    }

    public void SetColor(Color color)
    {
        CurrentColor = color;
        var isOff = color == PanelColors.Off;

        _dome.Fill = new RadialGradientBrush
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

        _glow.Color = color;
        _glow.Opacity = isOff ? 0.0 : 0.9;
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
