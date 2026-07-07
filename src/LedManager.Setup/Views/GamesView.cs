using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LedManager.Setup.Controls;
using LedManager.Setup.Data;
using LedManager.Setup.Localization;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Views;

/// <summary>
/// P2 game mapper, system level: shows the resolved panel of a system (Data Pack
/// colors + the user's override patch), lets the user repaint buttons from the
/// firmware palette, and writes the sparse override (overrides\systems\&lt;sys&gt;.json)
/// that the runtime applies live at the next game selection. Per-game arcade
/// overrides remain hand-written for now (overrides\games\&lt;sys&gt;\&lt;rom&gt;.json).
/// </summary>
public sealed class GamesView : UserControl
{
    private static readonly string[] Palette =
    {
        "WHITE", "GRAY", "RED", "GREEN", "BLUE", "YELLOW", "ORANGE", "GOLD", "LEMON",
        "LIME", "CYAN", "TURQUOISE", "AQUA", "TEAL", "PINK", "MAGENTA", "VIOLET", "PURPLE", "BLACK"
    };

    private readonly PanelLayoutDefinition _layoutDefinition;
    private readonly SystemPanelCatalog _catalog;
    private readonly SystemOverrideStore _store;
    private readonly PanelSurface _panel = new() { Interactive = true };
    private readonly ComboBox _systems;
    private readonly ComboBox _layouts;
    private readonly TextBlock _status;
    private readonly TextBlock _summary;
    private readonly ContextMenu _palette;

    private IReadOnlyList<SystemPanelCatalog.PanelLayout> _currentLayouts = Array.Empty<SystemPanelCatalog.PanelLayout>();
    private SystemPanelCatalog.PanelLayout? _currentLayout;
    private readonly Dictionary<int, string> _edited = new();
    private int _paletteSlot;
    private bool _loading;

    public GamesView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _layoutDefinition = layout;
        var pluginRoot = HardwareDescription.FindPluginRoot() ?? System.IO.Directory.GetCurrentDirectory();
        _catalog = new SystemPanelCatalog(pluginRoot);
        _store = new SystemOverrideStore(pluginRoot);

        _systems = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _layouts = new ComboBox { Width = 240, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
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
        header.Children.Add(new TextBlock { Text = "Panel", Foreground = Text(0xE8, 0xE8, 0xF0), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_layouts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Action(L.T("Enregistrer l'override", "Save the override"), OnSave, primary: true));
        buttons.Children.Add(Action(L.T("Annuler les modifications", "Discard changes"), (_, _) => ReloadOverride()));
        buttons.Children.Add(Action(L.T("Revenir aux couleurs du pack", "Back to pack colors"), OnResetToPack));

        var intro = new TextBlock
        {
            Text = L.T(
                "Cliquez un bouton du panel pour choisir sa couleur : votre patch est enregistré dans overrides\\systems "
                + "et appliqué par LedManager dès la prochaine sélection de jeu — le Data Pack n'est jamais modifié. "
                + "Le choix du panel sert d'aperçu (2/4/6/8 boutons et variantes historiques) ; l'override s'applique au système entier.",
                "Click a panel button to pick its color: your patch is saved under overrides\\systems and applied by "
                + "LedManager from the next game selection — the Data Pack is never modified. The panel selector is a "
                + "preview (2/4/6/8 buttons and historical variants); the override applies to the whole system."),
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
        stack.Children.Add(intro);
        stack.Children.Add(panelBorder);
        stack.Children.Add(_summary);
        stack.Children.Add(buttons);
        stack.Children.Add(_status);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        if (!_catalog.Available)
        {
            _status.Text = L.T(
                "Data Pack introuvable : le dossier APIExpose\\resources\\dynpanels\\systems doit exister à côté de LedManager.",
                "Data Pack not found: the APIExpose\\resources\\dynpanels\\systems folder must exist next to LedManager.");
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
        if (SelectedSystem is not { } system)
        {
            return;
        }

        _loading = true;
        _currentLayouts = _catalog.LoadLayouts(system);
        _layouts.Items.Clear();
        foreach (var layout in _currentLayouts)
        {
            _layouts.Items.Add(layout.Key.Contains(':')
                ? layout.DisplayName
                : layout.PanelButtons + " " + L.T("boutons", "buttons"));
        }

        // preview the richest generic layout by default
        var defaultIndex = _currentLayouts.ToList().FindIndex(l => !l.Key.Contains(':') && l.PanelButtons == 8);
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
        if (_loading || _layouts.SelectedIndex < 0 || _layouts.SelectedIndex >= _currentLayouts.Count)
        {
            return;
        }

        _currentLayout = _currentLayouts[_layouts.SelectedIndex];
        _panel.Build(_layoutDefinition, _currentLayout.PanelButtons, hasStart: true, hasSelect: true);
        Repaint();
    }

    private void ReloadOverride()
    {
        if (SelectedSystem is not { } system)
        {
            return;
        }

        _edited.Clear();
        foreach (var (slot, color) in _store.LoadSlotColors(system))
        {
            _edited[slot] = color;
        }

        if (_layouts.SelectedIndex >= 0 && _layouts.SelectedIndex < _currentLayouts.Count)
        {
            _currentLayout = _currentLayouts[_layouts.SelectedIndex];
            _panel.Build(_layoutDefinition, _currentLayout.PanelButtons, hasStart: true, hasSelect: true);
        }

        Repaint();
        _status.Text = _store.Exists(system)
            ? L.T("Override système chargé.", "System override loaded.")
            : L.T("Aucun override : couleurs du pack.", "No override: pack colors.");
    }

    private void Repaint()
    {
        if (_currentLayout is not { } layout)
        {
            return;
        }

        foreach (var slot in _panel.Slots)
        {
            var packColor = layout.BySlot.TryGetValue(slot, out var pack) ? pack.Color : "BLACK";
            var effective = _edited.TryGetValue(slot, out var over) ? over : packColor;
            _panel.SetSlot(slot, PanelColors.Resolve(effective));
        }

        _panel.SetTarget("START", PanelColors.Resolve(layout.StartColor));
        _panel.SetTarget("SELECT", PanelColors.Resolve(layout.SelectColor));

        _summary.Text = _edited.Count == 0
            ? L.T("Aucune personnalisation — tout vient du pack.", "No customization — everything comes from the pack.")
            : L.T("Personnalisé : ", "Customized: ") + string.Join(" · ",
                _edited.OrderBy(pair => pair.Key).Select(pair => $"B{pair.Key} → {pair.Value}"));
    }

    private void OnSlotClicked(int slot)
    {
        _paletteSlot = slot;
        _palette.PlacementTarget = _panel;
        _palette.IsOpen = true;
    }

    private ContextMenu BuildPalette()
    {
        var menu = new ContextMenu();
        var reset = new MenuItem { Header = L.T("Couleur du pack (retirer l'override)", "Pack color (remove override)") };
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
        if (color is null)
        {
            _edited.Remove(_paletteSlot);
        }
        else
        {
            // painting back the exact pack color = no override for that slot
            var pack = _currentLayout?.BySlot.TryGetValue(_paletteSlot, out var packEntry) == true ? packEntry.Color : null;
            if (color.Equals(pack, StringComparison.OrdinalIgnoreCase))
            {
                _edited.Remove(_paletteSlot);
            }
            else
            {
                _edited[_paletteSlot] = color;
            }
        }

        Repaint();
        _status.Text = L.T("Modification non enregistrée — cliquez « Enregistrer l'override ».",
            "Unsaved change — click \"Save the override\".");
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
            L.T($"Supprimer l'override du système {system} et revenir aux couleurs du pack ?",
                $"Delete the {system} system override and return to the pack colors?"),
            "LedManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _store.Delete(system);
        ReloadOverride();
        _status.Text = L.T("Override supprimé : couleurs du pack.", "Override deleted: pack colors.");
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
        button.Click += onClick;
        return button;
    }

    private static SolidColorBrush Text(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
