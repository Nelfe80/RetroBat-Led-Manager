using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LedManager.Setup.Controls;
using LedManager.Setup.Localization;
using LedManager.Setup.Serial;
using LedManager.Setup.VirtualPanel;

namespace LedManager.Setup.Views;

/// <summary>
/// Hardware setup wizard: prepare (stop LedManager + detect Pico) → panel test →
/// color channel test (R/G/B wire order, auto-fixed) → wiring test (light each
/// slot, the user clicks the button that lit up; mismatches are auto-fixed by
/// rewriting [GPIO:P1]) → save the validated configuration. Drives the Pico
/// through PicoCommandSender so the firmware init/GPIO profile is reused as-is.
/// </summary>
public sealed class WizardView : UserControl, IDisposable
{
    private enum Step { Prepare, PanelTest, ColorTest, WiringTest, Done }

    private readonly HardwareDescription _hardware;
    private readonly string _pluginRoot;
    private readonly PanelSurface _panel = new() { Interactive = true };

    private readonly TextBlock _title;
    private readonly TextBlock _body;
    private readonly Button _primary;
    private readonly Button _secondary;
    private readonly Button _back;
    private readonly TextBlock _status;
    private readonly StackPanel _choices;

    private Step _step = Step.Prepare;
    private PicoSenderHost? _sender;
    private PicoDetectionResult? _detection;

    // Wiring test state. An item is either a numbered slot or a named target (START/SELECT).
    private sealed record WiringItem(int? Slot, string? Target)
    {
        public string Label => Slot.HasValue ? $"B{Slot}" : Target!;
        public string Light(string color) => Slot.HasValue ? $"SLOT {Slot} {color}" : $"SET {Target} {color}";
        public bool Matches(int? slot, string? target)
            => (Slot.HasValue && slot == Slot) || (Target is not null && string.Equals(Target, target, StringComparison.OrdinalIgnoreCase));
    }

    private List<WiringItem> _wiringItems = new();
    private int _wiringIndex;
    private readonly Dictionary<WiringItem, WiringItem?> _wiringMap = new();

    // Color channel test state: one driven channel at a time, user reports the color seen.
    private static readonly (string Command, string Name, Color Expected)[] ColorChannels =
    {
        ("ALLPCT 100 0 0", "R", Color.FromRgb(0xE8, 0x30, 0x30)),
        ("ALLPCT 0 100 0", "G", Color.FromRgb(0x30, 0xE8, 0x50)),
        ("ALLPCT 0 0 100", "B", Color.FromRgb(0x30, 0x60, 0xE8))
    };

    private int _colorChannel;
    private readonly List<string> _colorSeen = new();

    public WizardView(HardwareDescription hardware, PanelLayoutDefinition layout)
    {
        _hardware = hardware;
        _pluginRoot = HardwareDescription.FindPluginRoot() ?? Directory.GetCurrentDirectory();
        _panel.Build(layout, hardware.ButtonCount, hardware.HasStart, hardware.HasSelect);
        _panel.SlotClicked += slot => OnWiringClick(slot, null);
        _panel.TargetClicked += target => OnWiringClick(null, target);

        _title = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Text(0xE8, 0xE8, 0xF0), TextWrapping = TextWrapping.Wrap };
        _body = new TextBlock { Margin = new Thickness(0, 12, 0, 0), FontSize = 13, Foreground = Text(0xB8, 0xB8, 0xC6), TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        _status = new TextBlock { Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = Text(0x8A, 0x8A, 0x9A), TextWrapping = TextWrapping.Wrap };
        _choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        _primary = new Button { Content = L.T("Commencer", "Start"), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 20, 8, 0), MinWidth = 130 };
        _secondary = new Button { Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 20, 8, 0), MinWidth = 130, Visibility = Visibility.Collapsed };
        _back = new Button { Content = L.T("Précédent", "Back"), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 20, 0, 0), MinWidth = 100, IsEnabled = false };
        _primary.Click += (_, _) => OnPrimary();
        _secondary.Click += (_, _) => OnSecondary();
        _back.Click += (_, _) => OnBack();

        var rightStack = new StackPanel { Margin = new Thickness(24, 8, 8, 8) };
        rightStack.Children.Add(_title);
        rightStack.Children.Add(_body);
        rightStack.Children.Add(_status);
        rightStack.Children.Add(_choices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_primary);
        buttons.Children.Add(_secondary);
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
        _secondary.Visibility = Visibility.Collapsed;
        _choices.Visibility = Visibility.Collapsed;

        switch (_step)
        {
            case Step.Prepare:
                _title.Text = L.T("1. Préparation", "1. Preparation");
                _body.Text = L.T(
                    "L'assistant va prendre le contrôle direct de votre Pico pour tester le câblage. "
                    + "Pour cela, LedManager doit être arrêté (il occupe le port du Pico).\n\n"
                    + "Branchez votre Pico en USB, puis cliquez sur « Détecter le Pico ».",
                    "The assistant takes direct control of your Pico to test the wiring. "
                    + "LedManager must be stopped for that (it holds the Pico's port).\n\n"
                    + "Plug your Pico in over USB, then click \"Detect the Pico\".");
                _primary.Content = L.T("Détecter le Pico", "Detect the Pico");
                _status.Text = LedManagerProcess.IsRunning()
                    ? L.T("⚠ LedManager est en cours d'exécution — il sera arrêté à la détection.",
                        "⚠ LedManager is running — it will be stopped at detection.")
                    : L.T("LedManager n'est pas en cours d'exécution. ✓", "LedManager is not running. ✓");
                _panel.ClearAll();
                break;

            case Step.PanelTest:
                _title.Text = L.T("2. Test du panneau", "2. Panel test");
                _body.Text = L.T(
                    "Vos boutons devraient tous s'allumer en blanc sur le vrai panneau. "
                    + "Utilisez les boutons ci-dessous pour vérifier que chaque LED répond.\n\n"
                    + "Si rien ne s'allume : vérifiez l'alimentation (câble USB data) et le firmware.",
                    "Your buttons should all light up white on the real panel. "
                    + "Use the buttons below to check that every LED answers.\n\n"
                    + "If nothing lights up: check the power (USB data cable) and the firmware.");
                _primary.Content = L.T("Le panneau s'allume →", "The panel lights up →");
                break;

            case Step.ColorTest:
                _title.Text = L.T("3. Test des couleurs", "3. Color test");
                _body.Text = L.T(
                    "L'assistant vérifie l'ordre des fils R, G, B. Le panneau virtuel montre la couleur "
                    + "attendue : si le vrai panneau affiche autre chose, indiquez la couleur réellement vue.\n\n"
                    + "L'assistant corrigera alors l'ordre des canaux dans la configuration — sans ressouder.",
                    "The assistant checks the R, G, B wire order. The virtual panel shows the expected "
                    + "color: if the real panel shows something else, report the color you actually see.\n\n"
                    + "The assistant will then fix the channel order in the configuration — no re-soldering.");
                _primary.Content = L.T("Passer ce test", "Skip this test");
                StartColorTest();
                break;

            case Step.WiringTest:
                _title.Text = L.T("4. Test du câblage", "4. Wiring test");
                _body.Text = L.T(
                    "Un bouton va s'allumer sur votre panneau physique, un par un. "
                    + "À chaque fois, cliquez ici sur le bouton virtuel qui correspond au bouton allumé en vrai.\n\n"
                    + "Cela permet à l'assistant de vérifier — et corriger — la correspondance entre les GPIO et vos boutons.",
                    "One button lights up on your physical panel, one at a time. "
                    + "Each time, click here the virtual button matching the one really lit.\n\n"
                    + "This lets the assistant verify — and fix — the mapping between GPIOs and your buttons.");
                _primary.Content = L.T("Passer", "Skip");
                StartWiringTest();
                break;

            case Step.Done:
                _title.Text = L.T("✓ Terminé", "✓ Done");
                _body.Text = BuildWiringSummary();
                _primary.Content = L.T("Fermer l'assistant", "Close the assistant");
                _panel.ClearAll();
                RenderDoneActions();
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
                _step = Step.ColorTest;
                RenderStep();
                break;

            case Step.ColorTest:
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
            case Step.ColorTest:
                _step = Step.PanelTest;
                _sender?.Send("ALL WHITE");
                _panel.SetAll(Color.FromRgb(0xF0, 0xF0, 0xF0));
                break;
            case Step.WiringTest:
                _step = Step.ColorTest;
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
        _status.Text = L.T("Arrêt de LedManager…", "Stopping LedManager…");
        await Task.Run(LedManagerProcess.StopAll);

        _status.Text = L.T("Recherche du Pico sur les ports série…", "Scanning serial ports for the Pico…");
        _detection = await PicoDetector.DetectAsync(_hardware.SerialPort);
        _status.Text = _detection.Message;

        if (!_detection.Found)
        {
            // firmware missing or Pico blank: offer the one-button installer
            _secondary.Content = L.T("Installer le firmware", "Install the firmware");
            _secondary.Visibility = Visibility.Visible;
            _secondary.IsEnabled = true;
            _primary.IsEnabled = true;
            return;
        }

        _secondary.Visibility = Visibility.Collapsed;

        var started = await StartSenderAsync();
        _primary.IsEnabled = true;
        if (!started)
        {
            return;
        }

        _sender!.Send("ALL WHITE");
        _panel.SetAll(Color.FromRgb(0xF0, 0xF0, 0xF0));

        _step = Step.PanelTest;
        RenderStep();
    }

    /// <summary>Starts (or restarts) PicoCommandSender and waits for its READY.</summary>
    private async Task<bool> StartSenderAsync()
    {
        StopSender();
        _sender = PicoSenderHost.Start(_pluginRoot);
        if (_sender is null)
        {
            _status.Text = L.T("PicoCommandSender.exe introuvable à la racine du plugin.",
                "PicoCommandSender.exe not found at the plugin root.");
            return false;
        }

        // Wait for the sender's READY (firmware GPIO profile initialized) rather than
        // a blind delay; PostInitDelayMs in the ini can be many seconds. The 30 s cap
        // covers the largest configured delay while never hanging the UI forever.
        _status.Text = L.T("Initialisation du Pico (profil GPIO)…", "Initializing the Pico (GPIO profile)…");
        await _sender.WaitForReadyAsync(TimeSpan.FromSeconds(30));

        if (!_sender.IsAlive)
        {
            _status.Text = L.T("Le pilote PicoCommandSender s'est arrêté. Vérifiez le port COM et le firmware.",
                "The PicoCommandSender driver stopped. Check the COM port and the firmware.");
            return false;
        }

        return true;
    }

    // ----- Color channel test (P1.c) -----

    private void StartColorTest()
    {
        _colorChannel = 0;
        _colorSeen.Clear();
        BuildColorChoices();
        LightCurrentColorChannel();
    }

    private void BuildColorChoices()
    {
        _choices.Children.Clear();
        foreach (var (label, name, color) in new[]
                 {
                     (L.T("ROUGE", "RED"), "R", ColorChannels[0].Expected),
                     (L.T("VERT", "GREEN"), "G", ColorChannels[1].Expected),
                     (L.T("BLEU", "BLUE"), "B", ColorChannels[2].Expected)
                 })
        {
            var choice = new Button
            {
                Content = label,
                Tag = name,
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(color)
            };
            choice.Click += (_, _) => OnColorAnswer((string)choice.Tag);
            _choices.Children.Add(choice);
        }

        _choices.Visibility = Visibility.Visible;
    }

    private void LightCurrentColorChannel()
    {
        var (command, name, expected) = ColorChannels[_colorChannel];
        _sender?.Send("CLEAR");
        _sender?.Send(command);
        _panel.SetAll(expected);
        var expectedName = name == "R" ? L.T("ROUGE", "RED") : name == "G" ? L.T("VERT", "GREEN") : L.T("BLEU", "BLUE");
        _status.Text = L.T($"Canal {_colorChannel + 1}/3 : le panneau virtuel montre du {expectedName}. "
                + "Quelle couleur voyez-vous sur le VRAI panneau ?",
            $"Channel {_colorChannel + 1}/3: the virtual panel shows {expectedName}. "
                + "Which color do you see on the REAL panel?");
    }

    private void OnColorAnswer(string seen)
    {
        if (_step != Step.ColorTest)
        {
            return;
        }

        _colorSeen.Add(seen);
        _colorChannel++;
        if (_colorChannel < ColorChannels.Length)
        {
            LightCurrentColorChannel();
            return;
        }

        EvaluateColorTest();
    }

    private void EvaluateColorTest()
    {
        _choices.Visibility = Visibility.Collapsed;
        _sender?.Send("CLEAR");
        _panel.ClearAll();

        if (_colorSeen.Count == 3 && _colorSeen[0] == "R" && _colorSeen[1] == "G" && _colorSeen[2] == "B")
        {
            _status.Text = L.T("Ordre des canaux R,G,B correct. ✓", "R,G,B channel order correct. ✓");
            _step = Step.WiringTest;
            RenderStep();
            return;
        }

        _status.Text = L.T($"Les couleurs vues ({string.Join(", ", _colorSeen)}) ne suivent pas l'ordre R, G, B : "
                + "les fils des canaux sont inversés quelque part.",
            $"The colors seen ({string.Join(", ", _colorSeen)}) do not follow the R, G, B order: "
                + "the channel wires are crossed somewhere.");
        _secondary.Content = L.T("Corriger l'ordre des canaux", "Fix the channel order");
        _secondary.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// One-button firmware install. Blank Pico (BOOTSEL drive visible): MicroPython
    /// UF2 first — copied automatically if fw\*.uf2 exists, otherwise guided download.
    /// Then the panel firmware goes over the MicroPython raw REPL, and detection re-runs.
    /// </summary>
    private async Task InstallFirmwareAsync()
    {
        _secondary.IsEnabled = false;
        _primary.IsEnabled = false;

        if (FirmwareInstaller.FindBootselDrive() is { } drive)
        {
            if (FirmwareInstaller.FindLocalUf2(_pluginRoot) is { } uf2)
            {
                _status.Text = L.T($"Pico en mode BOOTSEL : copie de {System.IO.Path.GetFileName(uf2)}…",
                    $"Pico in BOOTSEL mode: copying {System.IO.Path.GetFileName(uf2)}…");
                var copy = FirmwareInstaller.CopyUf2ToBootsel(uf2, drive);
                if (!copy.Success)
                {
                    _status.Text = L.T("Copie du UF2 impossible : ", "Could not copy the UF2: ") + copy.Message;
                    _secondary.IsEnabled = true;
                    _primary.IsEnabled = true;
                    return;
                }

                _status.Text = L.T("MicroPython copié, le Pico redémarre…", "MicroPython copied, the Pico is rebooting…");
                await Task.Delay(9000);
            }
            else
            {
                _status.Text = L.T(
                    "Pico en mode BOOTSEL : il lui faut d'abord MicroPython. Téléchargez le fichier .uf2 officiel "
                    + "(la page vient de s'ouvrir), déposez-le sur le lecteur RPI-RP2, puis recliquez « Installer le firmware ».",
                    "Pico in BOOTSEL mode: it needs MicroPython first. Download the official .uf2 file "
                    + "(the page just opened), drop it on the RPI-RP2 drive, then click \"Install the firmware\" again.");
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://micropython.org/download/RPI_PICO/") { UseShellExecute = true });
                }
                catch
                {
                    // browser launch is best effort; the URL is in the message
                }

                _secondary.IsEnabled = true;
                _primary.IsEnabled = true;
                return;
            }
        }

        await Task.Run(LedManagerProcess.StopAll);
        var progress = new Progress<string>(message => _status.Text = message);
        var result = await FirmwareInstaller.InstallAsync(_pluginRoot, _hardware.SerialPort, progress);
        _status.Text = result.Message;
        _secondary.IsEnabled = true;
        _primary.IsEnabled = true;

        if (result.Success)
        {
            _secondary.Visibility = Visibility.Collapsed;
            await Task.Delay(2500); // let the fresh firmware boot before probing
            await PrepareAsync();
        }
    }

    private async Task FixColorChannelsAsync()
    {
        _secondary.IsEnabled = false;
        var result = ColorChannelFixer.Apply(_pluginRoot, "P1", _colorSeen);
        _status.Text = result.Message;
        if (!result.Success)
        {
            _secondary.IsEnabled = true;
            return;
        }

        // the daemon reads [GPIO:P1] at startup: restart it, then re-run the test to confirm
        _secondary.Visibility = Visibility.Collapsed;
        _secondary.IsEnabled = true;
        if (await StartSenderAsync())
        {
            _status.Text = result.Message + L.T("\nRefaites le test pour confirmer.", "\nRe-run the test to confirm.");
            StartColorTest();
        }
    }

    // ----- Wiring test (P1.a/P1.b) -----

    private static readonly Color FeedbackColor = Color.FromRgb(0x20, 0xE8, 0xE8); // cyan

    private void StartWiringTest()
    {
        _wiringMap.Clear();

        // Numbered buttons first (top-to-bottom), then START and SELECT.
        var items = _panel.Slots.OrderBy(s => s).Select(s => new WiringItem(s, null)).ToList();
        if (_panel.TargetNames.Contains("SELECT")) items.Add(new WiringItem(null, "SELECT"));
        if (_panel.TargetNames.Contains("START")) items.Add(new WiringItem(null, "START"));
        _wiringItems = items;

        _wiringIndex = 0;
        LightCurrentWiringItem();
    }

    private void LightCurrentWiringItem()
    {
        if (_wiringIndex >= _wiringItems.Count)
        {
            _sender?.Send("CLEAR");
            _step = Step.Done;
            RenderStep();
            return;
        }

        // Turn the PHYSICAL panel off first (CLEAR), otherwise a single lit button is
        // invisible among the all-white panel left by the panel test. Then light just
        // this item in a vivid color so it stands out.
        var item = _wiringItems[_wiringIndex];
        _sender?.Send("CLEAR");
        _sender?.Send(item.Light("GREEN"));

        _panel.ClearAll();
        _status.Text = L.T($"Élément {_wiringIndex + 1}/{_wiringItems.Count} : un bouton s'allume en VERT sur le panneau. "
                + "Cliquez le bouton virtuel qui correspond.",
            $"Item {_wiringIndex + 1}/{_wiringItems.Count}: one button lights up GREEN on the panel. "
                + "Click the matching virtual button.");
    }

    private async void OnWiringClick(int? clickedSlot, string? clickedTarget)
    {
        if (_step != Step.WiringTest || _wiringIndex >= _wiringItems.Count)
        {
            return;
        }

        var lit = _wiringItems[_wiringIndex];
        var clicked = clickedSlot.HasValue ? new WiringItem(clickedSlot, null) : new WiringItem(null, clickedTarget);
        _wiringMap[lit] = clicked;

        // Feedback: blink the clicked button on BOTH panels so the user sees the click
        // registered — cyan on the virtual panel, a brief cyan pulse on the real one.
        if (clickedSlot.HasValue)
        {
            _panel.Flash(clickedSlot.Value.ToString(), FeedbackColor, 260);
        }
        else if (clickedTarget is not null)
        {
            _panel.Flash(clickedTarget, FeedbackColor, 260);
        }

        _sender?.Send(new WiringItem(clickedSlot, clickedTarget).Light("CYAN"));
        await Task.Delay(260);

        _wiringIndex++;
        LightCurrentWiringItem();
    }

    // ----- Done step: fix mapping (P1.b) or save config (P1.d) -----

    private List<KeyValuePair<WiringItem, WiringItem?>> WiringMismatches()
        => _wiringMap.Where(kv => kv.Value is null || !kv.Key.Matches(kv.Value!.Slot, kv.Value.Target)).ToList();

    private string BuildWiringSummary()
    {
        if (_wiringMap.Count == 0)
        {
            return L.T("Test du câblage passé. Vous pourrez le relancer à tout moment.",
                "Wiring test skipped. You can re-run it anytime.");
        }

        var mismatches = WiringMismatches();
        if (mismatches.Count == 0)
        {
            return L.T($"Câblage vérifié : les {_wiringMap.Count} éléments correspondent à la disposition attendue. "
                    + "Aucune correction nécessaire.",
                $"Wiring verified: all {_wiringMap.Count} items match the expected arrangement. "
                    + "No fix needed.");
        }

        var lines = string.Join("\n", mismatches.Select(kv => L.T(
            $"   • {kv.Key.Label} allumé → cliqué {kv.Value?.Label ?? "?"}",
            $"   • {kv.Key.Label} lit → clicked {kv.Value?.Label ?? "?"}")));
        return L.T($"{mismatches.Count} différence(s) détectée(s) entre le câblage et la disposition :\n{lines}\n\n"
                + "« Corriger automatiquement » réécrit le câblage logiciel ([GPIO:P1]) pour que chaque bouton "
                + "réponde à sa place — sans ressouder. Le test se relancera pour confirmer.",
            $"{mismatches.Count} difference(s) detected between the wiring and the arrangement:\n{lines}\n\n"
                + "\"Fix automatically\" rewrites the software wiring ([GPIO:P1]) so every button "
                + "answers at its place — no re-soldering. The test will re-run to confirm.");
    }

    private void RenderDoneActions()
    {
        if (_wiringMap.Count > 0 && WiringMismatches().Count > 0)
        {
            _secondary.Content = L.T("Corriger automatiquement", "Fix automatically");
        }
        else
        {
            _secondary.Content = L.T("Enregistrer la configuration", "Save the configuration");
        }

        _secondary.Visibility = Visibility.Visible;
        _status.Text = "";
    }

    private async void OnSecondary()
    {
        switch (_step)
        {
            case Step.Prepare:
                await InstallFirmwareAsync();
                break;

            case Step.ColorTest:
                await FixColorChannelsAsync();
                break;

            case Step.Done when _wiringMap.Count > 0 && WiringMismatches().Count > 0:
                await FixWiringMappingAsync();
                break;

            case Step.Done:
                SaveHardwareConfig();
                break;
        }
    }

    private async Task FixWiringMappingAsync()
    {
        _secondary.IsEnabled = false;
        var map = _wiringMap
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key.Label, kv => kv.Value!.Label, StringComparer.OrdinalIgnoreCase);
        var result = GpioMappingFixer.Apply(_pluginRoot, "P1", map);
        _status.Text = result.Message;
        _secondary.IsEnabled = true;
        if (!result.Success)
        {
            return;
        }

        // the daemon reads [GPIO:P1] at startup: restart it, then re-run the wiring
        // test so the user confirms every button now answers at its place
        _secondary.Visibility = Visibility.Collapsed;
        if (await StartSenderAsync())
        {
            _step = Step.WiringTest;
            RenderStep();
        }
    }

    private void SaveHardwareConfig()
    {
        var result = HardwareConfigWriter.Apply(
            _pluginRoot,
            "P1",
            _detection?.PortName,
            _sender?.MeasuredFirmwareInitMs,
            _hardware.ButtonCount,
            _hardware.HasStart,
            _hardware.HasSelect);
        _status.Text = result.Message + (result.Success
            ? L.T("\nLedManager utilisera ces réglages au prochain démarrage.", "\nLedManager will use these settings on its next start.")
            : "");
        if (result.Success)
        {
            _secondary.IsEnabled = false;
        }
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
