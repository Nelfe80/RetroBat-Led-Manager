using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using LedManager.Core.Ini;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

var app = new PicoCommandSenderApp();
return await app.RunAsync(args);

internal sealed class PicoCommandSenderApp
{
    public async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "send";
        var iniPath = GetOption(args, "--ini") ?? "PicoCommandSender.ini";
        var senderId = GetOption(args, "--sender");
        var config = PicoSenderConfig.Load(iniPath, senderId);

        if (mode == "probe")
        {
            var found = await PicoSerial.AutoDetectAsync(config);
            Console.WriteLine(found ?? "NOT_FOUND");
            return found is null ? 2 : 0;
        }

        if (config.DryRun)
        {
            Console.WriteLine($"[dry-run] sender={config.SenderId} port={config.Port}");
        }

        var bridgeLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        await using var serial = await PicoSerial.OpenAsync(config, line => bridgeLines.Writer.TryWrite(line));

        if (mode == "daemon")
        {
            var initLock = new SemaphoreSlim(1, 1);
            var lastBridgeReinitTicks = 0L;
            var initialFirmwareReadyIgnored = false;
            const int MinBridgeReinitIntervalMs = 2500;

            async Task RunInitCommandsAsync(string reason)
            {
                await initLock.WaitAsync();
                try
                {
                    Console.WriteLine($"[init] sender={config.SenderId} reason={reason}");
                    foreach (var init in config.InitCommands)
                    {
                        serial.Send(init);
                        await Task.Delay(120);
                    }

                    if (config.PostInitDelayMs > 0)
                    {
                        await Task.Delay(config.PostInitDelayMs);
                    }

                    Console.WriteLine($"READY sender={config.SenderId} port={serial.PortName}");
                }
                finally
                {
                    initLock.Release();
                }
            }

            async Task SendWithRecoveryAsync(string command, bool resendAfterRecovery)
            {
                foreach (var translated in PicoCommandTranslator.Translate(command, config.CommandAdapter))
                {
                    await SendOneWithRecoveryAsync(translated, resendAfterRecovery);
                }
            }

            async Task SendOneWithRecoveryAsync(string command, bool resendAfterRecovery)
            {
                var reconnectNeeded = false;
                await initLock.WaitAsync();
                try
                {
                    serial.Send(command);
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"[bridge] {ex.Message}; reconnecting...");
                    reconnectNeeded = true;
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"[bridge] {ex.Message}; reconnecting...");
                    reconnectNeeded = true;
                }
                finally
                {
                    initLock.Release();
                }

                if (!reconnectNeeded)
                {
                    return;
                }

                await serial.ReconnectAsync();
                await RunInitCommandsAsync("reconnect");
                if (resendAfterRecovery)
                {
                    await SendOneWithRecoveryAsync(command, false);
                }
            }

            _ = Task.Run(async () =>
            {
                await foreach (var line in bridgeLines.Reader.ReadAllAsync())
                {
                    var reinitReason = GetReinitReason(line);
                    if (reinitReason is null)
                    {
                        continue;
                    }

                    if (string.Equals(reinitReason, "firmware-ready", StringComparison.OrdinalIgnoreCase) &&
                        !initialFirmwareReadyIgnored)
                    {
                        initialFirmwareReadyIgnored = true;
                        Console.Error.WriteLine($"[bridge] initial firmware-ready ignored for sender={config.SenderId}; startup init already handles profile");
                        continue;
                    }

                    var now = Environment.TickCount64;
                    var previous = Interlocked.Read(ref lastBridgeReinitTicks);
                    if (previous > 0 && now - previous < MinBridgeReinitIntervalMs)
                    {
                        Console.Error.WriteLine($"[bridge] {reinitReason} reinit suppressed for sender={config.SenderId}; previous={now - previous}ms ago");
                        continue;
                    }

                    Interlocked.Exchange(ref lastBridgeReinitTicks, now);
                    Console.Error.WriteLine($"[bridge] {reinitReason} detected for sender={config.SenderId}; reinitializing profile");
                    try
                    {
                        await RunInitCommandsAsync(reinitReason);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[bridge] firmware reinit failed: {ex.Message}");
                    }
                }
            });

            await RunInitCommandsAsync("startup");

            while (Console.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                await SendWithRecoveryAsync(line, resendAfterRecovery: true);
            }

            return 0;
        }

        var inline = RemainingCommand(args);
        if (string.IsNullOrWhiteSpace(inline))
        {
            Console.Error.WriteLine("Missing command. Usage: PicoCommandSender send --ini file.ini \"SET B1 RED\"");
            return 1;
        }

        foreach (var command in PicoCommandTranslator.Translate(inline, config.CommandAdapter))
        {
            serial.Send(command);
        }

        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string RemainingCommand(string[] args)
    {
        var skipNext = false;
        var parts = new List<string>();
        foreach (var arg in args.Skip(1))
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (arg.Equals("--ini", StringComparison.OrdinalIgnoreCase))
            {
                skipNext = true;
                continue;
            }

            parts.Add(arg);
        }

        return string.Join(' ', parts);
    }

    private static string? GetReinitReason(string line)
    {
        if (line.Contains("SERIAL REOPENED", StringComparison.OrdinalIgnoreCase))
        {
            return "bridge-reopen";
        }

        return line.Contains("READY DYNAMIC PANEL", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("READY DYNAMIC_PANEL", StringComparison.OrdinalIgnoreCase)
                ? "firmware-ready"
                : null;
    }
}

internal sealed class PicoSenderConfig
{
    public string SenderId { get; init; } = "PICO";
    public int Player { get; init; }
    public string Role { get; init; } = "player";
    public string Port { get; init; } = "auto";
    public int BaudRate { get; init; } = 115200;
    public int BootDelayMs { get; init; } = 800;
    public int PostInitDelayMs { get; init; } = 1200;
    public int ProbeTimeoutMs { get; init; } = 900;
    public int WriteTimeoutMs { get; init; } = 15000;
    public string LineEnding { get; init; } = "\n";
    public string Transport { get; init; } = "Native";
    public string BridgeScript { get; init; } = "tools\\serial-bridge.ps1";
    public bool DryRun { get; init; }
    public IReadOnlyList<string> InitCommands { get; init; } = Array.Empty<string>();
    public CommandAdapterConfig CommandAdapter { get; init; } = CommandAdapterConfig.Disabled;

    public static PicoSenderConfig Load(string path, string? senderOverride = null)
    {
        var ini = IniDocument.Load(path);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
        var requestedSender = (senderOverride ?? "").Trim();
        string Section(string name) => ResolveSection(ini, name, requestedSender);
        string Get(string section, string key, string fallback = "") => ini.Get(Section(section), key, ini.Get(section, key, fallback));
        bool GetBool(string section, string key, bool fallback = false) => ini.GetBool(Section(section), key, ini.GetBool(section, key, fallback));
        int GetInt(string section, string key, int fallback = 0) => ini.GetInt(Section(section), key, ini.GetInt(section, key, fallback));

        var initCommands = BuildInitCommands(ini, requestedSender);
        return new PicoSenderConfig
        {
            SenderId = Get("Identity", "SenderId", requestedSender.Length > 0 ? requestedSender : "PICO"),
            Player = GetInt("Identity", "Player", 0),
            Role = Get("Identity", "Role", "player"),
            Port = Get("Serial", "Port", "auto"),
            BaudRate = GetInt("Serial", "BaudRate", 115200),
            BootDelayMs = GetInt("Serial", "BootDelayMs", 800),
            PostInitDelayMs = GetInt("Serial", "PostInitDelayMs", 1200),
            ProbeTimeoutMs = GetInt("Serial", "ProbeTimeoutMs", 900),
            WriteTimeoutMs = GetInt("Serial", "WriteTimeoutMs", 15000),
            LineEnding = Get("Serial", "LineEnding", "\\n").Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal),
            Transport = Get("Serial", "Transport", "Native"),
            BridgeScript = ResolvePath(baseDir, Get("Serial", "BridgeScript", "tools\\serial-bridge.ps1")),
            DryRun = GetBool("Serial", "DryRun", false),
            InitCommands = initCommands,
            CommandAdapter = CommandAdapterConfig.Load(ini, baseDir, requestedSender)
        };
    }

    private static string ResolvePath(string baseDir, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
    }

    internal static string ResolveSection(IniDocument ini, string section, string senderId)
    {
        var specific = senderId.Length > 0 ? $"{section}:{senderId}" : "";
        return specific.Length > 0 && ini.Section(specific).Count > 0 ? specific : section;
    }

    private static IReadOnlyList<string> BuildInitCommands(IniDocument ini, string senderId)
    {
        var advanced = PicoSenderConfig.ResolveSection(ini, "Advanced", senderId);
        var overrideCommands = ini.Get(advanced, "InitCommandsOverride", "");
        if (!string.IsNullOrWhiteSpace(overrideCommands))
        {
            return SplitCommands(overrideCommands);
        }

        var pico = PicoSenderConfig.ResolveSection(ini, "Pico", senderId);
        var autoInit = ini.GetBool(pico, "AutoInitFromHardware", ini.GetBool("Pico", "AutoInitFromHardware", false));
        if (autoInit)
        {
            return HardwareInitBuilder.Build(ini, senderId);
        }

        return SplitCommands(ini.Get(pico, "InitCommands", ini.Get("Pico", "InitCommands", "")));
    }

    private static IReadOnlyList<string> SplitCommands(string value)
    {
        return value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal sealed class CommandAdapterConfig
{
    public static CommandAdapterConfig Disabled { get; } = new();

    public bool Enabled { get; init; }
    public string Mode { get; init; } = "";
    public int FullPanelSlots { get; init; } = 8;
    public string FullPanelCommand { get; init; } = "ALLPCT {r} {g} {b}";
    public bool LogTranslations { get; init; } = true;
    public ColorPolicyConfig ColorPolicy { get; init; } = ColorPolicyConfig.Disabled;
    public IReadOnlyDictionary<string, PctColor> FullPanelColors { get; init; } =
        new Dictionary<string, PctColor>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> FullPanelAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static CommandAdapterConfig Load(IniDocument ini, string baseDir, string senderId)
    {
        var colors = new Dictionary<string, PctColor>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in MergedSection(ini, "CommandAdapter.FullPanelColors", senderId))
        {
            if (PctColor.TryParse(pair.Value, out var color))
            {
                colors[ColorPolicyConfig.NormalizeColorName(pair.Key)] = color;
            }
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in MergedSection(ini, "CommandAdapter.FullPanelAliases", senderId))
        {
            var source = ColorPolicyConfig.NormalizeColorName(pair.Key);
            var target = ColorPolicyConfig.NormalizeColorName(pair.Value);
            if (source.Length > 0 && target.Length > 0)
            {
                aliases[source] = target;
            }
        }

        var section = PicoSenderConfig.ResolveSection(ini, "CommandAdapter", senderId);
        return new CommandAdapterConfig
        {
            Enabled = ini.GetBool(section, "Enabled", ini.GetBool("CommandAdapter", "Enabled", false)),
            Mode = ini.Get(section, "Mode", ini.Get("CommandAdapter", "Mode", "")),
            FullPanelSlots = Math.Max(1, ini.GetInt(section, "FullPanelSlots", ini.GetInt("CommandAdapter", "FullPanelSlots", 8))),
            FullPanelCommand = ini.Get(section, "FullPanelCommand", ini.Get("CommandAdapter", "FullPanelCommand", "ALLPCT {r} {g} {b}")),
            LogTranslations = ini.GetBool(section, "LogTranslations", ini.GetBool("CommandAdapter", "LogTranslations", true)),
            ColorPolicy = ColorPolicyConfig.Load(ini, baseDir, senderId),
            FullPanelColors = colors,
            FullPanelAliases = aliases
        };
    }

    internal static IReadOnlyDictionary<string, string> MergedSection(IniDocument ini, string section, string senderId)
    {
        var values = new Dictionary<string, string>(ini.Section(section), StringComparer.OrdinalIgnoreCase);
        var specific = senderId.Length > 0 ? $"{section}:{senderId}" : "";
        if (specific.Length > 0)
        {
            foreach (var pair in ini.Section(specific))
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }
}

internal static class HardwareInitBuilder
{
    private static readonly HashSet<int> SafeGpios = new(Enumerable.Range(0, 23).Concat(new[] { 26, 27, 28 }));

    public static IReadOnlyList<string> Build(IniDocument ini, string senderId)
    {
        var hardware = PicoSenderConfig.ResolveSection(ini, "Hardware", senderId);
        var gpio = PicoSenderConfig.ResolveSection(ini, "GPIO", senderId);
        var commands = new List<string> { "PING", "INIT" };
        var used = new Dictionary<int, string>();

        var buttons = Math.Clamp(
            GetInt(ini, hardware, "PanelButtons", GetInt(ini, hardware, "Buttons", 8)),
            0,
            8);
        var buttonType = NormalizeLedType(Get(ini, hardware, "PanelButtonType", Get(ini, hardware, "ButtonMode", "RGBLED")));

        if (buttons > 0 && buttonType != "RGBLED")
        {
            throw new InvalidOperationException($"PanelButtonType={buttonType} is not supported for GPIO auto init yet. Use RGBLED or Advanced.InitCommandsOverride.");
        }

        for (var i = 1; i <= buttons; i++)
        {
            var name = $"B{i}";
            var pins = ParsePins(Get(ini, gpio, name, ""), 3, name);
            ReservePins(used, pins, name);
            commands.Add($"PTR {name} BUTTON RGB {i} {string.Join(',', pins)} LOW");
        }

        AddOptionalOutput(ini, hardware, gpio, used, commands, "START", "START", "START");
        AddOptionalOutput(ini, hardware, gpio, used, commands, "SELECT", "SELECT", "SELECT");
        AddOptionalOutput(ini, hardware, gpio, used, commands, "Joystick1", "JOY1", "JOY1");
        AddOptionalOutput(ini, hardware, gpio, used, commands, "Joystick2", "JOY2", "JOY2");

        commands.Add("COMMIT");
        if (GetBool(ini, hardware, "OnOffInvert", false))
        {
            commands.Add("ONOFFINVERT ON");
        }

        commands.Add("GET");
        Console.Error.WriteLine($"[hardware] sender={(senderId.Length > 0 ? senderId : "default")} auto-init GPIO used={used.Count}/26 panel={buttons} {buttonType}");
        return commands;
    }

    private static void AddOptionalOutput(
        IniDocument ini,
        string hardware,
        string gpio,
        Dictionary<int, string> used,
        List<string> commands,
        string hardwareKey,
        string ptrName,
        string slot)
    {
        var type = NormalizeLedType(Get(ini, hardware, hardwareKey, "NONE"));
        if (type == "NONE")
        {
            return;
        }

        if (type == "LED")
        {
            var pins = ParsePins(Get(ini, gpio, ptrName, ""), 1, ptrName);
            ReservePins(used, pins, ptrName);
            commands.Add($"PTR {ptrName} {OutputKind(ptrName)} ONOFF {slot} {pins[0]} LOW");
            return;
        }

        if (type == "RGBLED")
        {
            var pins = ParsePins(Get(ini, gpio, ptrName, ""), 3, ptrName);
            ReservePins(used, pins, ptrName);
            commands.Add($"PTR {ptrName} {OutputKind(ptrName)} RGB {slot} {string.Join(',', pins)} LOW");
            return;
        }

        throw new InvalidOperationException($"{hardwareKey}={type} is not supported for GPIO auto init yet. Use LED, RGBLED, NONE, or Advanced.InitCommandsOverride.");
    }

    private static string OutputKind(string name)
    {
        return name switch
        {
            "START" => "START",
            "SELECT" => "SELECT",
            _ when name.StartsWith("JOY", StringComparison.OrdinalIgnoreCase) => "JOY",
            _ => "AUX"
        };
    }

    private static string NormalizeLedType(string value)
    {
        var type = (value ?? "").Trim().ToUpperInvariant();
        return type switch
        {
            "" or "NO" or "OFF" or "ABSENT" => "NONE",
            "ONOFF" or "GPIO" or "SIMPLE" => "LED",
            "RGB" => "RGBLED",
            "ADDR" or "ADDRESSABLE" or "WS2812" or "NEOPIXEL" => "ADDRLED",
            _ => type
        };
    }

    private static IReadOnlyList<int> ParsePins(string value, int expected, string name)
    {
        var pins = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var pin) ? pin : -1)
            .ToList();
        if (pins.Count != expected || pins.Any(pin => pin < 0))
        {
            throw new InvalidOperationException($"{name} expects {expected} GPIO value(s), got '{value}'.");
        }

        return pins;
    }

    private static void ReservePins(Dictionary<int, string> used, IReadOnlyList<int> pins, string owner)
    {
        foreach (var pin in pins)
        {
            if (!SafeGpios.Contains(pin))
            {
                throw new InvalidOperationException($"{owner} uses forbidden GPIO {pin}. Allowed: GP0..GP22 + GP26..GP28.");
            }

            if (used.TryGetValue(pin, out var previous))
            {
                throw new InvalidOperationException($"{owner} reuses GPIO {pin}, already used by {previous}.");
            }

            used[pin] = owner;
        }
    }

    private static string Get(IniDocument ini, string section, string key, string fallback)
    {
        return ini.Get(section, key, fallback);
    }

    private static int GetInt(IniDocument ini, string section, string key, int fallback)
    {
        return ini.GetInt(section, key, fallback);
    }

    private static bool GetBool(IniDocument ini, string section, string key, bool fallback)
    {
        return ini.GetBool(section, key, fallback);
    }
}

internal sealed class ColorPolicyConfig
{
    public static ColorPolicyConfig Disabled { get; } = new();

    public bool Enabled { get; init; }
    public bool LogDecisions { get; init; } = true;
    public string Unknown { get; init; } = "allow";
    public string OnDeny { get; init; } = "fallback";
    public string SlotCommand { get; init; } = "SLOTPWM";
    public bool UseSlotCommandForAllowed { get; init; } = true;
    public bool IncludeUnknownTargets { get; init; }
    public IReadOnlySet<string> RgbTargets { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> IgnoredTargets { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Fallbacks { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, Dictionary<int, bool>> SingleRules { get; init; } =
        new Dictionary<string, Dictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, Dictionary<string, bool>> PairRules { get; init; } =
        new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

    public static ColorPolicyConfig Load(IniDocument senderIni, string baseDir, string senderId)
    {
        var adapterSection = PicoSenderConfig.ResolveSection(senderIni, "CommandAdapter", senderId);
        var path = senderIni.Get(adapterSection, "ColorPolicyFile", senderIni.Get("CommandAdapter", "ColorPolicyFile", ""));
        var ini = senderIni;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (senderIni.Section(PicoSenderConfig.ResolveSection(senderIni, "ColorPolicy", senderId)).Count == 0 &&
                senderIni.Section("ColorPolicy").Count == 0)
            {
                return Disabled;
            }
        }
        else
        {
            var resolved = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
            if (!File.Exists(resolved))
            {
                Console.Error.WriteLine($"[adapter] color policy file not found: {resolved}");
                return Disabled;
            }

            ini = IniDocument.Load(resolved);
            senderId = "";
        }

        var policySection = PicoSenderConfig.ResolveSection(ini, "ColorPolicy", senderId);
        var rgbTargets = ParseSet(ini.Get(policySection, "RgbTargets", ini.Get("ColorPolicy", "RgbTargets", "")));
        var ignoredTargets = ParseSet(ini.Get(policySection, "IgnoredTargets", ini.Get("ColorPolicy", "IgnoredTargets", "")));
        var fallbacks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CommandAdapterConfig.MergedSection(ini, "ColorPolicy.Fallbacks", senderId))
        {
            var key = NormalizeColorName(pair.Key);
            var value = NormalizeColorName(pair.Value);
            if (key.Length > 0 && value.Length > 0)
            {
                fallbacks[key] = value;
            }
        }

        var singleRules = new Dictionary<string, Dictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CommandAdapterConfig.MergedSection(ini, "ColorPolicy.Single", senderId))
        {
            singleRules[NormalizeColorName(pair.Key)] = ParseSingleRules(pair.Value);
        }

        var pairRules = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CommandAdapterConfig.MergedSection(ini, "ColorPolicy.Pair", senderId))
        {
            var key = NormalizePairKey(pair.Key);
            if (key.Length > 0)
            {
                pairRules[key] = ParsePairRules(pair.Value);
            }
        }

        return new ColorPolicyConfig
        {
            Enabled = ini.GetBool(policySection, "Enabled", ini.GetBool("ColorPolicy", "Enabled", true)),
            LogDecisions = ini.GetBool(policySection, "LogDecisions", ini.GetBool("ColorPolicy", "LogDecisions", true)),
            Unknown = ini.Get(policySection, "Unknown", ini.Get("ColorPolicy", "Unknown", "allow")).Trim().ToLowerInvariant(),
            OnDeny = ini.Get(policySection, "OnDeny", ini.Get("ColorPolicy", "OnDeny", "fallback")).Trim().ToLowerInvariant(),
            SlotCommand = NormalizeCommand(ini.Get(policySection, "SlotCommand", ini.Get("ColorPolicy", "SlotCommand", "SLOTPWM"))),
            UseSlotCommandForAllowed = ini.GetBool(policySection, "UseSlotCommandForAllowed", ini.GetBool("ColorPolicy", "UseSlotCommandForAllowed", true)),
            IncludeUnknownTargets = ini.GetBool(policySection, "IncludeUnknownTargets", ini.GetBool("ColorPolicy", "IncludeUnknownTargets", false)),
            RgbTargets = rgbTargets,
            IgnoredTargets = ignoredTargets,
            Fallbacks = fallbacks,
            SingleRules = singleRules,
            PairRules = pairRules
        };
    }

    public bool IsCountedTarget(string target)
    {
        target = NormalizeTarget(target);
        if (IgnoredTargets.Contains(target))
        {
            return false;
        }

        if (RgbTargets.Count == 0)
        {
            return IncludeUnknownTargets || int.TryParse(target, out _);
        }

        return RgbTargets.Contains(target) || IncludeUnknownTargets;
    }

    public bool TryResolveSingle(string color, int count, out bool allowed)
    {
        allowed = UnknownAllows();
        if (!SingleRules.TryGetValue(NormalizeColorName(color), out var rules))
        {
            return false;
        }

        if (rules.TryGetValue(count, out allowed))
        {
            return true;
        }

        var nearest = rules
            .OrderBy(pair => Math.Abs(pair.Key - count))
            .ThenBy(pair => pair.Key)
            .FirstOrDefault();
        if (nearest.Key > 0)
        {
            allowed = nearest.Value;
            return true;
        }

        return false;
    }

    public bool TryResolvePair(string leftColor, int leftCount, string rightColor, int rightCount, out bool allowed)
    {
        allowed = UnknownAllows();
        var key = NormalizePairKey($"{leftColor}|{rightColor}");
        if (PairRules.TryGetValue(key, out var rules) &&
            TryResolvePairCounts(rules, leftCount, rightCount, out allowed))
        {
            return true;
        }

        key = NormalizePairKey($"{rightColor}|{leftColor}");
        if (PairRules.TryGetValue(key, out rules) &&
            TryResolvePairCounts(rules, rightCount, leftCount, out allowed))
        {
            return true;
        }

        return false;
    }

    public bool UnknownAllows() => Unknown is not ("deny" or "block" or "fallback");

    private static HashSet<string> ParseSet(string value)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var target = NormalizeTarget(part);
            if (target.Length > 0)
            {
                set.Add(target);
            }
        }

        return set;
    }

    private static Dictionary<int, bool> ParseSingleRules(string value)
    {
        var rules = new Dictionary<int, bool>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length != 2 || !int.TryParse(pieces[0], out var count))
            {
                continue;
            }

            rules[count] = IsAllow(pieces[1]);
        }

        return rules;
    }

    private static Dictionary<string, bool> ParsePairRules(string value)
    {
        var rules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length != 2)
            {
                continue;
            }

            var counts = NormalizeCountPair(pieces[0]);
            if (counts.Length > 0)
            {
                rules[counts] = IsAllow(pieces[1]);
            }
        }

        return rules;
    }

    private static bool TryResolvePairCounts(Dictionary<string, bool> rules, int leftCount, int rightCount, out bool allowed)
    {
        allowed = true;
        var exact = NormalizeCountPair($"{leftCount}+{rightCount}");
        if (rules.TryGetValue(exact, out allowed))
        {
            return true;
        }

        var nearest = rules
            .Select(pair => new
            {
                pair.Key,
                pair.Value,
                Distance = CountPairDistance(pair.Key, leftCount, rightCount)
            })
            .Where(pair => pair.Distance >= 0)
            .OrderBy(pair => pair.Distance)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (nearest is null)
        {
            return false;
        }

        allowed = nearest.Value;
        return true;
    }

    private static int CountPairDistance(string value, int leftCount, int rightCount)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var left) || !int.TryParse(parts[1], out var right))
        {
            return -1;
        }

        return Math.Abs(left - leftCount) + Math.Abs(right - rightCount);
    }

    private static bool IsAllow(string value)
    {
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "y" or "ok" or "allow";
    }

    public static string NormalizeColorName(string value)
    {
        var color = (value ?? "").Trim().ToUpperInvariant();
        return color is "OFF" or "0" or "FALSE" or "NO" ? "BLACK" : color;
    }

    public static string NormalizeTarget(string value)
    {
        var target = (value ?? "").Trim().ToUpperInvariant();
        return target.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase) ? target["SLOT:".Length..] : target;
    }

    private static string NormalizeCommand(string value)
    {
        var command = (value ?? "").Trim().ToUpperInvariant();
        return command.Length > 0 ? command : "SLOTPWM";
    }

    private static string NormalizePairKey(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? $"{NormalizeColorName(parts[0])}|{NormalizeColorName(parts[1])}" : "";
    }

    private static string NormalizeCountPair(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var left) && int.TryParse(parts[1], out var right)
            ? $"{left}+{right}"
            : "";
    }
}

internal readonly record struct PctColor(int R, int G, int B)
{
    public static bool TryParse(string value, out PctColor color)
    {
        color = default;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !TryPct(parts[0], out var r) ||
            !TryPct(parts[1], out var g) ||
            !TryPct(parts[2], out var b))
        {
            return false;
        }

        color = new PctColor(r, g, b);
        return true;
    }

    private static bool TryPct(string value, out int pct)
    {
        if (!int.TryParse(value, out pct))
        {
            return false;
        }

        pct = Math.Clamp(pct, 0, 100);
        return true;
    }
}

internal static class PicoCommandTranslator
{
    public static IReadOnlyList<string> Translate(string command, CommandAdapterConfig adapter)
    {
        if (!adapter.Enabled ||
            !adapter.Mode.Equals("PicoFullPanelPwm", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(command))
        {
            return new[] { command };
        }

        if (TryTranslateAllColor(command, adapter, out var translated) ||
            TryTranslateFullPanelBatch(command, adapter, out translated))
        {
            if (adapter.LogTranslations)
            {
                Console.WriteLine($"[adapter] {command} -> {translated}");
            }

            return new[] { translated };
        }

        if (TryTranslateColorPolicy(command, adapter, out var translatedCommands))
        {
            if (adapter.LogTranslations)
            {
                Console.WriteLine($"[adapter] {command} -> {string.Join(" | ", translatedCommands)}");
            }

            return translatedCommands;
        }

        return new[] { command };
    }

    private static bool TryTranslateAllColor(string command, CommandAdapterConfig adapter, out string translated)
    {
        translated = "";
        var parts = command.Trim().Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var color = NormalizeColor(parts[1]);
        return TryBuildFullPanelCommand(color, adapter, out translated);
    }

    private static bool TryTranslateFullPanelBatch(string command, CommandAdapterConfig adapter, out string translated)
    {
        translated = "";
        var trimmed = command.Trim();
        if (!trimmed.StartsWith("BATCH ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var slotColors = new Dictionary<int, string>();
        foreach (var item in trimmed["BATCH ".Length..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseColorCommand(item, out var parsed) ||
                parsed.TargetKind != "SLOT" ||
                !int.TryParse(parsed.Target, out var slot))
            {
                return false;
            }

            if (slot < 1 || slot > adapter.FullPanelSlots || slotColors.ContainsKey(slot))
            {
                return false;
            }

            slotColors[slot] = parsed.Color;
        }

        if (slotColors.Count != adapter.FullPanelSlots)
        {
            return false;
        }

        for (var slot = 1; slot <= adapter.FullPanelSlots; slot++)
        {
            if (!slotColors.ContainsKey(slot))
            {
                return false;
            }
        }

        var firstColor = NormalizeColor(slotColors[1]);
        if (slotColors.Values.Any(color => !color.Equals(firstColor, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return TryBuildFullPanelCommand(firstColor, adapter, out translated);
    }

    private static bool TryTranslateColorPolicy(string command, CommandAdapterConfig adapter, out IReadOnlyList<string> translated)
    {
        translated = Array.Empty<string>();
        var policy = adapter.ColorPolicy;
        if (!policy.Enabled)
        {
            return false;
        }

        if (command.Trim().StartsWith("BATCH ", StringComparison.OrdinalIgnoreCase))
        {
            var items = command.Trim()["BATCH ".Length..]
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var parsed = new List<ParsedColorCommand>();
            foreach (var item in items)
            {
                if (!TryParseColorCommand(item, out var parsedItem))
                {
                    return false;
                }

                parsed.Add(parsedItem);
            }

            if (!TryApplyPolicy(parsed, policy, out var rewritten, out var batchReason))
            {
                return false;
            }

            if (policy.LogDecisions && batchReason.Length > 0)
            {
                Console.WriteLine($"[color-policy] {batchReason}");
            }

            translated = new[] { "BATCH " + string.Join(';', rewritten.Select(item => item.ToCommand())) };
            return true;
        }

        if (!TryParseColorCommand(command, out var single))
        {
            return false;
        }

        if (!policy.IsCountedTarget(single.Target) || IsBlack(single.Color))
        {
            return false;
        }

        var allowed = policy.UnknownAllows();
        var known = policy.TryResolveSingle(single.Color, 1, out allowed);
        var rewrittenSingle = single;
        var reason = "";
        if (!allowed && policy.OnDeny == "fallback")
        {
            if (policy.Fallbacks.TryGetValue(single.Color, out var fallback))
            {
                rewrittenSingle = rewrittenSingle with { Color = NormalizeColor(fallback) };
                reason = $"single {single.Target} {single.Color} denied -> {rewrittenSingle.Color}";
            }
        }
        else if (allowed && policy.UseSlotCommandForAllowed)
        {
            rewrittenSingle = ForcePwmCommand(rewrittenSingle, policy);
            reason = known ? $"single {single.Target} {single.Color} allowed -> pwm" : "";
        }

        if (rewrittenSingle.Equals(single))
        {
            return false;
        }

        if (policy.LogDecisions && reason.Length > 0)
        {
            Console.WriteLine($"[color-policy] {reason}");
        }

        translated = new[] { rewrittenSingle.ToCommand() };
        return true;
    }

    private static bool TryApplyPolicy(
        IReadOnlyList<ParsedColorCommand> commands,
        ColorPolicyConfig policy,
        out IReadOnlyList<ParsedColorCommand> rewritten,
        out string reason)
    {
        rewritten = commands;
        reason = "";

        var counted = commands
            .Where(item => policy.IsCountedTarget(item.Target))
            .Where(item => !IsBlack(item.Color))
            .ToList();
        if (counted.Count == 0)
        {
            return false;
        }

        var colorCounts = counted
            .GroupBy(item => item.Color, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var allowed = policy.UnknownAllows();
        var known = false;
        if (colorCounts.Count == 1)
        {
            var pair = colorCounts.First();
            known = policy.TryResolveSingle(pair.Key, pair.Value, out allowed);
        }
        else if (colorCounts.Count == 2)
        {
            var first = colorCounts.ElementAt(0);
            var second = colorCounts.ElementAt(1);
            known = policy.TryResolvePair(first.Key, first.Value, second.Key, second.Value, out allowed);
        }

        var changed = false;
        var next = commands.ToList();
        if (!allowed && policy.OnDeny == "fallback")
        {
            for (var i = 0; i < next.Count; i++)
            {
                var item = next[i];
                if (!policy.IsCountedTarget(item.Target) || IsBlack(item.Color))
                {
                    continue;
                }

                if (policy.Fallbacks.TryGetValue(item.Color, out var fallback))
                {
                    next[i] = item with { Color = NormalizeColor(fallback) };
                    changed = true;
                }
            }

            if (changed)
            {
                reason = $"denied {DescribeCounts(colorCounts)} -> fallback";
            }
        }
        else if (allowed && policy.UseSlotCommandForAllowed)
        {
            for (var i = 0; i < next.Count; i++)
            {
                var item = next[i];
                if (!policy.IsCountedTarget(item.Target) || IsBlack(item.Color))
                {
                    continue;
                }

                var forced = ForcePwmCommand(item, policy);
                if (!forced.Equals(item))
                {
                    next[i] = forced;
                    changed = true;
                }
            }

            if (changed && known)
            {
                reason = $"allowed {DescribeCounts(colorCounts)} -> pwm";
            }
        }

        if (!changed)
        {
            return false;
        }

        rewritten = next;
        return true;
    }

    private static ParsedColorCommand ForcePwmCommand(ParsedColorCommand item, ColorPolicyConfig policy)
    {
        return item.TargetKind switch
        {
            "SLOT" => item with { Command = policy.SlotCommand },
            "OUTPUT" when item.Command.Equals("SET", StringComparison.OrdinalIgnoreCase) => item with { Command = "SETPWM" },
            _ => item
        };
    }

    private static string DescribeCounts(IReadOnlyDictionary<string, int> colorCounts)
    {
        return string.Join("+", colorCounts.Select(pair => $"{pair.Key}:{pair.Value}"));
    }

    private static bool TryParseColorCommand(string item, out ParsedColorCommand parsed)
    {
        parsed = default;
        var parts = item.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        var command = parts[0].Trim().ToUpperInvariant();
        var targetKind = command switch
        {
            "SLOT" or "SLOTPWM" => "SLOT",
            "SET" or "SETPWM" => "OUTPUT",
            _ => ""
        };
        if (targetKind.Length == 0)
        {
            return false;
        }

        parsed = new ParsedColorCommand(
            command,
            targetKind,
            ColorPolicyConfig.NormalizeTarget(parts[1]),
            NormalizeColor(parts[2]));
        return parsed.Color.Length > 0;
    }

    private static bool TryBuildFullPanelCommand(string color, CommandAdapterConfig adapter, out string translated)
    {
        translated = "";
        color = ResolveFullPanelColorName(NormalizeColor(color), adapter);
        if (!adapter.FullPanelColors.TryGetValue(color, out var pct))
        {
            return false;
        }

        translated = adapter.FullPanelCommand
            .Replace("{r}", pct.R.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{g}", pct.G.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{b}", pct.B.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{color}", color, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string ResolveFullPanelColorName(string color, CommandAdapterConfig adapter)
    {
        var current = color;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (adapter.FullPanelAliases.TryGetValue(current, out var alias) && seen.Add(current))
        {
            current = NormalizeColor(alias);
        }

        return current;
    }

    private static string NormalizeColor(string value)
    {
        return ColorPolicyConfig.NormalizeColorName(value);
    }

    private static bool IsBlack(string color)
    {
        return NormalizeColor(color).Equals("BLACK", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSlot(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["SLOT:".Length..];
        }

        return normalized;
    }
}

internal readonly record struct ParsedColorCommand(string Command, string TargetKind, string Target, string Color)
{
    public string ToCommand() => $"{Command} {Target} {Color}";
}

internal sealed class PicoSerial : IAsyncDisposable
{
    private readonly PicoSenderConfig _config;
    private readonly SafeFileHandle? _handle;
    private readonly Action<string>? _bridgeOutput;
    private Process? _bridge;

    private PicoSerial(PicoSenderConfig config, string portName, SafeFileHandle? handle, Process? bridge = null, Action<string>? bridgeOutput = null)
    {
        _config = config;
        PortName = portName;
        _handle = handle;
        _bridge = bridge;
        _bridgeOutput = bridgeOutput;
    }

    public string PortName { get; }

    public static async Task<PicoSerial> OpenAsync(PicoSenderConfig config, Action<string>? bridgeOutput = null)
    {
        var port = config.Port.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? await AutoDetectAsync(config) ?? throw new InvalidOperationException("No Pico detected on serial ports.")
            : config.Port;

        if (config.DryRun)
        {
            return new PicoSerial(config, port, null, bridgeOutput: bridgeOutput);
        }

        if (config.Transport.Equals("PowerShellBridge", StringComparison.OrdinalIgnoreCase))
        {
            var bridge = StartPowerShellBridge(config, port, bridgeOutput);
            if (config.BootDelayMs > 0)
            {
                await Task.Delay(config.BootDelayMs);
            }

            return new PicoSerial(config, port, null, bridge, bridgeOutput);
        }

        ConfigurePort(port, config.BaudRate);
        var handle = OpenPortHandle(port);
        ConfigureHandle(handle, config.BaudRate);
        SetSerialTimeouts(handle, config.ProbeTimeoutMs);
        NativeMethods.PurgeComm(handle, NativeMethods.PurgeRxClear | NativeMethods.PurgeTxClear);
        if (config.BootDelayMs > 0)
        {
            await Task.Delay(config.BootDelayMs);
        }

        return new PicoSerial(config, port, handle, bridgeOutput: bridgeOutput);
    }

    public static async Task<string?> AutoDetectAsync(PicoSenderConfig config)
    {
        foreach (var port in EnumeratePorts())
        {
            try
            {
                ConfigurePort(port, config.BaudRate);
                using var handle = OpenPortHandle(port);
                ConfigureHandle(handle, config.BaudRate);
                SetSerialTimeouts(handle, config.ProbeTimeoutMs);
                NativeMethods.PurgeComm(handle, NativeMethods.PurgeRxClear | NativeMethods.PurgeTxClear);
                await Task.Delay(200);
                WriteLine(handle, "PING", config.LineEnding);
                var reply = ReadSome(handle);
                if (reply.Contains("PONG", StringComparison.OrdinalIgnoreCase) ||
                    reply.Contains("READY", StringComparison.OrdinalIgnoreCase) ||
                    reply.Contains("DYNAMIC PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    return port;
                }
            }
            catch
            {
                // Busy or unrelated port.
            }
        }

        return null;
    }

    public void Send(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (_handle is null && _config.DryRun)
        {
            Console.WriteLine($"[dry-run:{PortName}] {command}");
            return;
        }

        if (_bridge is not null)
        {
            if (_bridge.HasExited)
            {
                throw new IOException($"PowerShell serial bridge exited with code {_bridge.ExitCode}.");
            }

            _bridge.StandardInput.WriteLine(command);
            _bridge.StandardInput.Flush();
            return;
        }

        if (_handle is null)
        {
            throw new InvalidOperationException("Serial handle is not open.");
        }

        WriteLine(_handle, command, _config.LineEnding);
    }

    public async Task ReconnectAsync()
    {
        if (_bridge is not null)
        {
            try
            {
                if (!_bridge.HasExited)
                {
                    _bridge.Kill();
                }
            }
            catch
            {
            }

            _bridge.Dispose();
            _bridge = StartPowerShellBridge(_config, PortName, _bridgeOutput);
            if (_config.BootDelayMs > 0)
            {
                await Task.Delay(_config.BootDelayMs);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _handle?.Dispose();
        if (_bridge is not null && !_bridge.HasExited)
        {
            try
            {
                _bridge.StandardInput.Close();
                if (!_bridge.WaitForExit(1000))
                {
                    _bridge.Kill();
                }
            }
            catch
            {
            }
        }

        _bridge?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Process StartPowerShellBridge(PicoSenderConfig config, string port, Action<string>? bridgeOutput)
    {
        if (!File.Exists(config.BridgeScript))
        {
            throw new FileNotFoundException("Serial bridge script not found.", config.BridgeScript);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(config.BridgeScript);
        psi.ArgumentList.Add("-Port");
        psi.ArgumentList.Add(port);
        psi.ArgumentList.Add("-BaudRate");
        psi.ArgumentList.Add(config.BaudRate.ToString());
        psi.ArgumentList.Add("-WriteTimeoutMs");
        psi.ArgumentList.Add(config.WriteTimeoutMs.ToString());

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start PowerShell serial bridge.");
        _ = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                bridgeOutput?.Invoke(line);
                Console.WriteLine("[bridge] " + line);
            }
        });
        _ = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                Console.Error.WriteLine("[bridge] " + line);
            }
        });

        return process;
    }

    private static IEnumerable<string> EnumeratePorts()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is not null)
        {
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is string port)
                {
                    yield return port;
                }
            }
        }

        for (var i = 1; i <= 32; i++)
        {
            yield return "COM" + i;
        }
    }

    private static SafeFileHandle OpenPortHandle(string port)
    {
        var path = port.StartsWith("\\\\.\\", StringComparison.Ordinal) ? port : "\\\\.\\" + port;
        var handle = NativeMethods.CreateFile(
            path,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            0,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new IOException($"Cannot open serial port {port}.", Marshal.GetLastWin32Error());
        }

        return handle;
    }

    private static void ConfigurePort(string port, int baudRate)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "mode.com",
                Arguments = $"{port}: BAUD={baudRate} PARITY=N DATA=8 STOP=1",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(1000);
        }
        catch
        {
            // Direct file access may still work if the port already has the right settings.
        }
    }

    private static void WriteLine(SafeFileHandle handle, string command, string lineEnding)
    {
        var bytes = Encoding.UTF8.GetBytes(command.TrimEnd('\r', '\n') + lineEnding);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (NativeMethods.WriteFile(handle, bytes, bytes.Length, out var written, IntPtr.Zero) && written == bytes.Length)
            {
                return;
            }

            Thread.Sleep(60 * attempt);
        }

        throw new IOException($"Serial write failed after retries. Bytes={bytes.Length}.", Marshal.GetLastWin32Error());
    }

    private static string ReadSome(SafeFileHandle handle)
    {
        var buffer = new byte[1024];
        try
        {
            return NativeMethods.ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero) && read > 0
                ? Encoding.UTF8.GetString(buffer, 0, read)
                : "";
        }
        catch
        {
        }

        return "";
    }

    private static void SetSerialTimeouts(SafeFileHandle handle, int timeoutMs)
    {
        var timeouts = new NativeMethods.CommTimeouts
        {
            ReadIntervalTimeout = 50,
            ReadTotalTimeoutMultiplier = 1,
            ReadTotalTimeoutConstant = timeoutMs,
            WriteTotalTimeoutMultiplier = 1,
            WriteTotalTimeoutConstant = timeoutMs
        };

        NativeMethods.SetCommTimeouts(handle, ref timeouts);
    }

    private static void ConfigureHandle(SafeFileHandle handle, int baudRate)
    {
        var dcb = new NativeMethods.Dcb
        {
            DCBlength = Marshal.SizeOf<NativeMethods.Dcb>()
        };

        if (!NativeMethods.GetCommState(handle, ref dcb))
        {
            throw new IOException("GetCommState failed.", Marshal.GetLastWin32Error());
        }

        dcb.DCBlength = Marshal.SizeOf<NativeMethods.Dcb>();
        dcb.BaudRate = baudRate;
        dcb.ByteSize = 8;
        dcb.Parity = 0; // NOPARITY
        dcb.StopBits = 0; // ONESTOPBIT
        dcb.Flags = 1 | (1 << 4) | (1 << 12); // fBinary + DTR enable + RTS enable

        if (!NativeMethods.SetCommState(handle, ref dcb))
        {
            throw new IOException($"SetCommState failed win32={Marshal.GetLastWin32Error()}.");
        }
    }
}

internal static partial class NativeMethods
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint OpenExisting = 3;
    public const uint PurgeTxClear = 0x0004;
    public const uint PurgeRxClear = 0x0008;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct Dcb
    {
        public int DCBlength;
        public int BaudRate;
        public int Flags;
        public short wReserved;
        public short XonLim;
        public short XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public sbyte XonChar;
        public sbyte XoffChar;
        public sbyte ErrorChar;
        public sbyte EofChar;
        public sbyte EvtChar;
        public short wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public int ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public int WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        int numberOfBytesToWrite,
        out int numberOfBytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadFile(
        SafeFileHandle file,
        byte[] buffer,
        int numberOfBytesToRead,
        out int numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PurgeComm(SafeFileHandle file, uint flags);
}
