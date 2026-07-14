using System.Diagnostics;
using System.Windows;
using LedManager.Setup.Controls;
using LedManager.Setup.Localization;
using LedManager.Setup.Serial;
using LedManager.Setup.Views;

namespace LedManager.Setup;

/// <summary>
/// Shell: sidebar navigation between the live Panel monitor (read-only mirror of
/// LedManager) and the hardware Wizard (direct Pico control while LedManager is
/// stopped). The two are naturally exclusive on the COM port.
/// </summary>
public partial class MainWindow : Window
{
    private readonly string? _root;
    private readonly PanelLayoutDefinition _layout;
    private HardwareDescription _hardware;
    private IReadOnlyList<PicoIdentity> _picos;
    private MonitorView? _monitor;
    private WizardView? _wizard;
    private SystemsView? _systems;
    private GamesView? _games;

    public MainWindow()
    {
        InitializeComponent();
        TryLowerProcessPriority();

        NavHome.Content = L.T("Accueil", "Home");
        NavMonitor.Content = L.T("Panel virtuel", "Virtual panel");
        NavSystems.Content = L.T("Mes systèmes", "My systems");
        NavGames.Content = L.T("Mes jeux", "My games");
        NavWizard.Content = L.T("Assistant matériel", "Hardware assistant");
        PicoSelectorLabel.Text = L.T("PICO CONFIGURÉ", "CONFIGURED PICO");
        IdentifyButton.Content = L.T("Identifier (clignote)", "Identify (blinks)");

        _root = HardwareDescription.FindPluginRoot();
        _hardware = HardwareDescription.Load(_root);
        _layout = PanelLayoutDefinition.Load(_root);
        _picos = HardwareDescription.ListPicos(_root);

        foreach (var pico in _picos)
        {
            PicoSelector.Items.Add(pico.Label);
        }

        PicoSelector.SelectedIndex = Math.Max(0, _picos.ToList().FindIndex(p => p.SenderId == _hardware.SenderId));
        UpdateHardwareInfo();

        ShowHome();

        Closed += (_, _) =>
        {
            _monitor?.Dispose();
            _wizard?.Dispose();
            _systems?.Dispose();
            _games?.Dispose();
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
        NavHome.IsChecked = true;
        await System.Threading.Tasks.Task.Delay(3000); // async probes settle
        SaveScreenshot(directory, "setup-home");
        NavMonitor.IsChecked = true;
        await System.Threading.Tasks.Task.Delay(1200);
        SaveScreenshot(directory, "setup-monitor");
        NavSystems.IsChecked = true;
        await System.Threading.Tasks.Task.Delay(1200);
        SaveScreenshot(directory, "setup-systems");
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

    private void UpdateHardwareInfo()
    {
        if (HardwareInfo is null)
        {
            return;
        }

        HardwareInfo.Text = $"{_hardware.ButtonCount} " + L.T("boutons", "buttons")
            + (_hardware.HasStart ? " · START" : "")
            + (_hardware.HasSelect ? " · SELECT" : "")
            + $"\nPort {_hardware.SerialPort} · {_hardware.BaudRate} bauds"
            + "\n" + L.T("Miroir", "Mirror") + $" 127.0.0.1:{_hardware.MirrorPort}";
    }

    /// <summary>The user picked another Pico: every view now configures THAT panel.
    /// The panel blinks so the user sees which physical panel is selected.</summary>
    private void PicoSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PicoSelector.SelectedIndex < 0 || PicoSelector.SelectedIndex >= _picos.Count)
        {
            return;
        }

        var pico = _picos[PicoSelector.SelectedIndex];
        if (pico.SenderId == _hardware.SenderId)
        {
            return; // initial selection, nothing changes
        }

        _hardware = HardwareDescription.Load(_root, pico.SenderId);
        UpdateHardwareInfo();
        RebuildActiveView();
        _ = IdentifyAsync();
    }

    private void IdentifyButton_Click(object sender, RoutedEventArgs e) => _ = IdentifyAsync();

    private async System.Threading.Tasks.Task IdentifyAsync()
    {
        if (!IdentifyButton.IsEnabled)
        {
            return;
        }

        IdentifyButton.IsEnabled = false;
        PicoSelector.IsEnabled = false;
        PicoStatus.Text = L.T($"Identification de {_hardware.PicoLabel}…", $"Identifying {_hardware.PicoLabel}…");
        try
        {
            PicoStatus.Text = await PicoIdentifier.BlinkAsync(
                _root ?? System.IO.Directory.GetCurrentDirectory(), _hardware.SenderId);
        }
        finally
        {
            IdentifyButton.IsEnabled = true;
            PicoSelector.IsEnabled = true;
        }
    }

    private void RebuildActiveView()
    {
        if (NavMonitor.IsChecked == true)
        {
            ShowMonitor();
        }
        else if (NavSystems.IsChecked == true)
        {
            ShowSystems();
        }
        else if (NavGames.IsChecked == true)
        {
            ShowGames();
        }
        else if (NavWizard.IsChecked == true)
        {
            ShowWizard();
        }
        else
        {
            ShowHome();
        }
    }

    private void NavHome_Checked(object sender, RoutedEventArgs e) => ShowHome();

    private void NavSystems_Checked(object sender, RoutedEventArgs e) => ShowSystems();

    private void NavMonitor_Checked(object sender, RoutedEventArgs e) => ShowMonitor();

    private void NavGames_Checked(object sender, RoutedEventArgs e) => ShowGames();

    private void NavWizard_Checked(object sender, RoutedEventArgs e) => ShowWizard();

    private void DisposeViews()
    {
        _monitor?.Dispose();
        _monitor = null;
        _wizard?.Dispose();
        _wizard = null;
        _systems?.Dispose();
        _systems = null;
        _games?.Dispose();
        _games = null;
    }

    private void ShowHome()
    {
        if (ContentHost is null)
        {
            return;
        }

        DisposeViews();
        ContentHost.Content = new HomeView(_hardware);
    }

    private void ShowMonitor()
    {
        if (!IsLoaded && ContentHost is null)
        {
            return;
        }

        DisposeViews();
        _monitor = new MonitorView(_hardware, _layout);
        ContentHost.Content = _monitor;
        _monitor.Activate();
    }

    private void ShowSystems()
    {
        DisposeViews();
        _systems = new SystemsView(_hardware, _layout);
        ContentHost.Content = _systems;
    }

    private void ShowGames()
    {
        DisposeViews();
        _games = new GamesView(_hardware, _layout);
        ContentHost.Content = _games;
    }

    private void ShowWizard()
    {
        DisposeViews();
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
