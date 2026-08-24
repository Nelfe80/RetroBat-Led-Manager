using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LedManager.Setup.Controls;
using LedManager.Setup.Data;
using LedManager.Setup.Localization;
using LedManager.Setup.Serial;
using Path = System.IO.Path;

namespace LedManager.Setup.Views;

/// <summary>
/// P3 dashboard, the landing view: one status card per link of the chain
/// (LedManager process, Pico/COM, APIExpose, virtual-panel mirror, Data Pack,
/// user overrides), each refreshed asynchronously so a dead link never blocks.
/// </summary>
public sealed class HomeView : UserControl
{
    private sealed record Card(Ellipse Dot, TextBlock Text, StackPanel Actions);

    private readonly HardwareDescription _hardware;
    private readonly string _pluginRoot;
    private readonly Card _manager;
    private readonly Card _pico;
    private readonly Card _api;
    private readonly Card _pack;
    private readonly Card _overrides;
    private bool _busy;

    public HomeView(HardwareDescription hardware)
    {
        _hardware = hardware;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? Directory.GetCurrentDirectory();

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("État de l'installation", "Installation status"),
            Foreground = Text(0xE8, 0xE8, 0xF0),
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Chaque maillon de la chaîne, du flux APIExpose jusqu'aux LEDs.",
                "Every link of the chain, from the APIExpose stream to the LEDs."),
            Foreground = Text(0x8A, 0x8A, 0x9A),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 14)
        });

        _manager = AddCard(stack, "LedManager");
        _pico = AddCard(stack, "Pico");
        _api = AddCard(stack, "APIExpose");
        _pack = AddCard(stack, "Data Pack");
        _overrides = AddCard(stack, L.T("Personnalisations", "Customizations"));

        var refresh = new Button
        {
            Content = L.T("Rafraîchir", "Refresh"),
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        refresh.Click += (_, _) => _ = RefreshAsync();
        stack.Children.Add(refresh);

        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _ = RefreshAsync();
    }

    private static Card AddCard(StackPanel parent, string title)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse { Width = 12, Height = 12, Fill = Ui.Brush(Color.FromRgb(0x8A, 0x8A, 0x9A)), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var name = new TextBlock { Text = title, Foreground = Text(0xE8, 0xE8, 0xF0), FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var text = new TextBlock { Foreground = Text(0xB8, 0xB8, 0xC6), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        parent.Children.Add(new Border
        {
            Background = Ui.Brush(Color.FromRgb(0x1D, 0x1D, 0x2A)),
            BorderBrush = Ui.Brush(Color.FromRgb(0x2E, 0x2E, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid
        });

        return new Card(dot, text, actions);
    }

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            RefreshManager();
            RefreshPack();
            RefreshOverrides();
            RefreshPico();
            await RefreshApiAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private void RefreshManager()
    {
        var running = LedManagerProcess.IsRunning();
        SetState(_manager, running, null,
            running
                ? L.T("En cours d'exécution - vos LEDs suivent RetroBat.", "Running - your LEDs follow RetroBat.")
                : L.T("Arrêté. Il démarre normalement avec RetroBat.", "Stopped. It normally starts with RetroBat."));

        _manager.Actions.Children.Clear();
        var button = new Button
        {
            Content = running ? L.T("Arrêter", "Stop") : L.T("Démarrer", "Start"),
            Padding = new Thickness(12, 5, 12, 5)
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            if (LedManagerProcess.IsRunning())
            {
                await Task.Run(LedManagerProcess.StopAll);
            }
            else
            {
                var exe = Path.Combine(_pluginRoot, "LedManager.exe");
                if (File.Exists(exe))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        WorkingDirectory = _pluginRoot,
                        UseShellExecute = true
                    });
                    await Task.Delay(1200);
                }
            }

            button.IsEnabled = true;
            await RefreshAsync();
        };
        _manager.Actions.Children.Add(button);
    }

    private void RefreshPico()
    {
        _pico.Actions.Children.Clear();
        if (LedManagerProcess.IsRunning())
        {
            SetState(_pico, true, null,
                L.T($"Port {_hardware.SerialPort} · {_hardware.BaudRate} bauds - occupé par LedManager (normal en fonctionnement).",
                    $"Port {_hardware.SerialPort} · {_hardware.BaudRate} baud - held by LedManager (normal while running)."));
            return;
        }

        SetState(_pico, null, null,
            L.T($"Port {_hardware.SerialPort} · {_hardware.BaudRate} bauds · {_hardware.ButtonCount} boutons.",
                $"Port {_hardware.SerialPort} · {_hardware.BaudRate} baud · {_hardware.ButtonCount} buttons."));

        var detect = new Button { Content = L.T("Détecter", "Detect"), Padding = new Thickness(12, 5, 12, 5) };
        detect.Click += async (_, _) =>
        {
            detect.IsEnabled = false;
            _pico.Text.Text = L.T("Recherche du Pico…", "Scanning for the Pico…");
            var result = await PicoDetector.DetectAsync(_hardware.SerialPort);
            SetState(_pico, result.Found, null, result.Message);
            detect.IsEnabled = true;
        };
        _pico.Actions.Children.Add(detect);
    }

    private async Task RefreshApiAsync()
    {
        var baseUrl = "ws://127.0.0.1:12345";
        var ini = Path.Combine(_pluginRoot, "LedManager.ini");
        if (File.Exists(ini))
        {
            baseUrl = LedManager.Core.Ini.IniDocument.Load(ini).Get("APIExpose", "BaseUrl", baseUrl);
        }

        _api.Text.Text = baseUrl + " - " + L.T("test en cours…", "testing…");
        var alive = await ApiExposeProbe.IsAliveAsync(baseUrl);
        var mirror = await ApiExposeProbe.IsMirrorAliveAsync(_hardware.MirrorPort);
        SetState(_api, alive, null,
            (alive
                ? baseUrl + L.T(" - connecté.", " - connected.")
                : baseUrl + L.T(" - injoignable (RetroBat/APIExpose arrêté ?).", " - unreachable (RetroBat/APIExpose not running?)."))
            + " " + (mirror
                ? L.T($"Miroir panel virtuel actif (port {_hardware.MirrorPort}).", $"Virtual panel mirror active (port {_hardware.MirrorPort}).")
                : L.T("Miroir panel virtuel inactif.", "Virtual panel mirror inactive.")));
    }

    private void RefreshPack()
    {
        var catalog = new SystemPanelCatalog(_pluginRoot);
        var games = new GamePanelCatalog(_pluginRoot);
        if (!catalog.Available)
        {
            SetState(_pack, false, null,
                L.T("Introuvable : le dossier APIExpose\\resources\\dynpanels doit exister à côté de LedManager.",
                    "Not found: the APIExpose\\resources\\dynpanels folder must exist next to LedManager."));
            return;
        }

        SetState(_pack, true, null,
            L.T($"{catalog.ListSystems().Count} systèmes et {games.ListGames().Count} jeux arcade curatés.",
                $"{catalog.ListSystems().Count} systems and {games.ListGames().Count} curated arcade games."));
    }

    private void RefreshOverrides()
    {
        int systems = 0, games = 0;
        var systemsDir = Path.Combine(_pluginRoot, "overrides", "systems");
        var gamesDir = Path.Combine(_pluginRoot, "overrides", "games");
        if (Directory.Exists(systemsDir))
        {
            systems = Directory.GetFiles(systemsDir, "*.json").Length;
        }

        if (Directory.Exists(gamesDir))
        {
            games = Directory.GetFiles(gamesDir, "*.json", SearchOption.AllDirectories).Length;
        }

        SetState(_overrides, systems + games > 0 ? true : null, null,
            systems + games == 0
                ? L.T("Aucune configuration personnelle : tout vient du pack. Personnalisez dans « Mes jeux ».",
                    "No override: everything comes from the pack. Customize in the \"My games\" tab.")
                : L.T($"{systems} système(s) et {games} jeu(x) personnalisés - gérés dans « Mes jeux ».",
                    $"{systems} customized system(s) and {games} game(s) - managed in \"My games\"."));
    }

    /// <summary>true = green, false = red, null = neutral gray.</summary>
    private static void SetState(Card card, bool? ok, string? _, string text)
    {
        card.Dot.Fill = new SolidColorBrush(ok switch
        {
            true => Color.FromRgb(0x30, 0xE8, 0x50),
            false => Color.FromRgb(0xE8, 0x5C, 0x5C),
            null => Color.FromRgb(0x8A, 0x8A, 0x9A)
        });
        card.Text.Text = text;
    }

    private static SolidColorBrush Text(byte r, byte g, byte b) => Ui.Text(r, g, b);
}
