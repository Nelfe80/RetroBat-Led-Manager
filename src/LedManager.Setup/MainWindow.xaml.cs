using System.Diagnostics;
using System.Windows;
using LedManager.Setup.Controls;
using LedManager.Setup.Localization;
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

        NavMonitor.Content = L.T("Panel virtuel", "Virtual panel");
        NavGames.Content = L.T("Mes jeux", "My games");
        NavWizard.Content = L.T("Assistant matériel", "Hardware assistant");

        var root = HardwareDescription.FindPluginRoot();
        _hardware = HardwareDescription.Load(root);
        _layout = PanelLayoutDefinition.Load(root);

        HardwareInfo.Text = $"{_hardware.ButtonCount} " + L.T("boutons", "buttons")
            + (_hardware.HasStart ? " · START" : "")
            + (_hardware.HasSelect ? " · SELECT" : "")
            + $"\nPort {_hardware.SerialPort} · {_hardware.BaudRate} bauds"
            + "\n" + L.T("Miroir", "Mirror") + $" 127.0.0.1:{_hardware.MirrorPort}";

        ShowMonitor();

        Closed += (_, _) =>
        {
            _monitor?.Dispose();
            _wizard?.Dispose();
        };

        // documentation mode: `--screenshots <dir>` renders both views to PNG and exits.
        // Used to keep the wiki illustrations in sync with the real UI.
        var args = Environment.GetCommandLineArgs();
        var shotIndex = Array.IndexOf(args, "--screenshots");
        if (shotIndex >= 0 && shotIndex + 1 < args.Length)
        {
            Loaded += (_, _) => _ = CaptureAllTabsAsync(args[shotIndex + 1]);
        }
    }

    private async System.Threading.Tasks.Task CaptureAllTabsAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        NavMonitor.IsChecked = true;
        await System.Threading.Tasks.Task.Delay(1200);
        SaveScreenshot(directory, "setup-monitor");
        NavGames.IsChecked = true;
        await System.Threading.Tasks.Task.Delay(1200);
        SaveScreenshot(directory, "setup-games");
        NavWizard.IsChecked = true;
        await System.Threading.Tasks.Task.Delay(1200);
        SaveScreenshot(directory, "setup-wizard");
        Close();
    }

    private void SaveScreenshot(string directory, string name)
    {
        if (Content is not FrameworkElement root || root.ActualWidth <= 0)
        {
            return;
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private void NavMonitor_Checked(object sender, RoutedEventArgs e) => ShowMonitor();

    private void NavGames_Checked(object sender, RoutedEventArgs e) => ShowGames();

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

    private void ShowGames()
    {
        _monitor?.Dispose();
        _monitor = null;
        _wizard?.Dispose();
        _wizard = null;

        ContentHost.Content = new GamesView(_hardware, _layout);
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
