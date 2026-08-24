using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LedManager.Setup.Controls;
using LedManager.Setup.Localization;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Views;

/// <summary>
/// Live monitor: mirrors what LedManager sends to the hardware, in real time.
/// Read-only; works while LedManager (and a game) is running.
/// </summary>
public sealed class MonitorView : UserControl, IDisposable
{
    private readonly PanelSurface _panel = new();
    private readonly PanelCommandInterpreter _interpreter = new();
    private readonly Ellipse _statusDot;
    private readonly TextBlock _statusText;
    private readonly ListBox _log;
    private readonly ConcurrentQueue<VirtualPanelEvent> _pending = new();
    private readonly DispatcherTimer _drainTimer;
    private VirtualPanelClient? _client;

    public MonitorView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _panel.Build(layout, hardware.ButtonCount, hardware.HasStart, hardware.HasSelect);

        _interpreter.SlotChanged += _panel.SetSlot;
        _interpreter.TargetChanged += _panel.SetTarget;
        _interpreter.AllChanged += _panel.SetAll;
        _interpreter.MatrixChanged += _panel.SetMatrix;
        _interpreter.Flashed += _panel.Flash;

        _statusDot = new Ellipse { Width = 12, Height = 12, Fill = Ui.Brush(Color.FromRgb(0xD0, 0x40, 0x40)), VerticalAlignment = VerticalAlignment.Center };
        _statusText = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush(Color.FromRgb(0x8A, 0x8A, 0x9A)), Text = L.T("En attente de LedManager…", "Waiting for LedManager…") };
        // themed log card: white in the light theme, dark card in the dark one
        _log = new ListBox { Height = 104, Background = Brushes.Transparent, Foreground = Ui.Text(0xB8, 0xB8, 0xC6), FontFamily = new FontFamily("Consolas"), FontSize = 11, BorderThickness = new Thickness(0) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(_statusDot, Dock.Left);
        header.Children.Add(_statusDot);
        header.Children.Add(_statusText);

        var panelBorder = new Border { Background = Ui.Viewport, CornerRadius = new CornerRadius(12), Padding = new Thickness(24), Child = _panel };

        // console card under the panel: caption + rounded dark plate, so it reads
        // as a hardware feed viewer instead of a bare black block
        var logStack = new StackPanel();
        logStack.Children.Add(new TextBlock
        {
            Text = L.T("FLUX MATÉRIEL", "HARDWARE FEED"),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x7E)),
            Margin = new Thickness(2, 0, 0, 5)
        });
        logStack.Children.Add(_log);
        var logCard = new Border
        {
            Background = Ui.Brush(Color.FromRgb(0x1D, 0x1D, 0x2A)),
            BorderBrush = Ui.Brush(Color.FromRgb(0x3A, 0x3A, 0x52)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 12, 0, 0),
            Child = logStack
        };

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(header, 0);
        Grid.SetRow(panelBorder, 1);
        Grid.SetRow(logCard, 2);
        grid.Children.Add(header);
        grid.Children.Add(panelBorder);
        grid.Children.Add(logCard);
        Content = grid;

        _drainTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _drainTimer.Tick += (_, _) => DrainPending();

        _client = new VirtualPanelClient(port: hardware.MirrorPort);
        _client.ConnectionChanged += connected => Dispatcher.BeginInvoke(() => OnConnectionChanged(connected), DispatcherPriority.Background);
        _client.MessageReceived += _pending.Enqueue;
    }

    /// <summary>Starts mirroring. Called when the monitor tab becomes active.</summary>
    public void Activate()
    {
        _drainTimer.Start();
        _client?.Start();
    }

    private void OnConnectionChanged(bool connected)
    {
        _statusDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(0x30, 0xE8, 0x50) : Color.FromRgb(0xD0, 0x40, 0x40));
        _statusText.Text = connected
            ? L.T("Connecté à LedManager - le panel reflète en direct ce que le matériel reçoit.",
                "Connected to LedManager - the panel mirrors live what the hardware receives.")
            : L.T("En attente de LedManager - lancez RetroBat ou LedManager.exe…",
                "Waiting for LedManager - start RetroBat or LedManager.exe…");
        if (!connected)
        {
            _panel.ClearAll();
        }
    }

    private void DrainPending()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        // Snapshot-bounded drain: never chase a queue that refills while we loop,
        // otherwise a busy in-game stream keeps the UI thread captive.
        var toProcess = Math.Min(_pending.Count, 5000);
        var logged = 0;
        for (var i = 0; i < toProcess && _pending.TryDequeue(out var evt); i++)
        {
            _interpreter.Apply(evt.Command);
            if (logged < 25)
            {
                _log.Items.Insert(0, new TextBlock
                {
                    Text = $"{DateTime.Now:HH:mm:ss.fff}  [{evt.Sender}] {evt.Command}",
                    Foreground = Ui.Text(0xB8, 0xB8, 0xC6)
                });
                logged++;
            }
        }

        while (_log.Items.Count > 100)
        {
            _log.Items.RemoveAt(_log.Items.Count - 1);
        }
    }

    public void Dispose()
    {
        _drainTimer.Stop();
        _client?.Dispose();
        _client = null;
    }
}
