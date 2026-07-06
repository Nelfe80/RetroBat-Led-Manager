using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LedManager.Setup.Controls;
using LedManager.Setup.Serial;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Views;

/// <summary>
/// Hardware setup wizard: prepare (stop LedManager + detect Pico) → panel test →
/// wiring test (light each slot, the user clicks the button that lit up). Drives the
/// Pico through PicoCommandSender so the firmware init/GPIO profile is reused as-is.
/// </summary>
public sealed class WizardView : UserControl, IDisposable
{
    private enum Step { Prepare, PanelTest, WiringTest, Done }

    private readonly HardwareDescription _hardware;
    private readonly string _pluginRoot;
    private readonly PanelSurface _panel = new() { Interactive = true };

    private readonly TextBlock _title;
    private readonly TextBlock _body;
    private readonly Button _primary;
    private readonly Button _back;
    private readonly TextBlock _status;

    private Step _step = Step.Prepare;
    private PicoSenderHost? _sender;
    private PicoDetectionResult? _detection;

    // Wiring test state
    private List<int> _wiringOrder = new();
    private int _wiringIndex;
    private readonly Dictionary<int, int> _wiringMap = new(); // lit slot -> clicked slot

    public WizardView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _hardware = hardware;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? Directory.GetCurrentDirectory();
        _panel.Build(layout, hardware.ButtonCount, hardware.HasStart, hardware.HasSelect);
        _panel.SlotClicked += OnSlotClicked;

        _title = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Text(0xE8, 0xE8, 0xF0), TextWrapping = TextWrapping.Wrap };
        _body = new TextBlock { Margin = new Thickness(0, 12, 0, 0), FontSize = 13, Foreground = Text(0xB8, 0xB8, 0xC6), TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        _status = new TextBlock { Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = Text(0x8A, 0x8A, 0x9A), TextWrapping = TextWrapping.Wrap };
        _primary = new Button { Content = "Commencer", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 20, 8, 0), MinWidth = 130 };
        _back = new Button { Content = "Précédent", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 20, 0, 0), MinWidth = 100, IsEnabled = false };
        _primary.Click += (_, _) => OnPrimary();
        _back.Click += (_, _) => OnBack();

        var rightStack = new StackPanel { Margin = new Thickness(24, 8, 8, 8) };
        rightStack.Children.Add(_title);
        rightStack.Children.Add(_body);
        rightStack.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_primary);
        buttons.Children.Add(_back);
        rightStack.Children.Add(buttons);

        var panelBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x2A)), CornerRadius = new CornerRadius(12), Padding = new Thickness(24), Child = _panel };

        var grid = new Grid { Margin = new Thickness(20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(panelBorder, 0);
        Grid.SetColumn(rightStack, 1);
        grid.Children.Add(panelBorder);
        grid.Children.Add(rightStack);
        Content = grid;

        RenderStep();
    }

    private void RenderStep()
    {
        _back.IsEnabled = _step != Step.Prepare;
        switch (_step)
        {
            case Step.Prepare:
                _title.Text = "1. Préparation";
                _body.Text = "L'assistant va prendre le contrôle direct de votre Pico pour tester le câblage. "
                    + "Pour cela, LedManager doit être arrêté (il occupe le port du Pico).\n\n"
                    + "Branchez votre Pico en USB, puis cliquez sur « Détecter le Pico ».";
                _primary.Content = "Détecter le Pico";
                _status.Text = LedManagerProcess.IsRunning()
                    ? "⚠ LedManager est en cours d'exécution — il sera arrêté à la détection."
                    : "LedManager n'est pas en cours d'exécution. ✓";
                _panel.ClearAll();
                break;

            case Step.PanelTest:
                _title.Text = "2. Test du panneau";
                _body.Text = "Vos boutons devraient tous s'allumer en blanc sur le vrai panneau. "
                    + "Utilisez les boutons ci-dessous pour vérifier que chaque LED répond.\n\n"
                    + "Si rien ne s'allume : vérifiez l'alimentation (câble USB data) et le firmware.";
                _primary.Content = "Le panneau s'allume →";
                break;

            case Step.WiringTest:
                _title.Text = "3. Test du câblage";
                _body.Text = "Un bouton va s'allumer sur votre panneau physique, un par un. "
                    + "À chaque fois, cliquez ici sur le bouton virtuel qui correspond au bouton allumé en vrai.\n\n"
                    + "Cela permet à l'assistant de vérifier — et corriger — la correspondance entre les GPIO et vos boutons.";
                _primary.Content = "Passer";
                StartWiringTest();
                break;

            case Step.Done:
                _title.Text = "✓ Terminé";
                _body.Text = BuildWiringSummary();
                _primary.Content = "Fermer l'assistant";
                _panel.ClearAll();
                break;
        }
    }

    private async void OnPrimary()
    {
        switch (_step)
        {
            case Step.Prepare:
                await PrepareAsync();
                break;

            case Step.PanelTest:
                _step = Step.WiringTest;
                RenderStep();
                break;

            case Step.WiringTest:
                _step = Step.Done;
                RenderStep();
                break;

            case Step.Done:
                Window.GetWindow(this)?.Close();
                break;
        }
    }

    private void OnBack()
    {
        switch (_step)
        {
            case Step.PanelTest:
                _step = Step.Prepare;
                StopSender();
                break;
            case Step.WiringTest:
                _step = Step.PanelTest;
                break;
            case Step.Done:
                _step = Step.WiringTest;
                break;
        }

        RenderStep();
    }

    private async Task PrepareAsync()
    {
        _primary.IsEnabled = false;
        _status.Text = "Arrêt de LedManager…";
        await Task.Run(LedManagerProcess.StopAll);

        _status.Text = "Recherche du Pico sur les ports série…";
        _detection = await PicoDetector.DetectAsync(_hardware.SerialPort);
        _status.Text = _detection.Message;

        if (!_detection.Found)
        {
            _primary.IsEnabled = true;
            return;
        }

        _sender = PicoSenderHost.Start(_pluginRoot);
        if (_sender is null)
        {
            _status.Text = "PicoCommandSender.exe introuvable à la racine du plugin.";
            _primary.IsEnabled = true;
            return;
        }

        // Give the sender time to initialize the firmware GPIO profile, then light up.
        await Task.Delay(2500);
        _sender.Send("ALL WHITE");
        _panel.SetAll(Color.FromRgb(0xF0, 0xF0, 0xF0));

        _primary.IsEnabled = true;
        _step = Step.PanelTest;
        RenderStep();
    }

    private void StartWiringTest()
    {
        _wiringMap.Clear();
        _wiringOrder = _panel.Slots.OrderBy(s => s).ToList();
        _wiringIndex = 0;
        LightCurrentWiringSlot();
    }

    private void LightCurrentWiringSlot()
    {
        if (_wiringIndex >= _wiringOrder.Count)
        {
            _step = Step.Done;
            RenderStep();
            return;
        }

        _panel.SetAll(PanelColors.Off);
        var slot = _wiringOrder[_wiringIndex];
        _sender?.Send($"SLOT {slot} WHITE");
        _status.Text = $"Bouton {_wiringIndex + 1}/{_wiringOrder.Count} allumé sur le panneau. Cliquez le bouton virtuel correspondant.";
    }

    private void OnSlotClicked(int clickedSlot)
    {
        if (_step != Step.WiringTest || _wiringIndex >= _wiringOrder.Count)
        {
            return;
        }

        var litSlot = _wiringOrder[_wiringIndex];
        _wiringMap[litSlot] = clickedSlot;
        _wiringIndex++;
        LightCurrentWiringSlot();
    }

    private string BuildWiringSummary()
    {
        if (_wiringMap.Count == 0)
        {
            return "Test du câblage passé. Vous pourrez le relancer à tout moment.";
        }

        var mismatches = _wiringMap.Where(kv => kv.Key != kv.Value).ToList();
        if (mismatches.Count == 0)
        {
            return $"Câblage vérifié : les {_wiringMap.Count} boutons correspondent à la disposition attendue. "
                + "Aucune correction nécessaire.";
        }

        var lines = string.Join("\n", mismatches.Select(kv => $"   • GPIO du bouton B{kv.Key} → allume en réalité B{kv.Value}"));
        return $"{mismatches.Count} différence(s) détectée(s) entre le câblage et la disposition :\n{lines}\n\n"
            + "La correction automatique du mapping GPIO arrive dans une prochaine version — "
            + "en attendant, ces informations vous aident à ajuster le câblage ou le fichier PicoCommandSender.ini.";
    }

    private void StopSender()
    {
        _sender?.Dispose();
        _sender = null;
    }

    private static SolidColorBrush Text(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    public void Dispose()
    {
        StopSender();
    }
}
