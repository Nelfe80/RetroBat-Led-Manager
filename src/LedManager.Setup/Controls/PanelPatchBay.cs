using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LedManager.Setup.Localization;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Controls;

/// <summary>
/// Nodal patch bay for a game's panel, laid out like the real cabinet:
/// game ACTIONS (cyan) on the left, the PANEL in the center (SELECT/START and the
/// LED buttons painted with their game colors), the LAMP families on the right
/// (ports facing the panel), and the physical PERIPHERALS in a footer - one LARGE
/// node per device with one anchor socket per way/axis (joy 2/4/8, spinner,
/// trackball, paddle, pedal…).
///
/// Gestures: drag a port to a button (it snaps and enlarges) - an action can be
/// wired to SEVERAL buttons (drop again on a wired button to unplug just that
/// one); a light re-homes to a single button, void = back to defaults. Clicking a
/// chip, a button or a device focuses its ramifications; clicking the void clears.
/// Lamp cables are neutral - the LED color lives on the button and the chip dot.
/// Dashed = current setting, solid = user override.
/// </summary>
public sealed class PanelPatchBay : UserControl
{
    public sealed record Port(
        string Id,
        string Kind,
        string Label,
        string? Group,
        string? PackColor,
        IReadOnlyList<int> PackSlots,
        string? DeviceKey = null,
        string? Anchor = null,
        string? InputRef = null,
        IReadOnlyList<string>? PackTargets = null);

    /// <summary>Physical peripheral drawn as a LARGE node in the footer, with one
    /// anchor socket per axis/way (joy 2/4/8, spinner, trackball, pedal…).</summary>
    public sealed record DeviceNode(string Key, string Label, string Type, IReadOnlyList<string> Anchors);

    public const string KindAction = "action";
    public const string KindLight = "light";

    /// <summary>Joystick / analog channel. Channels with a real MAME id (P1_LEFT,
    /// P1_UP…) are wirable to buttons like the curator allows - the stick keeps
    /// working, the buttons are OR-added at deploy. Label-only analog channels
    /// (Paddle…) stay informative.</summary>
    public const string KindAxis = "axis";

    private static bool IsWirableMameId(string id) => System.Text.RegularExpressions.Regex.IsMatch(id, @"^P\d+_[A-Z0-9_]+$");

    private static readonly Color ActionColor = Color.FromRgb(0x20, 0xE8, 0xE8);
    private static readonly Color AxisColor = Color.FromRgb(0x4A, 0x8C, 0xFF);
    private static readonly Color AccentColor = Color.FromRgb(0x8A, 0x2B, 0xE2);

    /// <summary>Lamp cables are neutral on purpose: the LED color belongs to the
    /// button and the chip dot, not to the wire (less rainbow spaghetti).</summary>
    private static readonly Color LampCableColor = Color.FromRgb(0x9A, 0x86, 0xC8);

    private static readonly string[] Palette =
    {
        "WHITE", "GRAY", "RED", "GREEN", "BLUE", "YELLOW", "ORANGE", "GOLD", "LEMON",
        "LIME", "CYAN", "TURQUOISE", "AQUA", "TEAL", "PINK", "MAGENTA", "VIOLET", "PURPLE", "BLACK"
    };

    private const double ChipWidth = 220;
    private const double ChipHeight = 24;
    private const double ChipGap = 5;
    private const double GroupGap = 10;
    private const double ButtonRadius = 22;
    private const double SnapRadius = 40;
    private const double ButtonSpacing = 78;

    private double CenterLeft => ChipWidth + 60;
    private double PanelWidth => 4 * ButtonSpacing + 10;
    private double RightColumnX => CenterLeft + PanelWidth + 56;

    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };

    private List<Port> _ports = new();
    private readonly Dictionary<string, HashSet<int>> _actionOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lightOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _lightTargetOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _lightDetached = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _colorOverrides = new(StringComparer.OrdinalIgnoreCase);
    private int _buttonCount = 8;
    private IReadOnlyDictionary<int, string> _slotColors = new Dictionary<int, string>();

    private readonly Dictionary<string, Point> _portPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Point> _groupPortPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Point> _buttonCenters = new();
    private readonly Dictionary<int, int> _cableFanLeft = new();
    private readonly Dictionary<int, int> _cableFanRight = new();

    private List<DeviceNode> _devices = new();
    private readonly Dictionary<(string Device, string Anchor), Point> _deviceAnchors = new();
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    private string? _dragPort;
    private string? _dragGroup;
    private Point _dragPosition;
    private int? _hoverSlot;
    private int? _lastPulsedSlot;
    private bool _animateFocusOnce;
    private int? _selectedSlot;
    private string? _selectedPort;
    private string? _selectedDevice;
    private string? _selectedTarget;
    private IReadOnlyDictionary<string, string> _targetColors = new Dictionary<string, string>();
    private readonly Dictionary<string, Point> _targetCenters = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    /// <summary>Raised when the selection focus changes: the related slots and
    /// system targets, so the host can echo them on the virtual/real panels.</summary>
    public event Action<IReadOnlyCollection<int>, IReadOnlyCollection<string>>? SelectionEchoed;

    /// <summary>Raised on chip double-click: the host shows the channel details.</summary>
    public event Action<Port>? PortInspected;

    public PanelPatchBay()
    {
        _canvas.Background = CreateDotGrid();
        // the bay is a viewport: dark plate in both app themes, like any node editor
        Content = new Border
        {
            Background = Ui.Viewport,
            CornerRadius = new CornerRadius(10),
            Child = _canvas
        };
        MinHeight = 260;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;
        _canvas.MouseLeftButtonDown += OnCanvasDown;
    }

    /// <summary>Subtle dotted grid, the classic node-editor backdrop. Also keeps the
    /// whole canvas hit-testable (a null background would swallow void clicks).</summary>
    private static Brush CreateDotGrid()
    {
        var dot = new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x34)),
            null,
            new EllipseGeometry(new Point(1.1, 1.1), 1.1, 1.1));
        var brush = new DrawingBrush(dot)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 22, 22),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 22, 22),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    public void Load(
        IReadOnlyList<Port> ports,
        IReadOnlyDictionary<string, (int? Slot, string? Color, IReadOnlyList<string> Targets)> lightPatches,
        IReadOnlyDictionary<string, IReadOnlyList<int>> actionPatches,
        int buttonCount,
        IReadOnlyList<DeviceNode>? devices = null,
        IReadOnlyDictionary<int, string>? slotColors = null,
        IReadOnlyDictionary<string, string>? targetColors = null)
    {
        _ports = ports.ToList();
        _devices = devices?.ToList() ?? new List<DeviceNode>();
        _slotColors = slotColors ?? new Dictionary<int, string>();
        _targetColors = targetColors ?? new Dictionary<string, string>();
        _buttonCount = Math.Clamp(buttonCount, 1, 8);
        _actionOverrides.Clear();
        _lightOverrides.Clear();
        _lightDetached.Clear();
        _colorOverrides.Clear();
        _selectedSlot = null;
        _selectedPort = null;
        _selectedDevice = null;

        // fully unwired light families start folded: seawolf's 16 explosion
        // lamps must not bury the useful chips (header click re-opens them)
        _collapsedGroups.Clear();
        foreach (var group in _ports.Where(p => p.Kind == KindLight && !string.IsNullOrWhiteSpace(p.Group)).GroupBy(p => p.Group!))
        {
            if (group.Count() > 3 && group.All(p => p.PackSlots.Count == 0))
            {
                _collapsedGroups.Add(group.Key);
            }
        }

        _lightTargetOverrides.Clear();
        foreach (var (name, patch) in lightPatches)
        {
            if (patch.Targets.Count > 0)
            {
                _lightTargetOverrides[name] = patch.Targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            else if (patch.Slot == 0)
            {
                _lightDetached.Add(name); // saved as detached (dead LED)
            }
            else if (patch.Slot is { } slot && slot >= 1 && slot <= _buttonCount)
            {
                _lightOverrides[name] = slot;
            }

            if (!string.IsNullOrWhiteSpace(patch.Color))
            {
                _colorOverrides[name] = patch.Color!;
            }
        }

        foreach (var (id, slots) in actionPatches)
        {
            _actionOverrides[id] = slots.Where(s => s >= 1 && s <= _buttonCount).ToHashSet();
        }

        Render();
    }

    public IReadOnlyDictionary<string, (int? Slot, string? Color, IReadOnlyList<string> Targets)> GetLightPatches()
    {
        var patches = new Dictionary<string, (int?, string?, IReadOnlyList<string>)>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in _ports.Where(p => p.Kind == KindLight))
        {
            // multi-home lights: ANY explicit re-home is a change; single-home
            // lights: only a different button is one
            var packSlot = port.PackSlots.Count == 1 ? port.PackSlots[0] : (int?)null;
            int? slot = _lightOverrides.TryGetValue(port.Id, out var s) && s != packSlot ? s : null;
            var color = _colorOverrides.TryGetValue(port.Id, out var c) && !c.Equals(port.PackColor, StringComparison.OrdinalIgnoreCase) ? c : null;
            var packTargets = (port.PackTargets ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var targets = _lightTargetOverrides.TryGetValue(port.Id, out var t) && t.Count > 0
                          && !t.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(packTargets, StringComparer.OrdinalIgnoreCase)
                ? t.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
            if (_lightDetached.Contains(port.Id))
            {
                patches[port.Id] = (0, color, Array.Empty<string>()); // 0 = détachée
            }
            else if (slot is not null || color is not null || targets.Length > 0)
            {
                patches[port.Id] = (slot, color, targets);
            }
        }

        return patches;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<int>> GetActionPatches()
    {
        var patches = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in _ports.Where(p => p.Kind == KindAction || (p.Kind == KindAxis && IsWirableMameId(p.Id))))
        {
            if (!_actionOverrides.TryGetValue(port.Id, out var slots))
            {
                continue;
            }

            var pack = port.PackSlots.OrderBy(s => s).ToArray();
            var mine = slots.OrderBy(s => s).ToArray();
            if (!pack.SequenceEqual(mine) && mine.Length > 0)
            {
                patches[port.Id] = mine;
            }
        }

        return patches;
    }

    private IReadOnlyCollection<int> EffectiveSlots(Port port)
    {
        if (port.Kind == KindAction)
        {
            return _actionOverrides.TryGetValue(port.Id, out var slots) ? slots : port.PackSlots.ToHashSet();
        }

        if (port.Kind == KindAxis)
        {
            // a wired direction points at buttons; otherwise its device anchor
            return _actionOverrides.TryGetValue(port.Id, out var axisSlots) ? axisSlots : port.PackSlots;
        }

        // a detached lamp lights nothing; one homed on system targets has no
        // button home; otherwise it keeps ALL its pack homes (READY_LAMP lights
        // B4+B3) until re-homed to one
        if (_lightDetached.Contains(port.Id))
        {
            return Array.Empty<int>();
        }

        if (_lightTargetOverrides.TryGetValue(port.Id, out var targets) && targets.Count > 0)
        {
            return Array.Empty<int>();
        }

        return _lightOverrides.TryGetValue(port.Id, out var slot)
            ? new[] { slot }
            : port.PackSlots;
    }

    /// <summary>System LEDs the lamp lights: user override first, else the
    /// dynpanel defaults (llander lamp0 → START + SELECT).</summary>
    private IReadOnlyCollection<string> EffectiveTargets(Port port)
    {
        if (port.Kind != KindLight)
        {
            return Array.Empty<string>();
        }

        if (_lightTargetOverrides.TryGetValue(port.Id, out var targets))
        {
            return targets;
        }

        return _lightOverrides.ContainsKey(port.Id)
            ? Array.Empty<string>() // re-homed on a button: pack targets let go
            : port.PackTargets ?? Array.Empty<string>();
    }

    private bool IsOverridden(Port port)
        => port.Kind is KindAction or KindAxis
            ? _actionOverrides.ContainsKey(port.Id)
            : _lightOverrides.ContainsKey(port.Id) || _colorOverrides.ContainsKey(port.Id)
              || _lightTargetOverrides.ContainsKey(port.Id) || _lightDetached.Contains(port.Id);

    /// <summary>Dot color of a chip: the real channel color (lamps keep their LED color).</summary>
    private Color DotColor(Port port)
        => port.Kind switch
        {
            KindAction => ActionColor,
            KindAxis => AxisColor,
            _ => PanelColors.Resolve(_colorOverrides.TryGetValue(port.Id, out var c) ? c : port.PackColor ?? "GRAY")
        };

    /// <summary>Cable color of a port: neutral for lamps (see LampCableColor).</summary>
    private static Color CableColor(Port port)
        => port.Kind switch
        {
            KindAction => ActionColor,
            KindAxis => AxisColor,
            _ => LampCableColor
        };

    // ------------------------------------------------------------ selection focus

    private bool HasSelection => _selectedSlot is not null || _selectedPort is not null || _selectedDevice is not null || _selectedTarget is not null;

    /// <summary>Does a lamp's input_ref point at a system target ("SYS START | Start",
    /// "SYS COIN | Coin")? SELECT is the coin/select side of the panel.</summary>
    private static bool RefersToTarget(string? inputRef, string target)
    {
        if (string.IsNullOrWhiteSpace(inputRef))
        {
            return false;
        }

        return target.Equals("START", StringComparison.OrdinalIgnoreCase)
            ? inputRef.Contains("SYS START", StringComparison.OrdinalIgnoreCase)
            : inputRef.Contains("SYS COIN", StringComparison.OrdinalIgnoreCase)
              || inputRef.Contains("SYS SELECT", StringComparison.OrdinalIgnoreCase);
    }

    private bool PortRelated(Port port)
    {
        if (_selectedPort is { } selectedPort)
        {
            return port.Id.Equals(selectedPort, StringComparison.OrdinalIgnoreCase);
        }

        if (_selectedDevice is { } selectedDevice)
        {
            return string.Equals(port.DeviceKey, selectedDevice, StringComparison.OrdinalIgnoreCase);
        }

        if (_selectedTarget is { } selectedTarget)
        {
            return RefersToTarget(port.InputRef, selectedTarget);
        }

        if (_selectedSlot is { } selectedSlot)
        {
            return EffectiveSlots(port).Contains(selectedSlot);
        }

        return true;
    }

    private bool SlotRelated(int slot)
    {
        if (_selectedSlot is { } selectedSlot)
        {
            return slot == selectedSlot;
        }

        if (_selectedPort is not null || _selectedDevice is not null)
        {
            return _ports.Where(PortRelated).Any(p => EffectiveSlots(p).Contains(slot));
        }

        return true;
    }

    private void ClearSelection()
    {
        _selectedSlot = null;
        _selectedPort = null;
        _selectedDevice = null;
        _selectedTarget = null;
        _animateFocusOnce = true; // the next Render fades the dimming in
    }

    /// <summary>Tells the host what the current focus touches, so it can echo it
    /// on the virtual panel and (in a live-test session) on the real one.</summary>
    private void RaiseEcho()
    {
        if (SelectionEchoed is null)
        {
            return;
        }

        if (!HasSelection)
        {
            SelectionEchoed(Array.Empty<int>(), Array.Empty<string>());
            return;
        }

        var slots = _ports.Where(PortRelated).SelectMany(EffectiveSlots).ToHashSet();
        if (_selectedSlot is { } selected)
        {
            slots.Add(selected);
        }

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedTarget is { } target)
        {
            targets.Add(target);
        }

        foreach (var port in _ports.Where(PortRelated))
        {
            foreach (var homed in EffectiveTargets(port))
            {
                targets.Add(homed);
            }
        }

        SelectionEchoed(slots, targets);
    }

    // ------------------------------------------------------------------ render

    private static readonly int[] TopRow = { 4, 3, 5, 7 };
    private static readonly int[] BottomRow = { 1, 2, 6, 8 };

    private void Render()
    {
        _canvas.Children.Clear();
        _portPoints.Clear();
        _groupPortPoints.Clear();
        _buttonCenters.Clear();
        _cableFanLeft.Clear();
        _cableFanRight.Clear();

        // --- left column: game actions + button-homed axes ---
        double leftY = 4;
        var actions = _ports.Where(p => p.Kind == KindAction).ToList();
        if (actions.Count > 0)
        {
            leftY = DrawSection(
                L.T($"actions du jeu ({actions.Count})", $"game actions ({actions.Count})"),
                ActionColor, actions, 4, leftY, portsOnRight: true, groupPort: false, collapsible: false);
        }

        var slottedAxes = _ports.Where(p => p.Kind == KindAxis && p.PackSlots.Count > 0).ToList();
        if (slottedAxes.Count > 0)
        {
            leftY = DrawSection(
                L.T($"axes sur boutons ({slottedAxes.Count})", $"axes on buttons ({slottedAxes.Count})"),
                AxisColor, slottedAxes, 4, leftY, portsOnRight: true, groupPort: false, collapsible: false);
        }

        // --- center: the panel (SELECT/START + LED buttons) ---
        DrawTargetsRow(CenterLeft + 8);
        DrawButtonsArea(CenterLeft + 8);
        var panelBottom = 218 + ButtonRadius + 30;

        // --- right column: lamp families (wired first, unwired folded) ---
        double rightY = 4;
        foreach (var group in _ports.Where(p => p.Kind == KindLight)
                     .GroupBy(p => string.IsNullOrWhiteSpace(p.Group) ? L.T("lumières", "lights") : p.Group!)
                     .OrderBy(g => g.All(p => p.PackSlots.Count == 0) ? 1 : 0))
        {
            var members = group.ToList();
            rightY = DrawSection(
                $"{group.Key} ({members.Count})",
                AccentColor, members, RightColumnX, rightY, portsOnRight: false,
                groupPort: members.Count > 1, collapsible: true);
        }

        // --- footer: peripherals, one big node per device with its channels ---
        var footerTop = Math.Max(Math.Max(leftY, rightY), panelBottom) + 8;
        var footerBottom = DrawDeviceFooter(footerTop);

        // --- cables ---
        foreach (var port in _ports)
        {
            if (!_portPoints.TryGetValue(port.Id, out var start))
            {
                continue;
            }

            var related = PortRelated(port);

            // unwired device channels land on their anchor socket; once the user
            // wires a direction to buttons, the button cables take over
            if (port.Kind == KindAxis && EffectiveSlots(port).Count == 0)
            {
                if (port.DeviceKey is { } device
                    && _deviceAnchors.TryGetValue((device, port.Anchor ?? ""), out var anchor))
                {
                    DrawCable(start, anchor, CableColor(port), solid: false,
                        focused: related && HasSelection, dimmed: HasSelection && !related);
                }

                continue;
            }

            foreach (var slot in EffectiveSlots(port))
            {
                if (!_buttonCenters.TryGetValue(slot, out var center))
                {
                    continue;
                }

                var end = FanEndpoint(slot, center, fromRight: start.X > center.X);
                var slotOk = _selectedSlot is null || _selectedSlot == slot;
                var focused = related && slotOk && HasSelection;
                DrawCable(start, end, CableColor(port), IsOverridden(port),
                    focused: focused, dimmed: HasSelection && (!related || !slotOk));
            }

            // trigger link: a lamp driven by SYS START / SYS COIN also cables to
            // that system button (llander EXP lamps flash on start)
            if (port.Kind == KindLight)
            {
                foreach (var targetName in new[] { "START", "SELECT" })
                {
                    if (RefersToTarget(port.InputRef, targetName) && _targetCenters.TryGetValue(targetName, out var targetCenter))
                    {
                        DrawCable(start, new Point(targetCenter.X, targetCenter.Y + 11), CableColor(port), solid: false,
                            focused: related && HasSelection, dimmed: HasSelection && !related);
                    }
                }

                // lamp homed on system LEDs: dashed = dynpanel default, solid = user
                var targetOverridden = _lightTargetOverrides.ContainsKey(port.Id);
                foreach (var homedTarget in EffectiveTargets(port))
                {
                    if (_targetCenters.TryGetValue(homedTarget, out var targetCenter))
                    {
                        DrawCable(start, new Point(targetCenter.X, targetCenter.Y + 11), CableColor(port), solid: targetOverridden,
                            focused: related && HasSelection, dimmed: HasSelection && !related);
                    }
                }
            }
        }

        // --- dragged cable ---
        if ((_dragPort ?? _dragGroup) is not null)
        {
            var start = _dragPort is { } p && _portPoints.TryGetValue(p, out var pp) ? pp
                : _dragGroup is { } g && _groupPortPoints.TryGetValue(g, out var gp) ? gp
                : default;
            var color = _dragPort is { } dragId
                ? CableColor(_ports.First(x => x.Id.Equals(dragId, StringComparison.OrdinalIgnoreCase)))
                : AccentColor;
            var end = _hoverSlot is { } hover && _buttonCenters.TryGetValue(hover, out var snap) ? snap : _dragPosition;
            DrawCable(start, end, color, solid: true, focused: true, dimmed: false);
        }

        _canvas.Height = footerBottom + 8;
        _canvas.Width = RightColumnX + ChipWidth + 24;
        _animateFocusOnce = false; // the fade only accompanies the click that armed it
    }

    /// <summary>One chip section (header + chips); returns the next free Y.</summary>
    private double DrawSection(
        string headerText, Color headerColor, List<Port> ports,
        double x, double y, bool portsOnRight, bool groupPort, bool collapsible)
    {
        var groupKey = ports[0].Group ?? headerText;
        collapsible = collapsible && !string.IsNullOrWhiteSpace(ports[0].Group);
        var collapsed = collapsible && _collapsedGroups.Contains(groupKey);

        var header = new TextBlock
        {
            Text = (collapsible ? (collapsed ? "▸ " : "▾ ") : "") + headerText.ToUpperInvariant(),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(headerColor),
            Cursor = collapsible ? Cursors.Hand : Cursors.Arrow,
            ToolTip = collapsible
                ? L.T("Cliquer pour plier/déplier cette famille", "Click to fold/unfold this family")
                : null
        };
        if (collapsible)
        {
            header.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (!_collapsedGroups.Remove(groupKey))
                {
                    _collapsedGroups.Add(groupKey);
                }

                Render();
            };
        }

        var tick = new Border
        {
            Width = 3,
            Height = 11,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(headerColor),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tick, x);
        Canvas.SetTop(tick, y + 1);
        _canvas.Children.Add(tick);
        Canvas.SetLeft(header, x + 8);
        Canvas.SetTop(header, y);
        _canvas.Children.Add(header);

        if (groupPort && !collapsed)
        {
            var key = headerText;
            var point = portsOnRight ? new Point(x + ChipWidth + 8, y + 8) : new Point(x - 8, y + 8);
            _groupPortPoints[key] = point;
            DrawPort(point, new SolidColorBrush(AccentColor), 6, (_, e) => { _dragGroup = key; BeginDrag(e); });
        }

        y += 18;
        if (!collapsed)
        {
            foreach (var port in ports)
            {
                DrawChip(port, x, y, portsOnRight);
                y += ChipHeight + ChipGap;
            }
        }

        return y + GroupGap;
    }

    // ---------------------------------------------------------- device footer

    /// <summary>Anchor roles with a physical meaning sit at their true angle
    /// (degrees, 0 = east, clockwise); other roles spread on the left side.</summary>
    private static readonly Dictionary<string, double> AnchorAngles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["right"] = 0,
        ["down-right"] = 45,
        ["down"] = 90,
        ["down-left"] = 135,
        ["left"] = 180,
        ["up-left"] = 225,
        ["up"] = 270,
        ["up-right"] = 315,
        ["cw"] = 315,
        ["ccw"] = 225,
        ["x"] = 180,
        ["y"] = 270,
        ["press"] = 180
    };

    /// <summary>
    /// Footer: one block per peripheral - its channel chips on the left, its LARGE
    /// node on the right with one anchor socket per way/axis (the curator's
    /// granularity). Clicking the node focuses its ramifications.
    /// Returns the bottom Y consumed.
    /// </summary>
    private double DrawDeviceFooter(double top)
    {
        _deviceAnchors.Clear();
        if (_devices.Count == 0)
        {
            return Math.Max(top, 260);
        }

        var headerText = new TextBlock
        {
            Text = L.T("PÉRIPHÉRIQUES", "PERIPHERALS"),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(AxisColor)
        };
        var tick = new Border
        {
            Width = 3,
            Height = 11,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(AxisColor),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tick, 4);
        Canvas.SetTop(tick, top + 1);
        _canvas.Children.Add(tick);
        Canvas.SetLeft(headerText, 12);
        Canvas.SetTop(headerText, top);
        _canvas.Children.Add(headerText);

        const double nodeRadius = 40;
        var blockTop = top + 20;
        var x = 4.0;
        var bottom = blockTop;

        foreach (var device in _devices)
        {
            var channels = _ports.Where(p => p.Kind == KindAxis && string.Equals(p.DeviceKey, device.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var chipsHeight = channels.Count * (ChipHeight + ChipGap);
            var blockHeight = Math.Max(chipsHeight, nodeRadius * 2 + 26);
            var chipY = blockTop + Math.Max(0, (blockHeight - 26 - chipsHeight) / 2);
            foreach (var port in channels)
            {
                DrawChip(port, x, chipY, portsOnRight: true);
                chipY += ChipHeight + ChipGap;
            }

            var center = new Point(x + ChipWidth + 34 + nodeRadius, blockTop + nodeRadius);
            DrawDeviceNode(device, center, nodeRadius);
            bottom = Math.Max(bottom, blockTop + blockHeight);
            x += ChipWidth + 34 + nodeRadius * 2 + 56;
        }

        return bottom + 12;
    }

    private void DrawDeviceNode(DeviceNode device, Point center, double radius)
    {
        var isSelected = string.Equals(_selectedDevice, device.Key, StringComparison.OrdinalIgnoreCase);
        var dimmed = HasSelection && !isSelected && _selectedDevice is not null;

        var plate = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.3),
                Center = new Point(0.5, 0.45),
                RadiusX = 0.75,
                RadiusY = 0.75,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x2A, 0x36, 0x4E), 0),
                    new GradientStop(Color.FromRgb(0x1B, 0x24, 0x38), 0.7),
                    new GradientStop(Color.FromRgb(0x12, 0x18, 0x26), 1)
                }
            },
            Stroke = new SolidColorBrush(isSelected ? AccentColor : AxisColor) { Opacity = isSelected ? 1 : 0.6 },
            StrokeThickness = isSelected ? 3 : 1.5,
            Opacity = dimmed ? 0.4 : 1,
            Cursor = Cursors.Hand
        };
        Canvas.SetLeft(plate, center.X - radius);
        Canvas.SetTop(plate, center.Y - radius);
        plate.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var wasSelected = string.Equals(_selectedDevice, device.Key, StringComparison.OrdinalIgnoreCase);
            ClearSelection();
            _selectedDevice = wasSelected ? null : device.Key;
            Render();
            RaiseEcho();
        };
        _canvas.Children.Add(plate);

        var knob = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = new SolidColorBrush(AxisColor),
            IsHitTestVisible = false,
            Opacity = dimmed ? 0.4 : 1
        };
        Canvas.SetLeft(knob, center.X - 8);
        Canvas.SetTop(knob, center.Y - 8);
        _canvas.Children.Add(knob);

        // anchor sockets, one per way/axis
        var freeIndex = 0;
        foreach (var anchor in device.Anchors)
        {
            var angle = AnchorAngles.TryGetValue(anchor, out var known)
                ? known
                : 150 + freeIndex++ * 35; // unknown roles spread bottom-left
            var radians = angle * Math.PI / 180;
            var point = new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
            _deviceAnchors[(device.Key, anchor)] = point;

            var socketHalo = new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = new SolidColorBrush(AxisColor),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x18, 0x26)),
                IsHitTestVisible = false,
                Opacity = dimmed ? 0.4 : 1
            };
            Canvas.SetLeft(socketHalo, point.X - 7);
            Canvas.SetTop(socketHalo, point.Y - 7);
            _canvas.Children.Add(socketHalo);

            var socket = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(AxisColor),
                IsHitTestVisible = false,
                Opacity = dimmed ? 0.4 : 1
            };
            Canvas.SetLeft(socket, point.X - 3);
            Canvas.SetTop(socket, point.Y - 3);
            _canvas.Children.Add(socket);
        }

        var label = new TextBlock
        {
            Text = device.Label.ToUpperInvariant(),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(AxisColor) { Opacity = 0.85 },
            TextAlignment = TextAlignment.Center,
            Width = radius * 2 + 40,
            IsHitTestVisible = false,
            Opacity = dimmed ? 0.4 : 1
        };
        Canvas.SetLeft(label, center.X - radius - 20);
        Canvas.SetTop(label, center.Y + radius + 10);
        _canvas.Children.Add(label);
    }

    /// <summary>Focus fade: when a selection click just happened, dimmed elements
    /// settle to their opacity in a short fade instead of snapping.</summary>
    private void ApplyFocusFade(UIElement element, double target)
    {
        if (!_animateFocusOnce || target >= 1)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1.0, target, TimeSpan.FromMilliseconds(150))
            {
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            });
    }

    /// <summary>Connection spark: a bright dot runs along the freshly plugged cable.</summary>
    private void PlayConnectionSpark(Point from, Point to, Color color)
    {
        var mid = (from.X + to.X) / 2;
        var path = new PathGeometry(new[]
        {
            new PathFigure(from, new PathSegment[]
            {
                new BezierSegment(new Point(mid, from.Y), new Point(mid, to.Y), to, isStroked: true)
            }, closed: false)
        });

        var spark = new Ellipse
        {
            Width = 9,
            Height = 9,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(color, 0.5),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
                }
            }
        };
        var transform = new MatrixTransform();
        spark.RenderTransform = transform;
        Canvas.SetLeft(spark, -4.5);
        Canvas.SetTop(spark, -4.5);
        _canvas.Children.Add(spark);

        var travel = new System.Windows.Media.Animation.MatrixAnimationUsingPath
        {
            PathGeometry = path,
            Duration = TimeSpan.FromMilliseconds(320),
            DoesRotateWithTangent = false
        };
        travel.Completed += (_, _) => _canvas.Children.Remove(spark);
        transform.BeginAnimation(MatrixTransform.MatrixProperty, travel);
    }

    /// <summary>Multiple cables landing on one button fan out around its rim, on
    /// the side the cable comes from (actions arrive left, lamps arrive right).</summary>
    private Point FanEndpoint(int slot, Point center, bool fromRight)
    {
        var fan = fromRight ? _cableFanRight : _cableFanLeft;
        var index = fan.TryGetValue(slot, out var n) ? n : 0;
        fan[slot] = index + 1;
        var baseAngle = fromRight ? 0 : Math.PI;
        var angle = baseAngle + (index - 1.5) * 0.45;
        return new Point(center.X + Math.Cos(angle) * (ButtonRadius - 2), center.Y + Math.Sin(angle) * (ButtonRadius - 2));
    }

    private void DrawChip(Port port, double x, double y, bool portsOnRight)
    {
        var related = PortRelated(port);
        var isSelected = _selectedPort is { } sp && port.Id.Equals(sp, StringComparison.OrdinalIgnoreCase);
        var chip = new Border
        {
            Width = ChipWidth,
            Height = ChipHeight,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x36)),
            BorderBrush = new SolidColorBrush(isSelected ? AccentColor : IsOverridden(port) ? AccentColor : Color.FromRgb(0x3A, 0x3A, 0x52)),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            Opacity = HasSelection && !related ? 0.35 : 1,
            Cursor = Cursors.Hand,
            ToolTip = port.Id
        };
        ApplyFocusFade(chip, HasSelection && !related ? 0.35 : 1);

        // clicking the chip body focuses this channel's ramifications;
        // double-click opens the inspector
        chip.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                PortInspected?.Invoke(port);
                return;
            }

            var wasSelected = _selectedPort is { } current && port.Id.Equals(current, StringComparison.OrdinalIgnoreCase);
            ClearSelection();
            _selectedPort = wasSelected ? null : port.Id;
            Render();
            RaiseEcho();
        };

        var inner = new DockPanel { LastChildFill = true };
        var dot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(7, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(DotColor(port)),
            Cursor = port.Kind == KindLight ? Cursors.Hand : Cursors.Arrow
        };
        if (port.Kind == KindLight)
        {
            dot.MouseLeftButtonDown += (_, e) => { e.Handled = true; OpenPalette(port, dot); };
        }

        DockPanel.SetDock(dot, Dock.Left);
        inner.Children.Add(dot);
        inner.Children.Add(new TextBlock
        {
            Text = port.Label,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC6)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 6, 0)
        });
        chip.Child = inner;
        Canvas.SetLeft(chip, x);
        Canvas.SetTop(chip, y);
        _canvas.Children.Add(chip);

        var point = portsOnRight
            ? new Point(x + ChipWidth + 8, y + ChipHeight / 2)
            : new Point(x - 8, y + ChipHeight / 2);
        _portPoints[port.Id] = point;
        if (port.Kind == KindAxis && !IsWirableMameId(port.Id))
        {
            // label-only analog channel: shown wired to its device, not draggable
            DrawPort(point, new SolidColorBrush(DotColor(port)), 5, (_, e) => e.Handled = true);
        }
        else
        {
            DrawPort(point, new SolidColorBrush(DotColor(port)), 5, (_, e) => { _dragPort = port.Id; BeginDrag(e); });
        }
    }

    private void DrawPort(Point at, Brush fill, double radius, MouseButtonEventHandler onDown)
    {
        var halo = new Ellipse
        {
            Width = radius * 2 + 6,
            Height = radius * 2 + 6,
            Fill = Brushes.Transparent,
            Stroke = fill,
            StrokeThickness = 2,
            Cursor = Cursors.Hand
        };
        var core = new Ellipse { Width = radius * 2 - 2, Height = radius * 2 - 2, Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(halo, at.X - radius - 3);
        Canvas.SetTop(halo, at.Y - radius - 3);
        Canvas.SetLeft(core, at.X - radius + 1);
        Canvas.SetTop(core, at.Y - radius + 1);
        halo.MouseLeftButtonDown += onDown;
        _canvas.Children.Add(halo);
        _canvas.Children.Add(core);
    }

    /// <summary>SELECT and START on the panel, painted with the game's colors and
    /// clickable: focusing one highlights the lamps its input triggers (llander's
    /// EXP lamps flash on SYS START). Recoloring them is a later runtime pass.</summary>
    private void DrawTargetsRow(double left)
    {
        _targetCenters.Clear();
        var x = left + 8;
        foreach (var name in new[] { "SELECT", "START" })
        {
            const double radius = 13;
            var center = new Point(x + radius, 26);
            _targetCenters[name] = center;

            // a lamp homed here paints the LED with its color, else the game color
            var homedLamp = _ports.FirstOrDefault(p => p.Kind == KindLight && EffectiveTargets(p).Contains(name));
            var colorName = homedLamp is not null
                ? (_colorOverrides.TryGetValue(homedLamp.Id, out var oc) ? oc : homedLamp.PackColor ?? "GRAY")
                : _targetColors.TryGetValue(name, out var c) && !string.IsNullOrWhiteSpace(c) ? c : null;
            var lit = colorName is not null && !colorName.Equals("BLACK", StringComparison.OrdinalIgnoreCase);
            var fill = lit ? PanelColors.Resolve(colorName!) : Color.FromRgb(0x26, 0x26, 0x30);
            var isSelected = string.Equals(_selectedTarget, name, StringComparison.OrdinalIgnoreCase);
            var dimmed = HasSelection && !isSelected && _selectedTarget is not null;

            if (lit && !dimmed)
            {
                var glow = new Ellipse
                {
                    Width = (radius + 8) * 2,
                    Height = (radius + 8) * 2,
                    IsHitTestVisible = false,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(64, fill.R, fill.G, fill.B), 0),
                            new GradientStop(Color.FromArgb(0, fill.R, fill.G, fill.B), 1)
                        }
                    }
                };
                Canvas.SetLeft(glow, center.X - radius - 8);
                Canvas.SetTop(glow, center.Y - radius - 8);
                _canvas.Children.Add(glow);
            }

            var button = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.35, 0.3),
                    GradientStops =
                    {
                        new GradientStop(Mix(fill, Colors.White, lit ? 0.45 : 0.1), 0),
                        new GradientStop(fill, 0.62),
                        new GradientStop(Mix(fill, Colors.Black, 0.38), 1)
                    }
                },
                Stroke = new SolidColorBrush(isSelected ? AccentColor : Color.FromRgb(0x3A, 0x3A, 0x52)),
                StrokeThickness = isSelected ? 2.5 : 1.5,
                Opacity = dimmed ? 0.45 : 1,
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(button, center.X - radius);
            Canvas.SetTop(button, center.Y - radius);
            var captured = name;
            button.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                var wasSelected = string.Equals(_selectedTarget, captured, StringComparison.OrdinalIgnoreCase);
                ClearSelection();
                _selectedTarget = wasSelected ? null : captured;
                Render();
                RaiseEcho();
            };
            _canvas.Children.Add(button);

            var label = new TextBlock
            {
                Text = name,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A)),
                Opacity = dimmed ? 0.45 : 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, center.X - 16);
            Canvas.SetTop(label, center.Y + radius + 3);
            _canvas.Children.Add(label);

            x += 64;
        }
    }

    private void DrawButtonsArea(double left)
    {
        void DrawRow(int[] arrangement, double rowY)
        {
            var column = 0;
            foreach (var slot in arrangement)
            {
                if (slot > _buttonCount)
                {
                    column++;
                    continue;
                }

                var center = new Point(left + column * ButtonSpacing + ButtonRadius, rowY);
                _buttonCenters[slot] = center;

                var wiredLights = _ports.Where(p => p.Kind == KindLight && EffectiveSlots(p).Contains(slot)).ToList();
                var wiredActions = _ports.Where(p => p.Kind == KindAction && EffectiveSlots(p).Contains(slot)).ToList();

                // LED color priority: wired lamp → the game's resolved slot color
                // (mslug buttons are Red/Yellow/Green without any lamp) → dark
                var lampColor = wiredLights.Count > 0
                    ? PanelColors.Resolve(_colorOverrides.TryGetValue(wiredLights[0].Id, out var c) ? c : wiredLights[0].PackColor ?? "GRAY")
                    : (Color?)null;
                if (lampColor is null && _slotColors.TryGetValue(slot, out var slotColor)
                    && !slotColor.Equals("BLACK", StringComparison.OrdinalIgnoreCase))
                {
                    lampColor = PanelColors.Resolve(slotColor);
                }

                var isLit = lampColor is not null;
                var fill = lampColor ?? Color.FromRgb(0x22, 0x22, 0x2C);

                var isHover = _hoverSlot == slot;
                var isSelected = _selectedSlot == slot;
                var radius = isHover ? ButtonRadius + 4 : ButtonRadius;
                var dimButton = HasSelection && !SlotRelated(slot);

                // soft halo under a lit LED
                if (isLit && !dimButton)
                {
                    var glowRadius = radius + 11;
                    var glow = new Ellipse
                    {
                        Width = glowRadius * 2,
                        Height = glowRadius * 2,
                        IsHitTestVisible = false,
                        Fill = new RadialGradientBrush
                        {
                            GradientStops =
                            {
                                new GradientStop(Color.FromArgb(72, fill.R, fill.G, fill.B), 0),
                                new GradientStop(Color.FromArgb(0, fill.R, fill.G, fill.B), 1)
                            }
                        }
                    };
                    Canvas.SetLeft(glow, center.X - glowRadius);
                    Canvas.SetTop(glow, center.Y - glowRadius);
                    _canvas.Children.Add(glow);
                }

                // domed LED look: bright hot spot off-center, darker rim
                var led = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.35, 0.3),
                    Center = new Point(0.5, 0.45),
                    RadiusX = 0.75,
                    RadiusY = 0.75,
                    GradientStops =
                    {
                        new GradientStop(Mix(fill, Colors.White, isLit ? 0.5 : 0.12), 0),
                        new GradientStop(fill, 0.62),
                        new GradientStop(Mix(fill, Colors.Black, 0.38), 1)
                    }
                };

                var button = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = led,
                    Stroke = new SolidColorBrush(isHover || isSelected ? AccentColor : Color.FromRgb(0x3A, 0x3A, 0x52)),
                    StrokeThickness = isHover ? 4 : isSelected ? 3 : 1.5,
                    Opacity = dimButton ? 0.45 : 1,
                    Cursor = Cursors.Hand,
                    Tag = slot
                };
                Canvas.SetLeft(button, center.X - radius);
                Canvas.SetTop(button, center.Y - radius);
                ApplyFocusFade(button, dimButton ? 0.45 : 1);

                // magnet pulse: one breath when the dragged cable snaps this button
                if (isHover && _lastPulsedSlot != slot)
                {
                    _lastPulsedSlot = slot;
                    var scale = new ScaleTransform(1, 1, radius, radius);
                    button.RenderTransform = scale;
                    var pulse = new System.Windows.Media.Animation.DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new System.Windows.Media.Animation.BackEase { Amplitude = 0.6, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                }
                else if (!isHover && _lastPulsedSlot == slot)
                {
                    _lastPulsedSlot = null;
                }
                button.MouseLeftButtonDown += (_, e) =>
                {
                    if (_dragPort is null && _dragGroup is null)
                    {
                        e.Handled = true;
                        var wasSelected = _selectedSlot == slot;
                        ClearSelection();
                        _selectedSlot = wasSelected ? null : slot;
                        Render();
                        RaiseEcho();
                    }
                };
                _canvas.Children.Add(button);

                // action badge: cyan tick when the button triggers something
                if (wiredActions.Count > 0)
                {
                    var badge = new Ellipse
                    {
                        Width = 9,
                        Height = 9,
                        Fill = new SolidColorBrush(ActionColor),
                        Opacity = dimButton ? 0.45 : 1,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(badge, center.X + ButtonRadius - 10);
                    Canvas.SetTop(badge, center.Y - ButtonRadius + 1);
                    _canvas.Children.Add(badge);
                }

                var label = new TextBlock
                {
                    Text = $"B{slot}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF0)),
                    Opacity = dimButton ? 0.45 : 1
                };
                Canvas.SetLeft(label, center.X - 9);
                Canvas.SetTop(label, center.Y + ButtonRadius + 5);
                _canvas.Children.Add(label);

                column++;
            }
        }

        DrawRow(TopRow, 112);
        DrawRow(BottomRow, 218);
    }

    private void DrawCable(Point from, Point to, Color color, bool solid, bool focused, bool dimmed)
    {
        var mid = (from.X + to.X) / 2;
        var geometry = new PathGeometry(new[]
        {
            new PathFigure(from, new PathSegment[]
            {
                new BezierSegment(new Point(mid, from.Y), new Point(mid, to.Y), to, isStroked: true)
            }, closed: false)
        });

        var coreThickness = focused ? 3.4 : solid ? 2.4 : 1.8;

        // wide translucent underlay = the neon glow of the cable
        if (!dimmed)
        {
            _canvas.Children.Add(new Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(color) { Opacity = focused ? 0.32 : 0.14 },
                StrokeThickness = coreThickness + 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            });
        }

        _canvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(color) { Opacity = dimmed ? 0.12 : focused ? 1.0 : solid ? 0.85 : 0.5 },
            StrokeThickness = coreThickness,
            StrokeDashArray = solid ? null : new DoubleCollection { 4, 3 },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        });
    }

    // ------------------------------------------------------------------ interaction

    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        // click on empty space clears the selection focus
        if (_dragPort is null && _dragGroup is null && e.OriginalSource == _canvas && HasSelection)
        {
            ClearSelection();
            Render();
            RaiseEcho();
        }
    }

    private void BeginDrag(MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragPosition = e.GetPosition(_canvas);
        _hoverSlot = null;
        ClearSelection();
        _canvas.CaptureMouse();
        Render();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPort is null && _dragGroup is null)
        {
            return;
        }

        _dragPosition = e.GetPosition(_canvas);
        _hoverSlot = NearestSlot(_dragPosition);
        Render();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragPort is null && _dragGroup is null)
        {
            return;
        }

        var at = e.GetPosition(_canvas);
        var target = NearestSlot(at);
        var systemTarget = target is null ? NearestTarget(at) : null;
        string? droppedPort = _dragPort;
        if (_dragPort is { } id)
        {
            ApplyDrop(_ports.First(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)), target, systemTarget);
        }
        else if (_dragGroup is { } groupKey)
        {
            foreach (var port in _ports.Where(p => p.Kind == KindLight
                         && $"{(string.IsNullOrWhiteSpace(p.Group) ? L.T("lumières", "lights") : p.Group!)} ({_ports.Count(x => x.Kind == KindLight && string.Equals(x.Group, p.Group, StringComparison.OrdinalIgnoreCase))})" == groupKey))
            {
                ApplyDrop(port, target, systemTarget);
            }
        }

        _dragPort = null;
        _dragGroup = null;
        _hoverSlot = null;
        _lastPulsedSlot = null;
        _canvas.ReleaseMouseCapture();

        // brief connection flash: the dropped channel keeps the spotlight
        if (droppedPort is not null && (target is not null || systemTarget is not null))
        {
            _selectedPort = droppedPort;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_selectedPort == droppedPort)
                {
                    _selectedPort = null;
                    Render();
                }
            };
            timer.Start();
        }

        Render();

        // connection spark: a bright dot runs along the freshly plugged cable
        if (droppedPort is not null && _portPoints.TryGetValue(droppedPort, out var sparkFrom))
        {
            var port = _ports.First(p => p.Id.Equals(droppedPort, StringComparison.OrdinalIgnoreCase));
            Point? sparkTo = target is { } droppedSlot && _buttonCenters.TryGetValue(droppedSlot, out var buttonCenter)
                ? buttonCenter
                : systemTarget is { } droppedTarget && _targetCenters.TryGetValue(droppedTarget, out var targetCenter)
                    ? targetCenter
                    : null;
            if (sparkTo is { } destination)
            {
                PlayConnectionSpark(sparkFrom, destination, CableColor(port));
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Snap detection on the START/SELECT LEDs, for lamp re-homing.</summary>
    private string? NearestTarget(Point at)
    {
        string? best = null;
        var bestDistance = SnapRadius;
        foreach (var (name, center) in _targetCenters)
        {
            var distance = (center - at).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = name;
            }
        }

        return best;
    }

    /// <summary>
    /// Action dropped on a button toggles that button in its set (multi-wiring);
    /// a direction/axis does the same but may empty its set (back to the device
    /// anchor); a light moves its single home - a button, or one/both system LEDs
    /// (drop on START/SELECT toggles them, llander's lamp0 can light both).
    /// Void = back to defaults.
    /// </summary>
    private void ApplyDrop(Port port, int? slot, string? systemTarget = null)
    {
        if (port.Kind == KindLight && systemTarget is { } targetName)
        {
            var targets = _lightTargetOverrides.TryGetValue(port.Id, out var existingTargets)
                ? existingTargets
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!targets.Add(targetName))
            {
                targets.Remove(targetName);
            }

            if (targets.Count == 0)
            {
                _lightTargetOverrides.Remove(port.Id);
            }
            else
            {
                _lightTargetOverrides[port.Id] = targets;
                _lightOverrides.Remove(port.Id); // system home replaces the button home
            }

            return;
        }

        if (port.Kind is KindAction or KindAxis)
        {
            if (slot is not { } target)
            {
                _actionOverrides.Remove(port.Id);
                return;
            }

            var slots = _actionOverrides.TryGetValue(port.Id, out var existing)
                ? existing
                : port.PackSlots.ToHashSet();
            if (!slots.Add(target))
            {
                slots.Remove(target);
            }

            if (slots.Count == 0)
            {
                if (port.Kind == KindAction)
                {
                    slots.Add(target); // an action must land somewhere: re-toggle keeps it
                }
                else
                {
                    _actionOverrides.Remove(port.Id); // a direction falls back to its device
                    return;
                }
            }

            _actionOverrides[port.Id] = slots;
            return;
        }

        if (slot is { } lightTarget)
        {
            // dropping a lamp on its OWN current home detaches it (dead LED);
            // any other button re-homes it
            var current = EffectiveSlots(port);
            if (current.Count == 1 && current.Contains(lightTarget) && !_lightDetached.Contains(port.Id))
            {
                _lightDetached.Add(port.Id);
                _lightOverrides.Remove(port.Id);
            }
            else
            {
                _lightDetached.Remove(port.Id);
                _lightOverrides[port.Id] = lightTarget;
            }

            _lightTargetOverrides.Remove(port.Id); // button home replaces the system home
        }
        else
        {
            _lightOverrides.Remove(port.Id);
            _lightTargetOverrides.Remove(port.Id);
            _lightDetached.Remove(port.Id);
        }
    }

    private int? NearestSlot(Point at)
    {
        int? best = null;
        var bestDistance = SnapRadius;
        foreach (var (slot, center) in _buttonCenters)
        {
            var distance = (center - at).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = slot;
            }
        }

        return best;
    }

    private System.Windows.Controls.Primitives.Popup? _palettePopup;

    /// <summary>
    /// Floating swatch palette anchored at the lamp's color dot: a dark card with
    /// one round swatch per firmware color, the active one ringed in accent, and
    /// a dashed swatch to return to the pack color. Pure presentation - the
    /// override logic is unchanged.
    /// </summary>
    private void OpenPalette(Port port, FrameworkElement anchor)
    {
        _palettePopup?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false);

        var active = _colorOverrides.TryGetValue(port.Id, out var current) ? current : port.PackColor ?? "";
        var grid = new WrapPanel { Width = 5 * 30, Margin = new Thickness(8) };

        void Close()
        {
            if (_palettePopup is { } popup)
            {
                popup.IsOpen = false;
            }
        }

        FrameworkElement Swatch(Brush fill, string tooltip, bool isActive, Action onPick, bool dashed = false)
        {
            var dot = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = fill,
                Stroke = new SolidColorBrush(isActive ? AccentColor : Color.FromRgb(0x3A, 0x3A, 0x52)),
                StrokeThickness = isActive ? 2.5 : 1,
                StrokeDashArray = dashed ? new DoubleCollection { 2, 2 } : null
            };
            var host = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = dot
            };
            host.MouseEnter += (_, _) => dot.StrokeThickness = 2.5;
            host.MouseLeave += (_, _) => dot.StrokeThickness = isActive ? 2.5 : 1;
            host.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Close();
                onPick();
                Render();
                Changed?.Invoke();
            };
            return host;
        }

        grid.Children.Add(Swatch(
            new SolidColorBrush(PanelColors.Resolve(port.PackColor ?? "GRAY")) { Opacity = 0.55 },
            L.T($"Couleur d'origine ({port.PackColor ?? "?"})", $"Original color ({port.PackColor ?? "?"})"),
            isActive: !_colorOverrides.ContainsKey(port.Id),
            onPick: () => _colorOverrides.Remove(port.Id),
            dashed: true));
        foreach (var color in Palette)
        {
            var name = color;
            grid.Children.Add(Swatch(
                new SolidColorBrush(PanelColors.Resolve(name)),
                name,
                isActive: name.Equals(active, StringComparison.OrdinalIgnoreCase),
                onPick: () => _colorOverrides[port.Id] = name));
        }

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid
        };

        _palettePopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 4,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            Child = card,
            IsOpen = true
        };
    }
}
