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
/// Per-game panel mapper: pick a system (arcade…), then one of its curated game
/// dynpanels, see its resolved colors (Data Pack + system patch + game patch) and
/// repaint buttons from the firmware palette. The sparse game override is applied by
/// the runtime at the next game selection. System base templates are edited in
/// SystemsView. Optional live preview drives the real panel through PicoCommandSender.
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
    private readonly GamePanelCatalog _games;
    private readonly SystemOverrideStore _store;
    private readonly PanelSurface _panel = new() { Interactive = true };
    private readonly ComboBox _systems;
    private readonly TextBox _gameSearch;
    private readonly ListBox _gameList;
    private readonly Button _liveTest;
    private readonly TextBlock _status;
    private readonly TextBlock _summary;
    private readonly ContextMenu _palette;
    private readonly ControlsDeployCard _controlsCard = new();

    /// <summary>Systems whose games use the arcade per-game dynpanels.</summary>
    private static readonly HashSet<string> ArcadeFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "mame", "arcade", "fbneo", "neogeo", "neogeocd", "hbmame"
    };

    private SystemPanelCatalog _catalog = null!;
    private IReadOnlyList<string> _dynpanelRoms = Array.Empty<string>();
    private readonly Dictionary<string, HashSet<string>> _installedRomsBySystem = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _namesBySystem = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _namesLoading = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string>? _packNames;
    private GamePanelCatalog.GamePanel? _currentGame;
    private IReadOnlyDictionary<int, string> _systemPatch = new Dictionary<int, string>();
    private readonly Dictionary<int, string> _edited = new();
    private int _paletteSlot;
    private PicoSenderHost? _sender;

    public GamesView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _hardware = hardware;
        _layoutDefinition = layout;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? System.IO.Directory.GetCurrentDirectory();
        _games = new GamePanelCatalog(_pluginRoot);
        _store = new SystemOverrideStore(_pluginRoot);

        _systems = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _systems.SelectionChanged += (_, _) => RefreshGameList();

        _gameSearch = new TextBox { Width = 400, Margin = new Thickness(0, 0, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
        _gameSearch.TextChanged += (_, _) => RefreshGameList();
        _gameList = new ListBox { Width = 400, MaxHeight = 190, FontSize = 12 };
        _gameList.SelectionChanged += (_, _) => OnGameSelected();

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
        header.Children.Add(new TextBlock
        {
            Text = L.T($"Pico : {hardware.PicoLabel}", $"Pico: {hardware.PicoLabel}"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x2B, 0xE2)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var gameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        gameRow.Children.Add(new TextBlock { Text = L.T("Jeu", "Game"), Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        var gamePicker = new StackPanel();
        gamePicker.Children.Add(_gameSearch);
        gamePicker.Children.Add(_gameList);
        gameRow.Children.Add(gamePicker);
        gameRow.Children.Add(new TextBlock
        {
            Text = L.T("Cherchez par nom de rom ou nom du jeu (ex. 1943, Metal Slug, chasehq).",
                "Search by rom name or game name (e.g. 1943, Metal Slug, chasehq)."),
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
                "Choisissez un système puis un jeu : le panel du jeu s'affiche avec ses couleurs résolues "
                + "(pack + override système + override jeu). Cliquez un bouton pour choisir sa couleur : le patch "
                + "du jeu est enregistré dans overrides\\ et gagne sur le patch système, qui gagne sur le pack. "
                + "Le gabarit de base d'un système se personnalise dans « Mes systèmes ».",
                "Pick a system then a game: the game's panel shows its resolved colors "
                + "(pack + system override + game override). Click a button to pick its color: the game patch "
                + "is saved under overrides\\ and beats the system patch, which beats the pack. "
                + "A system's base template is customized in \"My systems\"."),
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
        stack.Children.Add(_controlsCard);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _controlsCard.ShowNone(L.T("Choisissez un jeu pour voir ses contrôles.", "Pick a game to see its controls."));

        if (!_games.Available)
        {
            _status.Text = L.T(
                "Data Pack introuvable : le dossier APIExpose\\resources\\dynpanels doit exister à côté de LedManager.",
                "Data Pack not found: the APIExpose\\resources\\dynpanels folder must exist next to LedManager.");
            _systems.IsEnabled = false;
            _gameSearch.IsEnabled = false;
            return;
        }

        _panel.Build(_layoutDefinition, _hardware.ButtonCount, hasStart: true, hasSelect: true);

        // Same system list as SystemsView (instant); the rom list comes from the
        // per-game dynpanel FILE NAMES (instant); the rom → game-name index for
        // name search loads in the background. The roms MUST be known before the
        // combo selection fires RefreshGameList, or the name index gets built (and
        // cached) against an empty rom set and name search stays dead.
        _dynpanelRoms = _games.ListGames();

        _catalog = new SystemPanelCatalog(_pluginRoot);
        if (_catalog.Available)
        {
            foreach (var system in _catalog.ListSystems())
            {
                _systems.Items.Add(system);
            }

            var mame = _systems.Items.IndexOf("mame");
            _systems.SelectedIndex = mame >= 0 ? mame : 0;
        }

        RefreshGameList();
    }

    /// <summary>
    /// rom → display name for a system. Primary source: the APIExpose gateway
    /// (GET /api/v1/gamelists/{system}/games — the names EmulationStation shows).
    /// Fallbacks when the API is down: the user's roms\système\gamelist.xml read
    /// locally, then the pack's arcade_lt.json. All parsed off the UI thread and
    /// restricted to the roms that have a dynpanel; rom search works immediately,
    /// name search once loaded.
    /// </summary>
    private async Task EnsureNamesAsync(string system)
    {
        var isArcade = ArcadeFamily.Contains(system);
        if ((isArcade && _dynpanelRoms.Count == 0) || _namesBySystem.ContainsKey(system) || !_namesLoading.Add(system))
        {
            return;
        }

        try
        {
            // Arcade: only the roms with a per-game dynpanel matter. Consoles: the
            // whole installed gamelist is the catalog (baseline = system template).
            var roms = isArcade ? _dynpanelRoms.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            var fromApi = await LoadNamesFromApiAsync(system, roms);
            if (fromApi is not null)
            {
                _namesBySystem[system] = fromApi;
            }
            else
            {
                var fromGamelist = await Task.Run(() => LoadNamesFromGamelist(system, roms));
                if (fromGamelist is not null)
                {
                    _namesBySystem[system] = fromGamelist;
                }
                else if (isArcade && _packNames is null)
                {
                    _packNames = await Task.Run(() => LoadNamesFromPack(roms!));
                }
            }
        }
        finally
        {
            _namesLoading.Remove(system);
        }

        RefreshGameList();
    }

    private async Task<IReadOnlyDictionary<string, string>?> LoadNamesFromApiAsync(string system, HashSet<string>? roms)
    {
        var baseUrl = ApiExposeClient.ResolveBaseUrl(_pluginRoot);
        var (ok, body) = await ApiExposeClient.GetAsync(baseUrl, $"/api/v1/gamelists/{system}/games");
        if (!ok)
        {
            return null;
        }

        try
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("games", out var games) || games.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            foreach (var game in games.EnumerateArray())
            {
                var rom = game.TryGetProperty("rom", out var r) ? r.GetString() : null;
                var name = game.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(rom) && !string.IsNullOrWhiteSpace(name) && (roms is null || roms.Contains(rom)))
                {
                    index.TryAdd(rom!, name!);
                }
            }

            return index;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyDictionary<string, string>? LoadNamesFromGamelist(string system, HashSet<string>? roms)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_pluginRoot, "..", "..", "roms", system, "gamelist.xml"));
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var doc = System.Xml.Linq.XDocument.Load(path);
            foreach (var game in doc.Root?.Elements("game") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
            {
                var rom = System.IO.Path.GetFileNameWithoutExtension((string?)game.Element("path") ?? "");
                var name = (string?)game.Element("name");
                if (!string.IsNullOrEmpty(rom) && !string.IsNullOrWhiteSpace(name) && (roms is null || roms.Contains(rom)))
                {
                    index.TryAdd(rom, name!);
                }
            }

            return index;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyDictionary<string, string> LoadNamesFromPack(HashSet<string> roms)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            _pluginRoot, "..", "APIExpose", "resources", "gamelist", "systems", "arcade_lt.json"));
        if (!System.IO.File.Exists(path))
        {
            return index;
        }

        foreach (var line in System.IO.File.ReadLines(path))
        {
            if (line.Length < 2)
            {
                continue;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var set = doc.RootElement.TryGetProperty("set", out var s) ? s.GetString() : null;
                if (set is null || !roms.Contains(set) || index.ContainsKey(set))
                {
                    continue;
                }

                var name = doc.RootElement.TryGetProperty("n", out var n) ? n.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    index[set] = name!;
                }
            }
            catch
            {
                // one bad line must not lose the whole index
            }
        }

        return index;
    }

    private string? SelectedSystem => _systems.SelectedItem as string;

    private string LayoutIdForHardware => $"{_hardware.ButtonCount}-Button";

    private void RefreshGameList()
    {
        _gameList.Items.Clear();
        if (SelectedSystem is not { } system)
        {
            return;
        }

        _ = EnsureNamesAsync(system);
        var isArcade = ArcadeFamily.Contains(system);
        var names = _namesBySystem.TryGetValue(system, out var loaded)
            ? loaded
            : isArcade ? _packNames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Arcade: games with a per-game dynpanel, limited to installed roms.
        // Consoles: the installed gamelist itself (baseline = system template).
        var candidates = isArcade
            ? _dynpanelRoms.Where(rom => InstalledRoms(system) is not { } installed || installed.Contains(rom))
            : names.Keys;

        var filter = _gameSearch.Text?.Trim() ?? "";
        foreach (var rom in candidates
                     .Where(rom => rom.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                   || (names.TryGetValue(rom, out var n) && n.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(rom => rom, StringComparer.OrdinalIgnoreCase)
                     .Take(50))
        {
            // game name first and prominent, rom as a discreet second line
            var content = new StackPanel();
            var hasName = names.TryGetValue(rom, out var name);
            content.Children.Add(new TextBlock
            {
                Text = hasName ? name : rom,
                FontSize = 12.5,
                Foreground = Text(0xE8, 0xE8, 0xF0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (hasName)
            {
                content.Children.Add(new TextBlock
                {
                    Text = rom,
                    FontSize = 10.5,
                    Foreground = Text(0x8A, 0x8A, 0x9A)
                });
            }

            _gameList.Items.Add(new ListBoxItem { Tag = rom, Content = content });
        }
    }

    /// <summary>
    /// A console game has no per-game dynpanel: its editing baseline is the SYSTEM
    /// template (like SystemsView), and the game override is saved under the rom
    /// SLUG — the key the runtime receives from APIExpose (NormalizeSlug) and uses
    /// to look up overrides\games\système\rom.json.
    /// </summary>
    private GamePanelCatalog.GamePanel? SynthesizeConsoleGame(string system, string rom)
    {
        var layouts = _catalog.LoadLayouts(system);
        if (layouts.Count == 0)
        {
            return null;
        }

        var index = layouts.ToList().FindIndex(l => !l.Key.Contains(':') && l.PanelButtons == _hardware.ButtonCount);
        var layout = layouts[index >= 0 ? index : layouts.Count - 1];
        var slug = System.Text.RegularExpressions.Regex
            .Replace(rom.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var display = _namesBySystem.TryGetValue(system, out var names) && names.TryGetValue(rom, out var name) ? name : rom;
        return new GamePanelCatalog.GamePanel(system, slug, display, layout.BySlot);
    }

    /// <summary>Rom files actually installed for a system (roms\système), so the
    /// list only offers games the user owns. Missing/empty folder = no filter.</summary>
    private HashSet<string>? InstalledRoms(string system)
    {
        if (!_installedRomsBySystem.TryGetValue(system, out var roms))
        {
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(_pluginRoot, "..", "..", "roms", system));
            roms = System.IO.Directory.Exists(dir)
                ? System.IO.Directory.EnumerateFiles(dir)
                    .Select(System.IO.Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _installedRomsBySystem[system] = roms;
        }

        return roms.Count == 0 ? null : roms;
    }

    private void OnGameSelected()
    {
        if (_gameList.SelectedItem is not ListBoxItem selected || selected.Tag is not string rom)
        {
            return;
        }

        var game = _games.Load(rom, LayoutIdForHardware);
        if (game == null && SelectedSystem is { } consoleSystem && !ArcadeFamily.Contains(consoleSystem))
        {
            game = SynthesizeConsoleGame(consoleSystem, rom);
        }

        if (game == null)
        {
            _status.Text = L.T($"Impossible de lire le dynpanel de {rom}.", $"Could not read {rom}'s dynpanel.");
            return;
        }

        _currentGame = game;
        _panel.Build(_layoutDefinition, _hardware.ButtonCount, hasStart: true, hasSelect: true);
        ReloadOverride();

        // Arcade games deploy their MAME cfg; anything else falls back to the
        // system-level RetroArch remap.
        if (game.OverrideSystem.Equals("arcade", StringComparison.OrdinalIgnoreCase))
        {
            _controlsCard.ShowMameGame(game.Rom);
        }
        else
        {
            _controlsCard.ShowSystem(game.System);
        }
    }

    // ----- resolution: pack -> system patch -> game patch -----

    private string PackColor(int slot)
    {
        return _currentGame is { } game && game.BySlot.TryGetValue(slot, out var entry) ? entry.Color : "BLACK";
    }

    /// <summary>What the runtime would show WITHOUT the game patch being edited.</summary>
    private string BaselineColor(int slot)
    {
        return _systemPatch.TryGetValue(slot, out var fromSystem) ? fromSystem : PackColor(slot);
    }

    private void ReloadOverride()
    {
        if (_currentGame is not { } game)
        {
            return;
        }

        // arcade/mame are interchangeable for the runtime; the setup reads whichever
        // file exists and always writes the canonical "arcade" one
        _systemPatch = _store.LoadSlotColors(game.OverrideSystem);
        if (_systemPatch.Count == 0 && game.OverrideSystem != game.System)
        {
            _systemPatch = _store.LoadSlotColors(game.System);
        }

        _edited.Clear();
        var saved = FirstNonEmpty(
            _store.LoadGameSlotColors(game.OverrideSystem, game.Rom),
            game.OverrideSystem != game.System ? _store.LoadGameSlotColors(game.System, game.Rom) : null);
        foreach (var (slot, color) in saved)
        {
            _edited[slot] = color;
        }

        Repaint();
        _status.Text = L.T($"Jeu {game.GameName} ({game.Rom}, système {game.System}).",
            $"Game {game.GameName} ({game.Rom}, system {game.System}).");
    }

    private void Repaint()
    {
        foreach (var slot in _panel.Slots)
        {
            var effective = _edited.TryGetValue(slot, out var over) ? over : BaselineColor(slot);
            _panel.SetSlot(slot, PanelColors.Resolve(effective));
        }

        _panel.SetTarget("START", PanelColors.Resolve("GRAY"));
        _panel.SetTarget("SELECT", PanelColors.Resolve("GRAY"));

        // origin summary: which patch level drives each customized slot
        var parts = new List<string>();
        foreach (var slot in _panel.Slots.OrderBy(s => s))
        {
            if (_edited.TryGetValue(slot, out var over))
            {
                parts.Add($"B{slot} → {over} " + L.T("(jeu)", "(game)"));
            }
            else if (_systemPatch.ContainsKey(slot))
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
        if (_currentGame is null)
        {
            _status.Text = L.T("Choisissez d'abord un jeu dans la liste.", "Pick a game in the list first.");
            return;
        }

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
        if (_currentGame is not { } game)
        {
            return;
        }

        var path = _store.SaveGame(game.OverrideSystem, game.Rom, _edited);
        _status.Text = _edited.Count == 0
            ? L.T("Plus aucune personnalisation : le patch a été retiré.", "No customization left: the patch was removed.")
            : L.T($"Override enregistré ({System.IO.Path.GetFileName(path)}). LedManager l'applique dès la prochaine sélection de jeu.",
                $"Override saved ({System.IO.Path.GetFileName(path)}). LedManager applies it from the next game selection.");
    }

    private void OnResetToPack(object sender, RoutedEventArgs e)
    {
        if (_currentGame is not { } game)
        {
            return;
        }

        var confirm = MessageBox.Show(
            L.T($"Supprimer l'override du jeu {game.Rom} ?", $"Delete the {game.Rom} game override?"),
            "LedManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _store.DeleteGame(game.OverrideSystem, game.Rom);
        if (game.OverrideSystem != game.System)
        {
            _store.DeleteGame(game.System, game.Rom);
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
            var effective = _edited.TryGetValue(slot, out var over) ? over : BaselineColor(slot);
            _sender.Send($"SLOT {slot} {effective}");
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
