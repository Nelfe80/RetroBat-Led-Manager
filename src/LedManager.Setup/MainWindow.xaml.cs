using System.Diagnostics;
using System.Windows;
using LedManager.Setup.Controls;
using LedManager.Setup.Views;

namespace LedManager.Setup;

/// <summary>
/// Shell: sidebar navigation between the live Panel monitor (read-only mirror of
/// LedManager) and the hardware Wizard (direct Pico control while LedManager is
/// stopped). The two are naturally exclusive on the COM port.
/// </summary>
public partial class MainWindow : Window
{
    private readonly HardwareDescription _hardware;
    private readonly PanelLayoutDefinition _layout;
    private MonitorView? _monitor;
    private WizardView? _wizard;

    public MainWindow()
    {
        InitializeComponent();
        TryLowerProcessPriority();

        var root = HardwareDescription.FindPluginRoot();
        _hardware = HardwareDescription.Load(root);
        _layout = PanelLayoutDefinition.Load(root);

        HardwareInfo.Text = $"{_hardware.ButtonCount} boutons"
            + (_hardware.HasStart ? " · START" : "")
            + (_hardware.HasSelect ? " · SELECT" : "")
            + $"\nPort {_hardware.SerialPort} · {_hardware.BaudRate} bauds"
            + $"\nMiroir 127.0.0.1:{_hardware.MirrorPort}";

        ShowMonitor();

        Closed += (_, _) =>
        {
            _monitor?.Dispose();
            _wizard?.Dispose();
        };
    }

    private void NavMonitor_Checked(object sender, RoutedEventArgs e) => ShowMonitor();

    private void NavWizard_Checked(object sender, RoutedEventArgs e) => ShowWizard();

    private void ShowMonitor()
    {
        if (!IsLoaded && ContentHost is null)
        {
            return;
        }

        _wizard?.Dispose();
        _wizard = null;

        _monitor = new MonitorView(_hardware, _layout);
        ContentHost.Content = _monitor;
        _monitor.Activate();
    }

    private void ShowWizard()
    {
        _monitor?.Dispose();
        _monitor = null;

        _wizard = new WizardView(_hardware, _layout);
        ContentHost.Content = _wizard;
    }

    private static void TryLowerProcessPriority()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            current.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // cosmetic
        }
    }
}
