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
    private readonly ComboBox _layoutPicker;
    private bool _suppressLayout;
    private readonly TextBox _gameSearch;
    private readonly TextBlock _searchPlaceholder;
    private readonly ListBox _gameList;
    private readonly Button _liveTest;
    private readonly TextBlock _status;
    private readonly TextBlock _summary;
    private readonly ContextMenu _palette;
    private readonly ControlsDeployCard _controlsCard = new();
    private readonly StackPanel _sheetContent = new();
    private readonly Border _gameSheet;
    private readonly PanelPatchBay _patchBay = new();
    private readonly Dictionary<string, string> _portDetails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _targetColorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _inspector = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed
    };

    /// <summary>Persistent host of the patch bay: rebuilding the sheet re-adds the
    /// same element, because re-parenting a WPF child into a fresh ScrollViewer
    /// throws (and would silently hide the whole sheet).</summary>
    private readonly ScrollViewer _patchBayHost;

    /// <summary>Systems whose games use the arcade per-game dynpanels.</summary>
    private static readonly HashSet<string> ArcadeFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "mame", "arcade", "fbneo", "neogeo", "neogeocd", "hbmame"
    };

    /// <summary>Emulators that really emit lamp outputs: the light channel only
    /// shows for them (FBNeo has none — its games keep colors and actions only).</summary>
    private static readonly HashSet<string> OutputCapableSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "mame", "arcade", "hbmame"
    };

    private bool SelectedSystemHasOutputs => SelectedSystem is not { } s || OutputCapableSystems.Contains(s);

    private SystemPanelCatalog _catalog = null!;
    private IReadOnlyList<string> _dynpanelRoms = Array.Empty<string>();
    private readonly Dictionary<string, HashSet<string>> _installedRomsBySystem = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _namesBySystem = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _namesLoading = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string>? _packNames;
    private GamePanelCatalog.GamePanel? _currentGame;
    private IReadOnlyDictionary<string, IReadOnlyList<int>> _deployedWiring = new Dictionary<string, IReadOnlyList<int>>();
    private IReadOnlyDictionary<int, string> _systemPatch = new Dictionary<int, string>();
    private readonly Dictionary<int, string> _edited = new();
    private int _paletteSlot;
    private PicoSenderHost? _sender;
    private bool _suppressSearch;

    public GamesView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _hardware = hardware;
        _layoutDefinition = layout;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? System.IO.Directory.GetCurrentDirectory();
        _games = new GamePanelCatalog(_pluginRoot);
        _store = new SystemOverrideStore(_pluginRoot);

        _systems = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _systems.SelectionChanged += (_, _) => OnSystemChanged();

        _layoutPicker = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _layoutPicker.SelectionChanged += (_, _) => OnLayoutChanged();

        _gameSearch = new TextBox
        {
            Width = 460,
            MinHeight = 34,
            FontSize = 13,
            Padding = new Thickness(31, 7, 8, 7),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _searchPlaceholder = new TextBlock
        {
            Text = L.T("Rechercher un jeu ou une rom…", "Search a game or a rom…"),
            Foreground = Text(0x8A, 0x8A, 0x9A),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(33, 0, 0, 0),
            IsHitTestVisible = false
        };
        _gameSearch.TextChanged += (_, _) =>
        {
            _searchPlaceholder.Visibility = string.IsNullOrEmpty(_gameSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (_suppressSearch)
            {
                return;
            }

            RefreshGameList();
            UpdateGameListVisibility();
        };
        _gameSearch.GotKeyboardFocus += (_, _) => UpdateGameListVisibility();
        _gameSearch.PreviewKeyDown += OnSearchKeyDown;

        // autocomplete behaviour: the result list only opens while searching
        _gameList = new ListBox { Width = 460, MaxHeight = 220, FontSize = 12.5, Visibility = Visibility.Collapsed };
        _gameList.SelectionChanged += (_, _) => OnGameSelected();

        _status = new TextBlock { Margin = new Thickness(0, 10, 0, 0), FontSize = 12, Foreground = Text(0x8A, 0x8A, 0x9A), TextWrapping = TextWrapping.Wrap };
        _summary = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 12, Foreground = Text(0xB8, 0xB8, 0xC6), TextWrapping = TextWrapping.Wrap };

        _panel.SlotClicked += OnSlotClicked;
        _panel.TargetClicked += _ => _status.Text = L.T(
            "START/SELECT ne sont pas encore personnalisables.",
            "START/SELECT are not customizable yet.");

        _palette = BuildPalette();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = L.T("Système", "System"), Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_systems);
        header.Children.Add(new TextBlock { Text = L.T("Gabarit", "Template"), Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_layoutPicker);
        header.Children.Add(new TextBlock
        {
            Text = L.T($"Pico : {hardware.PicoLabel}", $"Pico: {hardware.PicoLabel}"),
            Foreground = Ui.Brush(Color.FromRgb(0x8A, 0x2B, 0xE2)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        // search field: single bordered TextBox with the magnifier and placeholder
        // overlaid, so the focus ring is the field's own border (no double outline)
        var searchIcon = new TextBlock
        {
            Text = "\uE721",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Text(0x8A, 0x8A, 0x9A),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false
        };
        var searchOverlay = new Grid { Width = 460, Margin = new Thickness(0, 0, 0, 5) };
        searchOverlay.Children.Add(_gameSearch);
        searchOverlay.Children.Add(searchIcon);
        searchOverlay.Children.Add(_searchPlaceholder);

        var gameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var gamePicker = new StackPanel();
        gamePicker.Children.Add(searchOverlay);
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
        buttons.Children.Add(Action(L.T("Enregistrer ma configuration", "Save my configuration"), OnSave, primary: true));
        buttons.Children.Add(Action(L.T("Annuler les modifications", "Discard changes"), (_, _) => ReloadOverride()));
        buttons.Children.Add(Action(L.T("Revenir aux couleurs du pack", "Back to pack colors"), OnResetToPack));
        _liveTest = Action(L.T("Tester sur le panneau réel", "Test on the real panel"), OnLiveTest);
        buttons.Children.Add(_liveTest);

        var intro = new TextBlock
        {
            Text = L.T(
                "Choisissez un système puis un jeu : le panel s'affiche avec ses couleurs finales. "
                + "Cliquez un bouton pour choisir sa couleur : votre configuration du jeu est enregistrée et "
                + "passe devant celle du système, qui passe devant les réglages d'origine. "
                + "Le gabarit de base d'un système se personnalise dans « Mes systèmes ».",
                "Pick a system then a game: the panel shows its final colors. "
                + "Click a button to pick its color: your game configuration is saved and "
                + "wins over the system one, which wins over the factory settings. "
                + "A system's base template is customized in \"My systems\"."),
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

        _patchBayHost = new ScrollViewer
        {
            Content = _patchBay,
            MaxHeight = 640,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _inspector.Foreground = Text(0xB8, 0xB8, 0xC6);
        _patchBay.PortInspected += port =>
        {
            _inspector.Text = _portDetails.TryGetValue(port.Id, out var details)
                ? details
                : $"{port.Label} · {port.Id}";
            _inspector.Visibility = Visibility.Visible;
        };

        // clicking in the bay echoes the focus on the virtual panel above, and on
        // the real panel when a live-test session is running
        _patchBay.SelectionEchoed += (slots, targets) =>
        {
            foreach (var slot in slots)
            {
                _panel.Flash(slot.ToString(), Colors.White, 600);
            }

            foreach (var target in targets)
            {
                _panel.Flash(target, Colors.White, 600);
            }

            if (_sender is { IsAlive: true } && (slots.Count > 0 || targets.Count > 0))
            {
                foreach (var slot in slots)
                {
                    _sender.Send($"SLOT {slot} WHITE");
                }

                var restore = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
                restore.Tick += (_, _) => { restore.Stop(); SendLivePreview(); };
                restore.Start();
            }
        };

        _gameSheet = new Border
        {
            Background = Ui.Brush(Color.FromRgb(0x1D, 0x1D, 0x2A)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 14, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = _sheetContent
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(header);
        stack.Children.Add(gameRow);
        stack.Children.Add(intro);
        stack.Children.Add(panelBorder);
        stack.Children.Add(_summary);
        stack.Children.Add(buttons);
        stack.Children.Add(_status);
        stack.Children.Add(_gameSheet);
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

    /// <summary>Layout the user is editing — defaults to the real panel's one, but
    /// the 2/4/6/8-button templates (and system specials) stay reachable.</summary>
    private string SelectedLayoutId
        => _layoutPicker?.SelectedItem is ComboBoxItem { Tag: string key } ? key : LayoutIdForHardware;

    private int SelectedLayoutButtons
    {
        get
        {
            var match = System.Text.RegularExpressions.Regex.Match(SelectedLayoutId, @"^(\d+)-Button");
            return match.Success ? int.Parse(match.Groups[1].Value) : _hardware.ButtonCount;
        }
    }

    private void PopulateLayoutChoices()
    {
        _suppressLayout = true;
        _layoutPicker.Items.Clear();
        foreach (var count in new[] { 2, 4, 6, 8 })
        {
            _layoutPicker.Items.Add(new ComboBoxItem
            {
                Tag = $"{count}-Button",
                Content = count == _hardware.ButtonCount
                    ? L.T($"{count} boutons — mon panel", $"{count} buttons — my panel")
                    : L.T($"{count} boutons", $"{count} buttons")
            });
        }

        // special system templates (e.g. snes CONTROL PANEL accessories)
        if (SelectedSystem is { } system && !ArcadeFamily.Contains(system) && _catalog is not null)
        {
            foreach (var special in _catalog.LoadLayouts(system).Where(l => l.Key.Contains(':')))
            {
                _layoutPicker.Items.Add(new ComboBoxItem { Tag = special.Key, Content = special.DisplayName });
            }
        }

        _layoutPicker.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { 2, 4, 6, 8 }, _hardware.ButtonCount));
        _suppressLayout = false;
    }

    private void OnLayoutChanged()
    {
        if (_suppressLayout)
        {
            return;
        }

        _panel.Build(_layoutDefinition, SelectedLayoutButtons, hasStart: true, hasSelect: true);
        if (_currentGame is { } game)
        {
            LoadGame(game.Rom);
        }
        else
        {
            Repaint();
        }
    }

    /// <summary>Changing system resets the whole game context: without this, the
    /// previous game's panel, sheet and controls card lingered until a new game
    /// was picked in the new system.</summary>
    private void OnSystemChanged()
    {
        _currentGame = null;
        _edited.Clear();
        _systemPatch = new Dictionary<int, string>();
        _gameSheet.Visibility = Visibility.Collapsed;
        _suppressSearch = true;
        _gameSearch.Text = "";
        _suppressSearch = false;
        _searchPlaceholder.Visibility = Visibility.Visible;
        _gameList.Visibility = Visibility.Collapsed;
        PopulateLayoutChoices();
        _panel.Build(_layoutDefinition, SelectedLayoutButtons, hasStart: true, hasSelect: true);
        Repaint();

        if (SelectedSystem is { } system)
        {
            if (ArcadeFamily.Contains(system))
            {
                _status.Text = L.T("Choisissez un jeu dans la liste.", "Pick a game in the list.");
                _controlsCard.ShowNone(L.T("Choisissez un jeu pour voir ses contrôles.", "Pick a game to see its controls."));
            }
            else
            {
                _status.Text = L.T($"Jeux installés de « {system} » : personnalisez un jeu, ou le gabarit dans Mes systèmes.",
                    $"Installed games of \"{system}\": customize a game, or the template in My systems.");
                _controlsCard.ShowSystem(system);
            }
        }

        RefreshGameList();
    }

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
            // game name first and prominent, rom as a discreet second line; both
            // inherit the item Foreground so the selected state can turn them white
            var content = new StackPanel();
            var hasName = names.TryGetValue(rom, out var name);
            content.Children.Add(new TextBlock
            {
                Text = hasName ? name : rom,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (hasName)
            {
                content.Children.Add(new TextBlock
                {
                    Text = rom,
                    FontSize = 10.5,
                    Opacity = 0.62
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

        // honor the layout picker: exact key first (specials), then button count
        var list = layouts.ToList();
        var index = list.FindIndex(l => l.Key.Equals(SelectedLayoutId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = list.FindIndex(l => !l.Key.Contains(':') && l.PanelButtons == SelectedLayoutButtons);
        }

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

    /// <summary>Autocomplete: the result list only shows while a search is typed.</summary>
    private void UpdateGameListVisibility()
    {
        _gameList.Visibility = _gameSearch.Text.Trim().Length > 0 && _gameList.Items.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_gameList.Items.Count == 0)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            // Enter picks the first result
            _gameList.SelectedIndex = -1;
            _gameList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Down && _gameList.Items[0] is ListBoxItem first)
        {
            _gameList.Visibility = Visibility.Visible;
            first.Focus();
            e.Handled = true;
        }
    }

    private void OnGameSelected()
    {
        if (_gameList.SelectedItem is not ListBoxItem selected || selected.Tag is not string rom)
        {
            return;
        }

        // the chosen game takes the search box (like any combobox) and closes the list
        if (selected.Content is StackPanel lines && lines.Children.Count > 0 && lines.Children[0] is TextBlock nameBlock)
        {
            _suppressSearch = true;
            _gameSearch.Text = nameBlock.Text;
            _suppressSearch = false;
        }

        _gameList.Visibility = Visibility.Collapsed;
        LoadGame(rom);
    }

    /// <summary>Loads a game under the SELECTED layout (defaults to the real panel's).</summary>
    private void LoadGame(string rom)
    {
        _targetColorCache.Clear();
        var game = _games.Load(rom, SelectedLayoutId);
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
        _panel.Build(_layoutDefinition, SelectedLayoutButtons, hasStart: true, hasSelect: true);
        ReloadOverride();

        // Arcade games deploy their MAME cfg — except under FBNeo, whose target is
        // the per-game RetroArch remap; anything else falls back to the
        // system-level RetroArch remap.
        if (game.OverrideSystem.Equals("arcade", StringComparison.OrdinalIgnoreCase))
        {
            if (SelectedSystem is { } selected && selected.Equals("fbneo", StringComparison.OrdinalIgnoreCase))
            {
                _controlsCard.ShowFbneoGame(game.Rom);
                _ = LoadGameSheetAsync("mame", rom); // shared dynpanel scope
            }
            else
            {
                _controlsCard.ShowMameGame(game.Rom);
                _ = LoadGameSheetAsync(SelectedSystem ?? game.System, rom);
            }
        }
        else
        {
            _controlsCard.ShowSystem(game.System);
            _gameSheet.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Chantier 2 phase A — read-only game sheet: for each panel button, its TWO
    /// channels (input: game action / axis; light: game output driving the LED),
    /// plus the game lights not displayed anywhere. Data comes from the APIExpose
    /// definition projection; the sheet simply hides when the API is unreachable.
    /// </summary>
    private async Task LoadGameSheetAsync(string system, string rom)
    {
        _gameSheet.Visibility = Visibility.Collapsed;
        _sheetContent.Children.Clear();

        var baseUrl = ApiExposeClient.ResolveBaseUrl(_pluginRoot);
        var (ok, body) = await ApiExposeClient.GetAsync(baseUrl, $"/api/v1/panels/game/{system}/{rom}/definition?activeLayout={Uri.EscapeDataString(SelectedLayoutId)}");
        if (!ok)
        {
            return;
        }

        // the deployed-cfg readback only makes sense for MAME-side systems
        _deployedWiring = SelectedSystemHasOutputs
            ? await LoadCurrentWiringAsync(baseUrl, rom)
            : new Dictionary<string, IReadOnlyList<int>>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            _sheetContent.Children.Add(new TextBlock
            {
                Text = L.T($"Fiche du jeu — actions et lumières ({root.GetProperty("activeLayoutId").GetString()})",
                    $"Game sheet — actions and lights ({root.GetProperty("activeLayoutId").GetString()})"),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Text(0xE8, 0xE8, 0xF0),
                Margin = new Thickness(0, 0, 0, 8)
            });

            if (root.TryGetProperty("slots", out var slots) && slots.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var slot in slots.EnumerateArray())
                {
                    var line = DescribeSlot(slot);
                    if (line is not null)
                    {
                        _sheetContent.Children.Add(line);
                    }
                }
            }

            if (SelectedSystemHasOutputs
                && root.TryGetProperty("externalOutputs", out var external) && external.ValueKind == System.Text.Json.JsonValueKind.Array
                && external.GetArrayLength() > 0)
            {
                _sheetContent.Children.Add(new TextBlock
                {
                    Text = L.T("Lumières du jeu non affichées sur le panel :", "Game lights not shown on the panel:"),
                    FontSize = 11,
                    Foreground = Text(0x8A, 0x8A, 0x9A),
                    Margin = new Thickness(0, 8, 0, 2)
                });
                var names = external.EnumerateArray()
                    .Select(o => o.TryGetProperty("label", out var l) ? l.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .Take(12)
                    .ToList();
                _sheetContent.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", names) + (external.GetArrayLength() > 12 ? " …" : ""),
                    FontSize = 11,
                    Foreground = Text(0xB8, 0xB8, 0xC6),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            BuildLightsEditor(root);

            if (_sheetContent.Children.Count > 1)
            {
                _gameSheet.Visibility = Visibility.Visible;
            }
        }
        catch (System.Net.Http.HttpRequestException)
        {
            // API unreachable: the sheet just stays hidden
        }
        catch (Exception ex)
        {
            _sheetContent.Children.Add(new TextBlock
            {
                Text = L.T($"Fiche indisponible : {ex.Message}", $"Sheet unavailable: {ex.Message}"),
                Foreground = Ui.Brush(Color.FromRgb(0xE8, 0x8A, 0x5A)),
                TextWrapping = TextWrapping.Wrap
            });
            _gameSheet.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Chantier 2 phase B — light-channel editor: every game output can be
    /// reallocated to a panel button and recolored. Saved into the game override
    /// ("outputs" section) that the runtime already applies (PanelStateOverrides).
    /// </summary>
    private void BuildLightsEditor(System.Text.Json.JsonElement root)
    {
        var outputs = new List<(string Name, string Label, IReadOnlyList<int> PackSlots, string? PackColor, string? Group, string? InputRef)>();
        _portDetails.Clear();

        void Collect(System.Text.Json.JsonElement output, int? packSlot)
        {
            var name = output.TryGetProperty("outputName", out var on) && !string.IsNullOrWhiteSpace(on.GetString())
                ? on.GetString()
                : output.TryGetProperty("name", out var nn) ? nn.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || outputs.Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // multi-home lamps carry a "slots" array (READY_LAMP lights B4+B3)
            IReadOnlyList<int> packSlots;
            if (output.TryGetProperty("slots", out var multi) && multi.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                packSlots = multi.EnumerateArray()
                    .Where(v => v.ValueKind == System.Text.Json.JsonValueKind.Number)
                    .Select(v => v.GetInt32())
                    .Where(s => s >= 1)
                    .Distinct()
                    .ToArray();
            }
            else
            {
                packSlots = packSlot is { } single ? new[] { single } : Array.Empty<int>();
            }

            var group = output.TryGetProperty("group", out var g) ? g.GetString() : null;
            var inputRef = output.TryGetProperty("inputRef", out var ir) ? ir.GetString() : null;
            var valueType = output.TryGetProperty("valueType", out var vt) ? vt.GetString() : null;
            _portDetails[name!] = L.T(
                $"LUMIÈRE {name} · groupe {group ?? "—"} · type {valueType ?? "—"} · déclencheur {(string.IsNullOrWhiteSpace(inputRef) ? "—" : inputRef)}",
                $"LIGHT {name} · group {group ?? "—"} · type {valueType ?? "—"} · trigger {(string.IsNullOrWhiteSpace(inputRef) ? "—" : inputRef)}");

            outputs.Add((name!,
                output.TryGetProperty("label", out var l) ? l.GetString() ?? name! : name!,
                packSlots,
                output.TryGetProperty("color", out var c) ? c.GetString() : null,
                group,
                inputRef));
        }

        // the light channel only exists on emulators with real lamp outputs
        // (FBNeo has none: colors and actions still show, lamps don't)
        if (SelectedSystemHasOutputs)
        {
            if (root.TryGetProperty("slots", out var slots) && slots.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var slot in slots.EnumerateArray())
                {
                    var number = ReadSlotNumber(slot);
                    if (!slot.TryGetProperty("outputs", out var slotOutputs) || slotOutputs.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var output in slotOutputs.EnumerateArray())
                    {
                        Collect(output, number);
                    }
                }
            }

            if (root.TryGetProperty("externalOutputs", out var external) && external.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var output in external.EnumerateArray())
                {
                    Collect(output, null);
                }
            }
        }

        // --- game actions (input channel): P1 buttons, multi-allocatable ---
        var actions = new List<(string Id, string Label, List<int> Slots)>();
        void CollectAction(System.Text.Json.JsonElement input, int? slot)
        {
            var id = input.TryGetProperty("mameInput", out var mi) ? mi.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                // dynpanels may carry pre-0.286 Neo-Geo types (P1_A..P1_D) while
                // the deployed cfg uses the normalized P1_BUTTONn — align on the
                // normalized id so cables, wiring readback and patches all match
                var neoGeo = System.Text.RegularExpressions.Regex.Match(id!, @"^P1_([A-F])$");
                if (neoGeo.Success)
                {
                    id = $"P1_BUTTON{neoGeo.Groups[1].Value[0] - 'A' + 1}";
                }
            }

            if (string.IsNullOrWhiteSpace(id) || !System.Text.RegularExpressions.Regex.IsMatch(id!, @"^P1_BUTTON\d+$"))
            {
                return;
            }

            var mameTag = input.TryGetProperty("mameTag", out var mt) ? mt.GetString() : null;
            var mameMask = input.TryGetProperty("mameMask", out var mm) ? mm.GetString() : null;
            _portDetails[id!] = L.T(
                $"ACTION {id} · port {mameTag ?? "—"} · masque {mameMask ?? "—"}",
                $"ACTION {id} · port {mameTag ?? "—"} · mask {mameMask ?? "—"}");

            var existingAction = actions.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existingAction.Id is null)
            {
                var label = input.TryGetProperty("function", out var f) && !string.IsNullOrWhiteSpace(f.GetString())
                    ? f.GetString()!
                    : input.TryGetProperty("label", out var l) ? l.GetString() ?? id! : id!;
                existingAction = (id!, label, new List<int>());
                actions.Add(existingAction);
            }

            if (slot is { } s && !existingAction.Slots.Contains(s))
            {
                existingAction.Slots.Add(s);
            }
        }

        if (root.TryGetProperty("slots", out var slotsForActions) && slotsForActions.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var slot in slotsForActions.EnumerateArray())
            {
                var number = ReadSlotNumber(slot);
                if (slot.TryGetProperty("inputs", out var slotInputs) && slotInputs.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var input in slotInputs.EnumerateArray())
                    {
                        CollectAction(input, number);
                    }
                }
            }
        }

        if (root.TryGetProperty("controlMap", out var controlMap)
            && controlMap.TryGetProperty("inputs", out var mapInputs) && mapInputs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var input in mapInputs.EnumerateArray())
            {
                CollectAction(input, ReadSlotNumber(input));
            }
        }

        // default cables = the truth, in this order: wiring of the deployed cfg
        // (e.g. seawolf Fire on B1/B2/B6/B8), else the standard template slot n
        // for P1_BUTTONn — so no action ever floats unwired.
        foreach (var action in actions)
        {
            if (_deployedWiring.TryGetValue(action.Id, out var deployed))
            {
                action.Slots.Clear();
                action.Slots.AddRange(deployed.Where(s => s >= 1 && s <= SelectedLayoutButtons));
                continue;
            }

            if (action.Slots.Count > 0)
            {
                continue;
            }

            var match = System.Text.RegularExpressions.Regex.Match(action.Id, @"^P1_BUTTON(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number)
                && number >= 1 && number <= SelectedLayoutButtons)
            {
                action.Slots.Add(number);
            }
        }

        // --- joystick / peripherals channel (informative): analog axes per button,
        // directions and analog channels routed to their LARGE device node with one
        // anchor per way/axis — the curator's granularity ---
        var axisPorts = new List<(string Id, string Label, IReadOnlyList<int> Slots, string? DeviceKey, string? Anchor)>();
        var deviceNodes = new List<PanelPatchBay.DeviceNode>();

        void CollectAxis(string? id, string? label, int? slot, string? deviceKey = null, string? anchor = null)
        {
            if (string.IsNullOrWhiteSpace(id) || axisPorts.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            axisPorts.Add((id!, string.IsNullOrWhiteSpace(label) ? id! : label!,
                slot is { } s ? new[] { s } : Array.Empty<int>(), deviceKey, anchor));
        }

        if (root.TryGetProperty("slots", out var slotsForAxes) && slotsForAxes.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var slot in slotsForAxes.EnumerateArray())
            {
                if (!slot.TryGetProperty("axes", out var slotAxes) || slotAxes.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                var number = ReadSlotNumber(slot);
                foreach (var axis in slotAxes.EnumerateArray())
                {
                    var label = axis.TryGetProperty("label", out var al) ? al.GetString() : null;
                    var id = axis.TryGetProperty("id", out var ai) && !string.IsNullOrWhiteSpace(ai.GetString()) ? ai.GetString() : label;
                    CollectAxis(id, label, number);
                }
            }
        }

        // slotless analog channels: each becomes a device node (paddle, spinner,
        // trackball, wheel… recognized from the label)
        if (root.TryGetProperty("externalAxes", out var externalAxes) && externalAxes.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var axis in externalAxes.EnumerateArray())
            {
                var label = axis.TryGetProperty("label", out var al) ? al.GetString() : null;
                var id = axis.TryGetProperty("id", out var ai) && !string.IsNullOrWhiteSpace(ai.GetString()) ? ai.GetString() : label;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var deviceKey = "analog:" + id;
                var anchors = AnchorsForAnalog(label ?? id!);
                deviceNodes.Add(new PanelPatchBay.DeviceNode(deviceKey, label ?? id!, "analog", anchors));
                CollectAxis(id, label, null, deviceKey, anchors[0]);
            }
        }

        // player-1 directions: grouped per physical device (type/label from the
        // dynpanel device list), the node exposes the joystick's real ways
        var directionsByDevice = new Dictionary<string, (string Label, string Way, List<(string Id, string Label, string Direction)> Entries)>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("controlMap", out var mapForDirections)
            && mapForDirections.TryGetProperty("inputs", out var directionInputs)
            && directionInputs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var input in directionInputs.EnumerateArray())
            {
                var mame = input.TryGetProperty("mameInput", out var mi) ? mi.GetString() : null;
                var match = mame is null
                    ? null
                    : System.Text.RegularExpressions.Regex.Match(mame, @"^P1_(?:JOYSTICK(?:LEFT|RIGHT)?_)?(LEFT|RIGHT|UP|DOWN)$");
                if (match is not { Success: true })
                {
                    continue;
                }

                var direction = match.Groups[1].Value.ToLowerInvariant();
                var deviceType = input.TryGetProperty("deviceType", out var dt) ? dt.GetString() ?? "" : "";
                var deviceLabel = input.TryGetProperty("deviceLabel", out var dl) ? dl.GetString() ?? "" : "";
                var way = input.TryGetProperty("joystickWay", out var jw) ? jw.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(way))
                {
                    way = deviceType; // "joy8way", "joy2wayhorizontal"… carry the ways too
                }

                var key = "joy:" + (string.IsNullOrWhiteSpace(deviceLabel) ? deviceType : deviceLabel);
                var label = input.TryGetProperty("function", out var f) && !string.IsNullOrWhiteSpace(f.GetString())
                    ? f.GetString()!
                    : L.T(direction switch { "up" => "Haut", "down" => "Bas", "left" => "Gauche", _ => "Droite" },
                        char.ToUpperInvariant(direction[0]) + direction[1..]);

                if (!directionsByDevice.TryGetValue(key, out var device))
                {
                    var displayLabel = string.IsNullOrWhiteSpace(deviceLabel) || deviceLabel.StartsWith("joy", StringComparison.OrdinalIgnoreCase)
                        ? L.T("Joystick", "Joystick")
                        : deviceLabel;
                    device = (displayLabel, way, new List<(string, string, string)>());
                    directionsByDevice[key] = device;
                }

                if (!string.IsNullOrWhiteSpace(way))
                {
                    directionsByDevice[key] = (device.Label, way, device.Entries);
                }

                device.Entries.Add((mame!, label, direction));
            }
        }

        foreach (var (key, device) in directionsByDevice)
        {
            var anchors = JoystickAnchors(device.Way, device.Entries.Select(e => e.Direction));
            var displayWay = anchors.Count is 8 or 4 or 2 ? L.T($" {anchors.Count} voies", $" {anchors.Count}-way") : "";
            deviceNodes.Add(new PanelPatchBay.DeviceNode(key, device.Label + displayWay, "joystick", anchors));
            foreach (var (id, label, direction) in device.Entries)
            {
                CollectAxis(id, label, null, key, direction);
            }
        }

        if ((outputs.Count == 0 && actions.Count == 0 && axisPorts.Count == 0) || _currentGame is not { } game)
        {
            return;
        }

        var lightPatches = _store.LoadGameOutputPatches(game.OverrideSystem, game.Rom);
        var actionPatches = _store.LoadGameInputPatches(game.OverrideSystem, game.Rom);

        // START/SELECT colors + the lamps the system inputs drive (llander:
        // start/select → lamp0, coin → lamp1) from the game's system inputs
        var targetColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lampTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("controlMap", out var mapForTargets)
            && mapForTargets.TryGetProperty("inputs", out var targetInputs)
            && targetInputs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var input in targetInputs.EnumerateArray())
            {
                var id = input.TryGetProperty("id", out var ti) ? ti.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var isStart = id!.Equals("start", StringComparison.OrdinalIgnoreCase);
                var isSelect = id.Equals("coin", StringComparison.OrdinalIgnoreCase) || id.Equals("select", StringComparison.OrdinalIgnoreCase);
                if (!isStart && !isSelect)
                {
                    continue;
                }

                var targetName = isStart ? "START" : "SELECT";
                var color = input.TryGetProperty("color", out var tc) ? tc.GetString() : null;
                if (!string.IsNullOrWhiteSpace(color) && !targetColors.ContainsKey(targetName))
                {
                    targetColors[targetName] = color!;
                }

                var outputRef = input.TryGetProperty("outputRef", out var or) ? or.GetString() : null;
                if (!string.IsNullOrWhiteSpace(outputRef))
                {
                    if (!lampTargets.TryGetValue(outputRef!, out var set))
                    {
                        lampTargets[outputRef!] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    set.Add(targetName);
                }
            }
        }

        _sheetContent.Children.Add(new TextBlock
        {
            Text = L.T("Câblage du panel — actions et lumières :", "Panel wiring — actions and lights:"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = Text(0xE8, 0xE8, 0xF0),
            Margin = new Thickness(0, 10, 0, 2)
        });
        _sheetContent.Children.Add(new TextBlock
        {
            Text = L.T("Tire un câble d'une prise vers un bouton (il s'aimante) · SUPPRIMER une liaison : redépose l'action sur le "
                    + "bouton déjà branché, ou lâche le câble dans le vide pour revenir au réglage d'origine · une ACTION (cyan) "
                    + "peut être branchée sur plusieurs boutons — y compris pour retirer un câble PAR DÉFAUT (redépose sur le "
                    + "bouton pointillé) · une LUMIÈRE peut éclairer plusieurs boutons d'origine ; la déplacer la ramène à un "
                    + "seul, et la redéposer sur son propre bouton la DÉTACHE (LED morte) · une DIRECTION de périphérique (bleu) peut aussi être câblée sur des "
                    + "boutons — le stick continue de fonctionner, les boutons s'ajoutent · pointillé = réglage actuel, plein = "
                    + "ta modification · la prise violette d'un groupe câble toute la famille · clic sur une puce, un bouton ou "
                    + "un périphérique = focus · le point coloré ouvre la palette.",
                "Drag a cable from a port to a button (it snaps) · REMOVE a link: drop the action again on the wired button, or "
                    + "drop the cable in the void to return to the original setting · an ACTION (cyan) can be wired to several "
                    + "buttons · a LIGHT may light several buttons by default; moving it re-homes it to one · a device "
                    + "DIRECTION (blue) can also be wired to buttons — the stick keeps working, buttons are added · dashed = "
                    + "current setting, solid = your change · a group's purple port wires the whole family · click a chip, a "
                    + "button or a device to focus · the color dot opens the palette."),
            FontSize = 10.5,
            Foreground = Text(0x8A, 0x8A, 0x9A),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var ports = actions
            .Select(a => new PanelPatchBay.Port(a.Id, PanelPatchBay.KindAction, a.Label, null, null, a.Slots))
            .Concat(axisPorts.Select(a => new PanelPatchBay.Port(a.Id, PanelPatchBay.KindAxis, a.Label, null, null, a.Slots, a.DeviceKey, a.Anchor)))
            .Concat(outputs.Select(o =>
            {
                // slotless lamp driven by a system input → homed on that LED by
                // default; slotted lamp → the link shows as a trigger cable
                var driven = lampTargets.TryGetValue(o.Name, out var t) ? t : null;
                var packTargets = driven is not null && o.PackSlots.Count == 0 ? driven.ToArray() : null;
                var inputRef = o.InputRef;
                if (driven is not null && packTargets is null)
                {
                    inputRef = string.Join(" ", driven.Select(x => x.Equals("START", StringComparison.OrdinalIgnoreCase) ? "SYS START" : "SYS SELECT"))
                               + (string.IsNullOrWhiteSpace(inputRef) ? "" : " " + inputRef);
                }

                return new PanelPatchBay.Port(o.Name, PanelPatchBay.KindLight, o.Label, o.Group, o.PackColor, o.PackSlots,
                    InputRef: inputRef, PackTargets: packTargets);
            }))
            .ToList();
        // the bay paints buttons with the game's resolved colors (mslug has no
        // lamp: its buttons still show Red/Yellow/Green)
        var slotColors = _panel.Slots.ToDictionary(s => s, s => _edited.TryGetValue(s, out var over) ? over : BaselineColor(s));

        // reflect the system LED colors on the big panel too: homed lamp first
        // (its saved color override wins), else the system-input color
        _targetColorCache.Clear();
        foreach (var (name, color) in targetColors)
        {
            _targetColorCache[name] = color;
        }

        foreach (var (lamp, homes) in lampTargets)
        {
            var port = outputs.FirstOrDefault(o => o.Name.Equals(lamp, StringComparison.OrdinalIgnoreCase));
            if (port.Name is null || port.PackSlots.Count > 0)
            {
                continue;
            }

            var lampColor = lightPatches.TryGetValue(lamp, out var patch) && !string.IsNullOrWhiteSpace(patch.Color)
                ? patch.Color!
                : port.PackColor;
            if (!string.IsNullOrWhiteSpace(lampColor))
            {
                foreach (var home in homes)
                {
                    _targetColorCache[home] = lampColor!;
                }
            }
        }

        Repaint();

        _patchBay.Load(ports, lightPatches, actionPatches, SelectedLayoutButtons, deviceNodes, slotColors, targetColors);

        _sheetContent.Children.Add(_patchBayHost);
        _inspector.Visibility = Visibility.Collapsed;
        _sheetContent.Children.Add(_inspector);

        var save = new Button
        {
            Content = L.T("Enregistrer le câblage", "Save the wiring"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.Bold,
            Style = (Style)Application.Current.FindResource("AccentButton")
        };
        save.Click += (_, _) => SaveWiring();
        _sheetContent.Children.Add(save);
    }

    private void SaveWiring()
    {
        if (_currentGame is not { } game)
        {
            return;
        }

        var lights = _patchBay.GetLightPatches();
        var actions = _patchBay.GetActionPatches();
        _store.SaveGameOutputPatches(game.OverrideSystem, game.Rom, lights);
        var path = _store.SaveGameInputPatches(game.OverrideSystem, game.Rom, actions);

        _status.Text = lights.Count == 0 && actions.Count == 0
            ? L.T("Câblage remis aux réglages du pack.", "Wiring restored to pack settings.")
            : L.T($"Câblage enregistré ({System.IO.Path.GetFileName(path)}). Lumières : appliquées à la prochaine sélection du jeu."
                    + (actions.Count > 0 ? " Actions : cliquez « Mettre à jour ce jeu » (Contrôles) pour les pousser dans MAME." : ""),
                $"Wiring saved ({System.IO.Path.GetFileName(path)}). Lights: applied at the next game selection."
                    + (actions.Count > 0 ? " Actions: click \"Update this game\" (Controls) to push them into MAME." : ""));
    }

    /// <summary>One line per used button: "B3  ✱ Cadet Mission (lumière lamp2) · ● Abort (action)…"</summary>
    /// <summary>
    /// Wiring of the game's DEPLOYED MAME cfg in physical buttons — what really
    /// fires today (e.g. seawolf: Fire on B1/B2/B6/B8). The bay shows it as the
    /// default cables, ahead of the dynpanel template.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> LoadCurrentWiringAsync(string baseUrl, string rom)
    {
        var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var (ok, body) = await ApiExposeClient.GetAsync(baseUrl, $"/api/v1/panels/controls/mamecfg/current?rom={Uri.EscapeDataString(rom)}");
            if (!ok)
            {
                return result;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("wiring", out var wiring) || wiring.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return result;
            }

            foreach (var entry in wiring.EnumerateObject())
            {
                if (entry.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                var slots = entry.Value.EnumerateArray()
                    .Where(v => v.ValueKind == System.Text.Json.JsonValueKind.Number)
                    .Select(v => v.GetInt32())
                    .Where(s => s >= 1)
                    .ToArray();
                if (slots.Length > 0)
                {
                    result[entry.Name] = slots;
                }
            }
        }
        catch
        {
            // endpoint absent (older API): template defaults apply
        }

        return result;
    }

    /// <summary>Anchor roles of a slotless analog channel, from its label — the
    /// curator's peripheral families (spinner/dial/trackball/paddle/wheel/pedal…).</summary>
    private static IReadOnlyList<string> AnchorsForAnalog(string label)
    {
        var lower = label.ToLowerInvariant();
        if (lower.Contains("trackball") || lower.Contains("roller"))
        {
            return new[] { "x", "y" };
        }

        if (lower.Contains("spinner") || lower.Contains("dial") || lower.Contains("paddle")
            || lower.Contains("wheel") || lower.Contains("turntable") || lower.Contains("handlebar")
            || lower.Contains("volant"))
        {
            return new[] { "ccw", "cw" };
        }

        if (lower.Contains("pedal") || lower.Contains("pédale") || lower.Contains("throttle"))
        {
            return new[] { "press" };
        }

        if (lower.Contains("gun") || lower.Contains("crosshair"))
        {
            return new[] { "x", "y" };
        }

        return new[] { "x" };
    }

    /// <summary>Joystick anchors from the dynpanel ways ("8", "4", "2"…), falling
    /// back to the directions the game actually declares.</summary>
    private static IReadOnlyList<string> JoystickAnchors(string way, IEnumerable<string> declaredDirections)
    {
        if (way.Contains('8'))
        {
            return new[] { "up", "up-right", "right", "down-right", "down", "down-left", "left", "up-left" };
        }

        if (way.Contains('4'))
        {
            return new[] { "up", "right", "down", "left" };
        }

        var declared = declaredDirections.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (way.Contains('2') || declared.Length == 2)
        {
            return declared.Length == 2 ? declared : new[] { "left", "right" };
        }

        return declared.Length > 0 ? declared : new[] { "up", "right", "down", "left" };
    }

    /// <summary>Reads the "slot" property, tolerating null values (system inputs
    /// like START/COIN carry "slot": null in the projection).</summary>
    private static int? ReadSlotNumber(System.Text.Json.JsonElement element)
        => element.TryGetProperty("slot", out var s)
           && s.ValueKind == System.Text.Json.JsonValueKind.Number
           && s.TryGetInt32(out var n)
            ? n
            : null;

    private TextBlock? DescribeSlot(System.Text.Json.JsonElement slot)
    {
        var number = ReadSlotNumber(slot) ?? -1;
        if (number <= 0)
        {
            return null;
        }

        var block = new TextBlock { FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap };
        block.Inlines.Add(new System.Windows.Documents.Run($"B{number}  ") { FontWeight = FontWeights.Bold, Foreground = Text(0xE8, 0xE8, 0xF0) });
        var parts = 0;

        void Append(string glyph, string? color, string label, string kind)
        {
            if (parts++ > 0)
            {
                block.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Text(0x5A, 0x5A, 0x6A) });
            }

            block.Inlines.Add(new System.Windows.Documents.Run(glyph + " ") { Foreground = new SolidColorBrush(PanelColors.Resolve(color ?? "GRAY")) });
            block.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = Text(0xB8, 0xB8, 0xC6) });
            block.Inlines.Add(new System.Windows.Documents.Run($" ({kind})") { Foreground = Text(0x8A, 0x8A, 0x9A), FontSize = 10.5 });
        }

        if (slot.TryGetProperty("inputs", out var inputs) && inputs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var input in inputs.EnumerateArray())
            {
                var label = input.TryGetProperty("function", out var f) && !string.IsNullOrWhiteSpace(f.GetString())
                    ? f.GetString()!
                    : input.TryGetProperty("label", out var l) ? l.GetString() ?? "?" : "?";
                var color = input.TryGetProperty("color", out var c) ? c.GetString() : null;
                Append("●", color, label, L.T("action", "action"));
            }
        }

        if (slot.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var output in outputs.EnumerateArray())
            {
                var label = output.TryGetProperty("label", out var l) ? l.GetString() ?? "?" : "?";
                var color = output.TryGetProperty("color", out var c) ? c.GetString() : null;
                var name = output.TryGetProperty("outputName", out var on) ? on.GetString() : null;
                Append("✱", color, label, L.T($"lumière {name}", $"light {name}"));
            }
        }

        if (slot.TryGetProperty("axes", out var axes) && axes.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var axis in axes.EnumerateArray())
            {
                var label = axis.TryGetProperty("label", out var l) ? l.GetString() ?? "?" : "?";
                var color = axis.TryGetProperty("color", out var c) ? c.GetString() : null;
                Append("◆", color, label, L.T("axe", "axis"));
            }
        }

        return parts > 0 ? block : null;
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
            // historical function name centered on the dome (long lamp ids are
            // auto-hidden by the visual)
            _panel.SetSlotFunction(slot,
                _currentGame?.BySlot.TryGetValue(slot, out var pack) == true ? pack.Label : "");
        }

        // START/SELECT wear the game's system-input colors, or the lamp homed on
        // them (llander lamp0) — cached when the game sheet loads
        _panel.SetTarget("START", PanelColors.Resolve(_targetColorCache.TryGetValue("START", out var startColor) ? startColor : "GRAY"));
        _panel.SetTarget("SELECT", PanelColors.Resolve(_targetColorCache.TryGetValue("SELECT", out var selectColor) ? selectColor : "GRAY"));

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
        _status.Text = L.T("Modification non enregistrée — cliquez « Enregistrer ma configuration ».",
            "Unsaved change — click \"Save my configuration\".");
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
            ? L.T("Plus aucune personnalisation : votre configuration a été retirée.", "No customization left: your configuration was removed.")
            : L.T("Configuration enregistrée. LedManager l'applique dès la prochaine sélection de jeu.",
                "Configuration saved. LedManager applies it from the next game selection.");
    }

    private void OnResetToPack(object sender, RoutedEventArgs e)
    {
        if (_currentGame is not { } game)
        {
            return;
        }

        var confirm = MessageBox.Show(
            L.T($"Supprimer ma configuration du jeu {game.Rom} ?", $"Delete my {game.Rom} configuration?"),
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
