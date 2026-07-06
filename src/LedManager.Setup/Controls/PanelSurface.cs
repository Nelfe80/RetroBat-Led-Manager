using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Controls;

/// <summary>
/// The reusable virtual panel: SELECT/START at the top-left, the RetroBat-standard
/// button rows below, plus a matrix line. Shared by the live monitor (mirror mode)
/// and the wizard (interactive mode, where clicking a button raises SlotClicked).
/// </summary>
public sealed class PanelSurface : UserControl
{
    private readonly StackPanel _systemButtonsRow;
    private readonly StackPanel _buttonRowsHost;
    private readonly TextBlock _matrixText;
    private readonly Dictionary<int, ButtonVisual> _slots = new();
    private readonly Dictionary<string, ButtonVisual> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when the user clicks a numbered button (interactive mode). Arg = slot.</summary>
    public event Action<int>? SlotClicked;

    /// <summary>Raised when the user clicks a named target (START/SELECT).</summary>
    public event Action<string>? TargetClicked;

    public bool Interactive { get; set; }

    public IReadOnlyCollection<int> Slots => _slots.Keys;

    public IReadOnlyCollection<string> TargetNames => _targets.Keys;

    public PanelSurface()
    {
        _systemButtonsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 4) };
        _buttonRowsHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _matrixText = new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xE8, 0x50))
        };

        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(_systemButtonsRow);
        root.Children.Add(_buttonRowsHost);
        root.Children.Add(_matrixText);
        Content = root;
    }

    public void Build(PanelLayoutDefinition layout, int buttonCount, bool hasStart, bool hasSelect)
    {
        _systemButtonsRow.Children.Clear();
        _buttonRowsHost.Children.Clear();
        _slots.Clear();
        _targets.Clear();

        if (hasSelect)
        {
            AddTarget("SELECT", 52);
        }

        if (hasStart)
        {
            AddTarget("START", 52);
        }

        foreach (var row in layout.RowsFor(buttonCount))
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var slot in row)
            {
                var slotNumber = slot;
                var visual = new ButtonVisual($"B{slotNumber}", 84, layout.RetrobatLabel(slotNumber));
                visual.Root.MouseLeftButtonUp += (_, _) => { if (Interactive) SlotClicked?.Invoke(slotNumber); };
                visual.Root.Cursor = Cursors.Hand;
                _slots[slotNumber] = visual;
                rowPanel.Children.Add(visual.Root);
            }

            _buttonRowsHost.Children.Add(rowPanel);
        }
    }

    private void AddTarget(string name, double size)
    {
        var visual = new ButtonVisual(name, size);
        visual.Root.MouseLeftButtonUp += (_, _) => { if (Interactive) TargetClicked?.Invoke(name); };
        visual.Root.Cursor = Cursors.Hand;
        _targets[name] = visual;
        _systemButtonsRow.Children.Add(visual.Root);
    }

    public void SetSlot(int slot, Color color)
    {
        if (_slots.TryGetValue(slot, out var visual))
        {
            visual.SetColor(color);
        }
    }

    public void SetTarget(string target, Color color)
    {
        if (_targets.TryGetValue(target, out var visual))
        {
            visual.SetColor(color);
        }
        else if (target.StartsWith("B", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(target.AsSpan(1), out var slot))
        {
            SetSlot(slot, color);
        }
    }

    public void SetAll(Color color)
    {
        foreach (var visual in _slots.Values)
        {
            visual.SetColor(color);
        }
    }

    public void ClearAll()
    {
        SetAll(PanelColors.Off);
        foreach (var visual in _targets.Values)
        {
            visual.SetColor(PanelColors.Off);
        }

        _matrixText.Text = "";
    }

    public void SetMatrix(string text) => _matrixText.Text = text;

    public void Flash(string target, Color color, int durationMs)
    {
        ButtonVisual? visual = null;
        if (int.TryParse(target, out var slot))
        {
            _slots.TryGetValue(slot, out visual);
        }
        else
        {
            _targets.TryGetValue(target, out visual);
        }

        if (visual is null)
        {
            return;
        }

        var previous = visual.CurrentColor;
        visual.SetColor(color);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, durationMs)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            visual.SetColor(previous);
        };
        timer.Start();
    }
}
