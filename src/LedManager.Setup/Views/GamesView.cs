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
/// P2 game mapper: shows the resolved panel of a system — or of one arcade game —
/// (Data Pack colors + the user's override patches), lets the user repaint buttons
/// from the firmware palette, and writes the sparse overrides the runtime applies
/// live at the next game selection. Optional live preview drives the real panel
/// through PicoCommandSender (LedManager stopped meanwhile).
/// </summary>
public sealed class GamesView : UserControl, IDisposable
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
    private readonly GamePanelCatalog _games;
    private readonly SystemOverrideStore _store;
    private readonly PanelSurface _panel = new() { Interactive = true };
    private readonly ComboBox _systems;
    private readonly ComboBox _layouts;
    private readonly TextBox _gameSearch;
    private readonly ListBox _gameList;
    private readonly Button _backToSystem;
    private readonly Button _liveTest;
    private readonly TextBlock _status;
    private readonly TextBlock _summary;
    private readonly ContextMenu _palette;

    private IReadOnlyList<SystemPanelCatalog.PanelLayout> _currentLayouts = Array.Empty<SystemPanelCatalog.PanelLayout>();
    private SystemPanelCatalog.PanelLayout? _currentLayout;
    private GamePanelCatalog.GamePanel? _currentGame;
    private IReadOnlyDictionary<int, string> _systemPatch = new Dictionary<int, string>();
    private readonly Dictionary<int, string> _edited = new();
    private int _paletteSlot;
    private bool _loading;
    private PicoSenderHost? _sender;

    private bool GameMode => _currentGame != null;

    public GamesView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _hardware = hardware;
        _layoutDefinition = layout;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? System.IO.Directory.GetCurrentDirectory();
        _catalog = new SystemPanelCatalog(_pluginRoot);
        _games = new GamePanelCatalog(_pluginRoot);
        _store = new SystemOverrideStore(_pluginRoot);

        _systems = new ComboBox { Width = 180, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _layouts = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _systems.SelectionChanged += (_, _) => OnSystemChanged();
        _layouts.SelectionChanged += (_, _) => OnLayoutChanged();

        _gameSearch = new TextBox { Width = 180, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _gameSearch.TextChanged += (_, _) => RefreshGameList();
        _gameList = new ListBox { Width = 180, MaxHeight = 96, FontSize = 12 };
        _gameList.SelectionChanged += (_, _) => OnGameSelected();
        _backToSystem = Action(L.T("← Système entier", "← Whole system"), (_, _) => ExitGameMode());
        _backToSystem.Visibility = Visibility.Collapsed;

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
        header.Children.Add(new TextBlock { Text = "Panel", Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_layouts);
        header.Children.Add(_backToSystem);

        // arcade game picker (curated per-game dynpanels)
        var gameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        gameRow.Children.Add(new TextBlock { Text = L.T("Jeu arcade", "Arcade game"), Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        var gamePicker = new StackPanel();
        gamePicker.Children.Add(_gameSearch);
        gamePicker.Children.Add(_gameList);
        gameRow.Children.Add(gamePicker);
        gameRow.Children.Add(new TextBlock
        {
            Text = L.T("Tapez un nom de rom (ex. mslug, chasehq, seawolf) pour éditer un jeu précis.",
                "Type a rom name (e.g. mslug, chasehq, seawolf) to edit a specific game."),
            Foreground = Text(0x8A, 0x8A, 0x9A),
            FontSize = 11,
            Margin = new Thickness(10, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Action(L.T("Enregistrer l'override", "Save the override"), OnSave, primary: true));
        buttons.Children.Add(Action(L.T("Annuler les modifications", "Discard changes"), (_, _) => ReloadOverride()));
        buttons.Children.Add(Action(L.T("Revenir aux couleurs du pack", "Back to pack colors"), OnResetToPack));
        _liveTest = Action(L.T("Tester sur le panneau réel", "Test on the real panel"), OnLiveTest);
        buttons.Children.Add(_liveTest);

        var intro = new TextBlock
        {
            Text = L.T(
                "Cliquez un bouton du panel pour choisir sa couleur : votre patch est enregistré dans overrides\\ "
                + "et appliqué par LedManager dès la prochaine sélection de jeu — le Data Pack n'est jamais modifié. "
                + "Un patch de jeu gagne sur le patch système, qui gagne sur le pack.",
                "Click a panel button to pick its color: your patch is saved under overrides\\ and applied by "
                + "LedManager from the next game selection — the Data Pack is never modified. "
                + "A game patch beats the system patch, which beats the pack."),
            Foreground = Text(0xB8, 0xB8, 0xC6),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            LineHeight = 18
        };

        var panelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x2A)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = _panel
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(header);
        stack.Children.Add(gameRow);
        stack.Children.Add(intro);
        stack.Children.Add(panelBorder);
        stack.Children.Add(_summary);
        stack.Children.Add(buttons);
        stack.Children.Add(_status);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        if (!_catalog.Available)
        {
            _status.Text = L.T(
                "Data Pack introuvable : le dossier APIExpose\\resources\\dynpanels doit exister à côté de LedManager.",
                "Data Pack not found: the APIExpose\\resources\\dynpanels folder must exist next to LedManager.");
            _systems.IsEnabled = false;
            _layouts.IsEnabled = false;
            _gameSearch.IsEnabled = false;
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

        RefreshGameList();
    }

    private string? SelectedSystem => _systems.SelectedItem as string;

    private string LayoutIdForHardware => $"{_hardware.ButtonCount}-Button";

    // ----- system mode -----

    private void OnSystemChanged()
    {
        if (SelectedSystem is not { } system || GameMode)
        {
            if (GameMode)
            {
                ExitGameMode();
            }

            if (SelectedSystem is null)
            {
                return;
            }
        }

        _loading = true;
        _currentLayouts = _catalog.LoadLayouts(SelectedSystem!);
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
    }

    private void OnLayoutChanged()
    {
        if (_loading || GameMode || _layouts.SelectedIndex < 0 || _layouts.SelectedIndex >= _currentLayouts.Count)
        {
            return;
        }

        _currentLayout = _currentLayouts[_layouts.SelectedIndex];
        _panel.Build(_layoutDefinition, _currentLayout.PanelButtons, hasStart: true, hasSelect: true);
        Repaint();
    }

    // ----- game mode -----

    private void RefreshGameList()
    {
        var filter = _gameSearch.Text.Trim();
        _gameList.Items.Clear();
        if (filter.Length < 2)
        {
            return;
        }

        foreach (var rom in _games.ListGames()
                     .Where(rom => rom.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .Take(50))
        {
            _gameList.Items.Add(rom);
        }
    }

    private void OnGameSelected()
    {
        if (_gameList.SelectedItem is not string rom)
        {
            return;
        }

        var game = _games.Load(rom, LayoutIdForHardware);
        if (game == null)
        {
            _status.Text = L.T($"Impossible de lire le dynpanel de {rom}.", $"Could not read {rom}'s dynpanel.");
            return;
        }

        _currentGame = game;
        _backToSystem.Visibility = Visibility.Visible;
        _layouts.IsEnabled = false;
        _panel.Build(_layoutDefinition, _hardware.ButtonCount, hasStart: true, hasSelect: true);
        ReloadOverride();
    }

    private void ExitGameMode()
    {
        _currentGame = null;
        _gameList.SelectedItem = null;
        _backToSystem.Visibility = Visibility.Collapsed;
        _layouts.IsEnabled = true;
        OnLayoutChanged();
        ReloadOverride();
    }

    // ----- resolution: pack -> system patch -> game patch -----

    /// <summary>Pack color of a slot in the current context (system layout or game dynpanel).</summary>
    private string PackColor(int slot)
    {
        if (_currentGame is { } game)
        {
            return game.BySlot.TryGetValue(slot, out var entry) ? entry.Color : "BLACK";
        }

        return _currentLayout?.BySlot.TryGetValue(slot, out var pack) == true ? pack.Color : "BLACK";
    }

    /// <summary>What the runtime would show WITHOUT the patch currently being edited.</summary>
    private string BaselineColor(int slot)
    {
        if (GameMode && _systemPatch.TryGetValue(slot, out var fromSystem))
        {
            return fromSystem;
        }

        return PackColor(slot);
    }

    private void ReloadOverride()
    {
        if (SelectedSystem is null && !GameMode)
        {
            return;
        }

        // arcade/mame are interchangeable for the runtime; the setup reads whichever
        // file exists and always writes the canonical "arcade" one
        var overrideSystem = _currentGame?.OverrideSystem ?? SelectedSystem!;
        _systemPatch = _store.LoadSlotColors(overrideSystem);
        if (_systemPatch.Count == 0 && _currentGame is { } aliased && aliased.OverrideSystem != aliased.System)
        {
            _systemPatch = _store.LoadSlotColors(aliased.System);
        }

        _edited.Clear();
        var saved = _currentGame is { } game
            ? FirstNonEmpty(
                _store.LoadGameSlotColors(game.OverrideSystem, game.Rom),
                game.OverrideSystem != game.System ? _store.LoadGameSlotColors(game.System, game.Rom) : null)
            : _systemPatch;
        foreach (var (slot, color) in saved)
        {
            _edited[slot] = color;
        }

        if (!GameMode && _layouts.SelectedIndex >= 0 && _layouts.SelectedIndex < _currentLayouts.Count)
        {
            _currentLayout = _currentLayouts[_layouts.SelectedIndex];
            _panel.Build(_layoutDefinition, _currentLayout.PanelButtons, hasStart: true, hasSelect: true);
        }

        Repaint();
        _status.Text = GameMode
            ? L.T($"Jeu {_currentGame!.GameName} ({_currentGame.Rom}, système {_currentGame.System}).",
                $"Game {_currentGame!.GameName} ({_currentGame.Rom}, system {_currentGame.System}).")
            : _store.Exists(overrideSystem)
                ? L.T("Override système chargé.", "System override loaded.")
                : L.T("Aucun override : couleurs du pack.", "No override: pack colors.");
    }

    private void Repaint()
    {
        foreach (var slot in _panel.Slots)
        {
            var effective = _edited.TryGetValue(slot, out var over) ? over : BaselineColor(slot);
            _panel.SetSlot(slot, PanelColors.Resolve(effective));
        }

        if (!GameMode && _currentLayout is { } layout)
        {
            _panel.SetTarget("START", PanelColors.Resolve(layout.StartColor));
            _panel.SetTarget("SELECT", PanelColors.Resolve(layout.SelectColor));
        }
        else
        {
            _panel.SetTarget("START", PanelColors.Resolve("GRAY"));
            _panel.SetTarget("SELECT", PanelColors.Resolve("GRAY"));
        }

        // origin summary: which patch level drives each customized slot
        var parts = new List<string>();
        foreach (var slot in _panel.Slots.OrderBy(s => s))
        {
            if (_edited.TryGetValue(slot, out var over))
            {
                parts.Add($"B{slot} → {over} " + (GameMode ? L.T("(jeu)", "(game)") : L.T("(système)", "(system)")));
            }
            else if (GameMode && _systemPatch.ContainsKey(slot))
            {
                parts.Add($"B{slot} → {_systemPatch[slot]} " + L.T("(système)", "(system)"));
            }
        }

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
        var reset = new MenuItem { Header = L.T("Couleur d'origine (retirer l'override)", "Original color (remove override)") };
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
        if (color is null || color.Equals(BaselineColor(_paletteSlot), StringComparison.OrdinalIgnoreCase))
        {
            // painting the baseline color back = no patch entry for that slot
            _edited.Remove(_paletteSlot);
        }
        else
        {
            _edited[_paletteSlot] = color;
        }

        Repaint();
        _status.Text = L.T("Modification non enregistrée — cliquez « Enregistrer l'override ».",
            "Unsaved change — click \"Save the override\".");
        if (_sender is { IsAlive: true })
        {
            SendLivePreview();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string path;
        if (_currentGame is { } game)
        {
            path = _store.SaveGame(game.OverrideSystem, game.Rom, _edited);
        }
        else if (SelectedSystem is { } system)
        {
            path = _store.Save(system, _edited);
        }
        else
        {
            return;
        }

        _status.Text = _edited.Count == 0
            ? L.T("Plus aucune personnalisation : le patch a été retiré.", "No customization left: the patch was removed.")
            : L.T($"Override enregistré ({System.IO.Path.GetFileName(path)}). LedManager l'applique dès la prochaine sélection de jeu.",
                $"Override saved ({System.IO.Path.GetFileName(path)}). LedManager applies it from the next game selection.");
    }

    private void OnResetToPack(object sender, RoutedEventArgs e)
    {
        var what = _currentGame is { } game
            ? L.T($"l'override du jeu {game.Rom}", $"the {game.Rom} game override")
            : L.T($"l'override du système {SelectedSystem}", $"the {SelectedSystem} system override");
        var confirm = MessageBox.Show(
            L.T($"Supprimer {what} ?", $"Delete {what}?"),
            "LedManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (_currentGame is { } g)
        {
            _store.DeleteGame(g.OverrideSystem, g.Rom);
            if (g.OverrideSystem != g.System)
            {
                _store.DeleteGame(g.System, g.Rom);
            }
        }
        else if (SelectedSystem is { } system)
        {
            _store.Delete(system);
        }

        ReloadOverride();
        _status.Text = L.T("Override supprimé.", "Override deleted.");
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
        _status.Text = L.T("Initialisation du Pico…", "Initializing the Pico…");
        _sender = PicoSenderHost.Start(_pluginRoot);
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
        _status.Text = L.T("Couleurs envoyées au panneau réel — elles suivent vos clics en direct.",
            "Colors sent to the real panel — they follow your clicks live.");
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
            var effective = _edited.TryGetValue(slot, out var over) ? over : BaselineColor(slot);
            _sender.Send($"SLOT {slot} {effective}");
        }

        if (!GameMode && _currentLayout is { } layout)
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

    private static IReadOnlyDictionary<int, string> FirstNonEmpty(
        IReadOnlyDictionary<int, string> primary, IReadOnlyDictionary<int, string>? fallback)
        => primary.Count > 0 || fallback is null ? primary : fallback;

    private static Button Action(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = primary ? FontWeights.Bold : FontWeights.Normal
        };
        button.Click += onClick;
        return button;
    }

    private static SolidColorBrush Text(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    public void Dispose()
    {
        StopLiveTest();
    }
}
