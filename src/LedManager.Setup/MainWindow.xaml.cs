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

    // A game is running while this window is open: messages are queued and the UI
    // is refreshed in a single 50 ms batch pass instead of one dispatch per command.
    private readonly System.Collections.Concurrent.ConcurrentQueue<VirtualPanelEvent> _pending = new();
    private readonly DispatcherTimer _drainTimer;

    public MainWindow()
    {
        InitializeComponent();
        TryLowerProcessPriority();

        var (buttonCount, hasStart, hasSelect, port) = LoadHardwareDescription();
        var layout = PanelLayoutDefinition.Load(FindPluginRoot());
        BuildPanel(layout, buttonCount, hasStart, hasSelect);

        _interpreter.SlotChanged += SetSlot;
        _interpreter.TargetChanged += SetTarget;
        _interpreter.AllChanged += SetAll;
        _interpreter.MatrixChanged += text => MatrixText.Text = text;
        _interpreter.Flashed += Flash;

        _drainTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _drainTimer.Tick += (_, _) => DrainPending();
        _drainTimer.Start();

        _client = new VirtualPanelClient(port: port);
        _client.ConnectionChanged += connected => Dispatcher.BeginInvoke(() => OnConnectionChanged(connected), DispatcherPriority.Background);
        _client.MessageReceived += _pending.Enqueue;
        _client.Start();

        Closed += (_, _) =>
        {
            _drainTimer.Stop();
            _client.Dispose();
        };
    }

    private static void TryLowerProcessPriority()
    {
        try
        {
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            current.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Cosmetic: the game keeps priority anyway.
        }
    }

    private void DrainPending()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        // Snapshot-bounded drain: never chase a queue that refills while we loop,
        // otherwise a busy in-game stream keeps the UI thread captive (frozen window).
        // Commands are applied in order, so the LAST state always wins; the log only
        // shows a sample of the pass.
        var toProcess = Math.Min(_pending.Count, 5000);
        var logged = 0;
        for (var i = 0; i < toProcess && _pending.TryDequeue(out var evt); i++)
        {
            _interpreter.Apply(evt.Command);
            if (logged < 25)
            {
                LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  [{evt.Sender}] {evt.Command}");
                logged++;
            }
        }

        while (LogList.Items.Count > 100)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
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

    /// <summary>
    /// Recommended RetroBat arrangement: SELECT then START at the top-left, and the
    /// button rows from the layout definition (8 buttons: B4 B3 B5 B7 / B1 B2 B6 B8).
    /// The same identities work from 2 to 8 buttons without rewiring.
    /// </summary>
    private void BuildPanel(PanelLayoutDefinition layout, int buttonCount, bool hasStart, bool hasSelect)
    {
        if (hasSelect)
        {
            var select = new ButtonVisual("SELECT", 52);
            _targets["SELECT"] = select;
            SystemButtonsRow.Children.Add(select.Root);
        }

        if (hasStart)
        {
            var start = new ButtonVisual("START", 52);
            _targets["START"] = start;
            SystemButtonsRow.Children.Add(start.Root);
        }

        foreach (var row in layout.RowsFor(buttonCount))
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var slot in row)
            {
                var visual = new ButtonVisual($"B{slot}", 84, layout.RetrobatLabel(slot));
                _slots[slot] = visual;
                rowPanel.Children.Add(visual.Root);
            }

            ButtonRowsHost.Children.Add(rowPanel);
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

}

/// <summary>
/// The recommended physical layout (resources\setup\layouts\retrobat_standard.json).
/// Falls back to the curator's canonical LAYOUT_SLOTS if the file is missing.
/// </summary>
internal sealed class PanelLayoutDefinition
{
    private static readonly Dictionary<string, int[][]> FallbackRows = new()
    {
        ["2-Button"] = new[] { new[] { 1, 2 } },
        ["4-Button"] = new[] { new[] { 4, 3 }, new[] { 1, 2 } },
        ["6-Button"] = new[] { new[] { 4, 3, 5 }, new[] { 1, 2, 6 } },
        ["8-Button"] = new[] { new[] { 4, 3, 5, 7 }, new[] { 1, 2, 6, 8 } }
    };

    private static readonly Dictionary<int, string> FallbackLabels = new()
    {
        [1] = "A", [2] = "B", [3] = "X", [4] = "Y", [5] = "L1", [6] = "R1", [7] = "L2", [8] = "R2"
    };

    private Dictionary<string, int[][]> _rows = FallbackRows;
    private Dictionary<int, string> _labels = FallbackLabels;

    public static PanelLayoutDefinition Load(string? pluginRoot)
    {
        var definition = new PanelLayoutDefinition();
        if (pluginRoot is null)
        {
            return definition;
        }

        var path = System.IO.Path.Combine(pluginRoot, "resources", "setup", "layouts", "retrobat_standard.json");
        if (!File.Exists(path))
        {
            return definition;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("layouts", out var layouts))
            {
                var rows = new Dictionary<string, int[][]>();
                foreach (var layout in layouts.EnumerateObject())
                {
                    if (layout.Value.TryGetProperty("rows", out var rowsElement))
                    {
                        rows[layout.Name] = rowsElement.EnumerateArray()
                            .Select(r => r.EnumerateArray().Select(v => v.GetInt32()).ToArray())
                            .ToArray();
                    }
                }

                if (rows.Count > 0)
                {
                    definition._rows = rows;
                }
            }

            if (root.TryGetProperty("buttons", out var buttons))
            {
                var labels = new Dictionary<int, string>();
                foreach (var button in buttons.EnumerateObject())
                {
                    if (int.TryParse(button.Name, out var slot)
                        && button.Value.TryGetProperty("retrobat", out var name))
                    {
                        labels[slot] = name.GetString() ?? "";
                    }
                }

                if (labels.Count > 0)
                {
                    definition._labels = labels;
                }
            }
        }
        catch
        {
            // Malformed file: the canonical fallback still applies.
        }

        return definition;
    }

    public IEnumerable<int[]> RowsFor(int buttonCount)
    {
        var key = buttonCount switch
        {
            <= 2 => "2-Button",
            <= 4 => "4-Button",
            <= 6 => "6-Button",
            _ => "8-Button"
        };

        var rows = _rows.TryGetValue(key, out var found) ? found : FallbackRows["8-Button"];
        return rows
            .Select(row => row.Where(slot => slot <= buttonCount).ToArray())
            .Where(row => row.Length > 0);
    }

    public string RetrobatLabel(int slot)
    {
        return _labels.TryGetValue(slot, out var label) ? label : "";
    }
}

/// <summary>
/// A round arcade button with a glow that follows the LED color.
/// Performance matters here: this window runs WHILE a game is running, so no
/// bitmap effects (DropShadowEffect renders in software and steals frame time).
/// The glow is a plain gradient ellipse and all brushes are cached and frozen.
/// </summary>
internal sealed class ButtonVisual
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
        if (color == CurrentColor && !ReferenceEquals(_dome.Fill, null))
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
