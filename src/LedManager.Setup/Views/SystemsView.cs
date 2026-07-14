using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LedManager.Setup.Controls;
using LedManager.Setup.Data;
using LedManager.Setup.Localization;
using LedManager.Setup.Serial;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Views;

/// <summary>
/// System panel mapper: shows the resolved base template of a system (Data Pack
/// colors + the user's system override), lets the user repaint its buttons from the
/// firmware palette, and writes the sparse override the runtime applies to every
/// game of that system. Per-game panels (arcade dynpanels) live in GamesView.
/// Optional live preview drives the real panel through PicoCommandSender.
/// </summary>
public sealed class SystemsView : UserControl, IDisposable
{
    private static readonly string[] Palette =
    {
        "WHITE", "GRAY", "RED", "GREEN", "BLUE", "YELLOW", "ORANGE", "GOLD", "LEMON",
        "LIME", "CYAN", "TURQUOISE", "AQUA", "TEAL", "PINK", "MAGENTA", "VIOLET", "PURPLE", "BLACK"
    };

    private readonly HardwareDescription _hardware;
    private readonly PanelLayoutDefinition _layoutDefinition;
    private readonly string _pluginRoot;
    private readonly SystemPanelCatalog _catalog;
    private readonly SystemOverrideStore _store;
    private readonly PanelSurface _panel = new() { Interactive = true };
    private readonly ComboBox _systems;
    private readonly ComboBox _layouts;
    private readonly Button _liveTest;
    private readonly TextBlock _status;
    private readonly TextBlock _summary;
    private readonly ContextMenu _palette;
    private readonly ControlsDeployCard _controlsCard = new();

    private IReadOnlyList<SystemPanelCatalog.PanelLayout> _currentLayouts = Array.Empty<SystemPanelCatalog.PanelLayout>();
    private SystemPanelCatalog.PanelLayout? _currentLayout;
    private readonly Dictionary<int, string> _edited = new();
    private int _paletteSlot;
    private bool _loading;
    private PicoSenderHost? _sender;

    public SystemsView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _hardware = hardware;
        _layoutDefinition = layout;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? System.IO.Directory.GetCurrentDirectory();
        _catalog = new SystemPanelCatalog(_pluginRoot);
        _store = new SystemOverrideStore(_pluginRoot);

        _systems = new ComboBox { Width = 180, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _layouts = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _systems.SelectionChanged += (_, _) => OnSystemChanged();
        _layouts.SelectionChanged += (_, _) => OnLayoutChanged();

        _status = new TextBlock { Margin = new Thickness(0, 10, 0, 0), FontSize = 12, Foreground = Text(0x8A, 0x8A, 0x9A), TextWrapping = TextWrapping.Wrap };
        _summary = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 12, Foreground = Text(0xB8, 0xB8, 0xC6), TextWrapping = TextWrapping.Wrap };

        _panel.SlotClicked += OnSlotClicked;
        _panel.TargetClicked += _ => _status.Text = L.T(
            "START/SELECT ne sont pas personnalisables par override pour l'instant.",
            "START/SELECT are not override-customizable yet.");

        _palette = BuildPalette();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = L.T("Système", "System"), Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_systems);
        header.Children.Add(new TextBlock { Text = L.T("Gabarit", "Template"), Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_layouts);
        header.Children.Add(new TextBlock
        {
            Text = L.T($"Pico : {hardware.PicoLabel}", $"Pico: {hardware.PicoLabel}"),
            Foreground = Ui.Brush(Color.FromRgb(0x8A, 0x2B, 0xE2)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Action(L.T("Enregistrer ma configuration", "Save my configuration"), OnSave, primary: true));
        buttons.Children.Add(Action(L.T("Annuler les modifications", "Discard changes"), (_, _) => ReloadOverride()));
        buttons.Children.Add(Action(L.T("Revenir aux couleurs du pack", "Back to pack colors"), OnResetToPack));
        _liveTest = Action(L.T("Tester sur le panneau réel", "Test on the real panel"), OnLiveTest);
        buttons.Children.Add(_liveTest);

        var intro = new TextBlock
        {
            Text = L.T(
                "Personnalisez les couleurs des boutons du gabarit de base d'un système : cliquez un bouton pour "
                + "choisir sa couleur. Votre patch est enregistré dans overrides\\ et appliqué par LedManager à tous "
                + "les jeux du système — le Data Pack n'est jamais modifié. Les panels par jeu (arcade) se règlent "
                + "dans « Mes jeux ».",
                "Customize the button colors of a system's base template: click a button to pick its color. "
                + "Your patch is saved under overrides\\ and applied by LedManager to every game of the system — "
                + "the Data Pack is never modified. Per-game panels (arcade) are edited in \"My games\"."),
            Foreground = Text(0xB8, 0xB8, 0xC6),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            LineHeight = 18
        };

        var panelBorder = new Border
        {
            Background = Ui.Viewport,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = _panel
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(header);
        stack.Children.Add(intro);
        stack.Children.Add(panelBorder);
        stack.Children.Add(_summary);
        stack.Children.Add(buttons);
        stack.Children.Add(_status);
        stack.Children.Add(_controlsCard);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        if (!_catalog.Available)
        {
            _status.Text = L.T(
                "Data Pack introuvable : le dossier APIExpose\\resources\\dynpanels doit exister à côté de LedManager.",
                "Data Pack not found: the APIExpose\\resources\\dynpanels folder must exist next to LedManager.");
            _systems.IsEnabled = false;
            _layouts.IsEnabled = false;
            return;
        }

        foreach (var system in _catalog.ListSystems())
        {
            _systems.Items.Add(system);
        }

        if (_systems.Items.Count > 0)
        {
            _systems.SelectedIndex = 0;
        }
    }

    private string? SelectedSystem => _systems.SelectedItem as string;

    private void OnSystemChanged()
    {
        if (SelectedSystem is null)
        {
            return;
        }

        _loading = true;
        _currentLayouts = _catalog.LoadLayouts(SelectedSystem);
        _layouts.Items.Clear();
        foreach (var layout in _currentLayouts)
        {
            _layouts.Items.Add(layout.Key.Contains(':')
                ? layout.DisplayName
                : layout.PanelButtons + " " + L.T("boutons", "buttons"));
        }

        var defaultIndex = _currentLayouts.ToList().FindIndex(l => !l.Key.Contains(':') && l.PanelButtons == _hardware.ButtonCount);
        if (defaultIndex < 0)
        {
            defaultIndex = _currentLayouts.Count - 1;
        }

        _loading = false;
        _layouts.SelectedIndex = Math.Max(0, defaultIndex);
        ReloadOverride();
        _controlsCard.ShowSystem(SelectedSystem!);
    }

    private void OnLayoutChanged()
    {
        if (_loading || _layouts.SelectedIndex < 0 || _layouts.SelectedIndex >= _currentLayouts.Count)
        {
            return;
        }

        _currentLayout = _currentLayouts[_layouts.SelectedIndex];
        _panel.Build(_layoutDefinition, _currentLayout.PanelButtons, hasStart: true, hasSelect: true);
        Repaint();
    }

    // ----- resolution: pack -> system patch -----

    private string PackColor(int slot)
    {
        return _currentLayout?.BySlot.TryGetValue(slot, out var pack) == true ? pack.Color : "BLACK";
    }

    private void ReloadOverride()
    {
        if (SelectedSystem is null)
        {
            return;
        }

        _edited.Clear();
        foreach (var (slot, color) in _store.LoadSlotColors(SelectedSystem))
        {
            _edited[slot] = color;
        }

        if (_layouts.SelectedIndex >= 0 && _layouts.SelectedIndex < _currentLayouts.Count)
        {
            _currentLayout = _currentLayouts[_layouts.SelectedIndex];
            _panel.Build(_layoutDefinition, _currentLayout.PanelButtons, hasStart: true, hasSelect: true);
        }

        Repaint();
        _status.Text = _store.Exists(SelectedSystem)
            ? L.T("Votre configuration du système est chargée.", "Your system configuration is loaded.")
            : L.T("Aucune configuration personnelle : couleurs du pack.", "No personal configuration: pack colors.");
    }

    private void Repaint()
    {
        foreach (var slot in _panel.Slots)
        {
            var effective = _edited.TryGetValue(slot, out var over) ? over : PackColor(slot);
            _panel.SetSlot(slot, PanelColors.Resolve(effective));
        }

        if (_currentLayout is { } layout)
        {
            _panel.SetTarget("START", PanelColors.Resolve(layout.StartColor));
            _panel.SetTarget("SELECT", PanelColors.Resolve(layout.SelectColor));
        }

        var parts = _panel.Slots.OrderBy(s => s)
            .Where(slot => _edited.ContainsKey(slot))
            .Select(slot => $"B{slot} → {_edited[slot]} " + L.T("(système)", "(system)"))
            .ToList();

        _summary.Text = parts.Count == 0
            ? L.T("Aucune personnalisation — tout vient du pack.", "No customization — everything comes from the pack.")
            : L.T("Personnalisé : ", "Customized: ") + string.Join(" · ", parts);
    }

    // ----- painting -----

    private void OnSlotClicked(int slot)
    {
        _paletteSlot = slot;
        _palette.PlacementTarget = _panel;
        _palette.IsOpen = true;
    }

    private ContextMenu BuildPalette()
    {
        var menu = new ContextMenu();
        var reset = new MenuItem { Header = L.T("Couleur d'origine (retirer ma configuration)", "Original color (remove my configuration)") };
        reset.Click += (_, _) => ApplyColor(null);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        foreach (var color in Palette)
        {
            var item = new MenuItem
            {
                Header = color,
                Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(PanelColors.Resolve(color)),
                    BorderBrush = Brushes.DimGray,
                    BorderThickness = new Thickness(1)
                }
            };
            item.Click += (_, _) => ApplyColor(color);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void ApplyColor(string? color)
    {
        if (color is null || color.Equals(PackColor(_paletteSlot), StringComparison.OrdinalIgnoreCase))
        {
            // painting the pack color back = no patch entry for that slot
            _edited.Remove(_paletteSlot);
        }
        else
        {
            _edited[_paletteSlot] = color;
        }

        Repaint();
        _status.Text = L.T("Modification non enregistrée — cliquez « Enregistrer ma configuration ».",
            "Unsaved change — click \"Save the override\".");
        if (_sender is { IsAlive: true })
        {
            SendLivePreview();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (SelectedSystem is not { } system)
        {
            return;
        }

        var path = _store.Save(system, _edited);
        _status.Text = _edited.Count == 0
            ? L.T("Plus aucune personnalisation : le patch a été retiré.", "No customization left: the patch was removed.")
            : L.T($"Override enregistré ({System.IO.Path.GetFileName(path)}). LedManager l'applique dès la prochaine sélection de jeu.",
                $"Override saved ({System.IO.Path.GetFileName(path)}). LedManager applies it from the next game selection.");
    }

    private void OnResetToPack(object sender, RoutedEventArgs e)
    {
        if (SelectedSystem is not { } system)
        {
            return;
        }

        var confirm = MessageBox.Show(
            L.T($"Supprimer l'override du système {system} ?", $"Delete the {system} system override?"),
            "LedManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _store.Delete(system);
        ReloadOverride();
        _status.Text = L.T("Configuration supprimée.", "Configuration deleted.");
    }

    // ----- live preview on the real panel -----

    private async void OnLiveTest(object sender, RoutedEventArgs e)
    {
        if (_sender is { IsAlive: true })
        {
            StopLiveTest();
            _status.Text = L.T("Test arrêté. Relancez RetroBat ou LedManager.exe pour reprendre la main.",
                "Test stopped. Restart RetroBat or LedManager.exe to hand the panel back.");
            return;
        }

        if (LedManagerProcess.IsRunning())
        {
            var confirm = MessageBox.Show(
                L.T("LedManager occupe le port du Pico. L'arrêter pour tester les couleurs sur le vrai panneau ?",
                    "LedManager holds the Pico's port. Stop it to test the colors on the real panel?"),
                "LedManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            _liveTest.IsEnabled = false;
            await Task.Run(LedManagerProcess.StopAll);
        }

        _liveTest.IsEnabled = false;
        _status.Text = L.T($"Initialisation du Pico {_hardware.PicoLabel}…", $"Initializing Pico {_hardware.PicoLabel}…");
        _sender = PicoSenderHost.Start(_pluginRoot, _hardware.SenderId);
        if (_sender is null)
        {
            _status.Text = L.T("PicoCommandSender.exe introuvable à la racine du plugin.",
                "PicoCommandSender.exe not found at the plugin root.");
            _liveTest.IsEnabled = true;
            return;
        }

        await _sender.WaitForReadyAsync(TimeSpan.FromSeconds(30));
        _liveTest.IsEnabled = true;
        if (!_sender.IsAlive)
        {
            _status.Text = L.T("Le pilote PicoCommandSender s'est arrêté. Pico branché et allumé ?",
                "The PicoCommandSender driver stopped. Pico plugged in and powered?");
            StopLiveTest();
            return;
        }

        SendLivePreview();
        _liveTest.Content = L.T("Arrêter le test", "Stop the test");
        _status.Text = L.T($"Couleurs envoyées au panneau réel ({_hardware.PicoLabel}) — elles suivent vos clics en direct.",
            $"Colors sent to the real panel ({_hardware.PicoLabel}) — they follow your clicks live.");
    }

    private void SendLivePreview()
    {
        if (_sender is not { IsAlive: true })
        {
            return;
        }

        _sender.Send("CLEAR");
        foreach (var slot in _panel.Slots)
        {
            var effective = _edited.TryGetValue(slot, out var over) ? over : PackColor(slot);
            _sender.Send($"SLOT {slot} {effective}");
        }

        if (_currentLayout is { } layout)
        {
            _sender.Send($"SET START {layout.StartColor}");
            _sender.Send($"SET SELECT {layout.SelectColor}");
        }
    }

    private void StopLiveTest()
    {
        _sender?.Dispose();
        _sender = null;
        _liveTest.Content = L.T("Tester sur le panneau réel", "Test on the real panel");
        _liveTest.IsEnabled = true;
    }

    private static Button Action(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = primary ? FontWeights.Bold : FontWeights.Normal
        };
        if (primary)
        {
            button.Style = (Style)Application.Current.FindResource("AccentButton");
        }

        button.Click += onClick;
        return button;
    }

    private static SolidColorBrush Text(byte r, byte g, byte b) => Ui.Text(r, g, b);

    public void Dispose()
    {
        StopLiveTest();
    }
}
