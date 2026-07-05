using LedManager.Core.Ini;

namespace LedManager.Core.Configuration;

public sealed class LedManagerConfig
{
    public string BaseDirectory { get; init; } = AppContext.BaseDirectory;
    public string DefaultSenderId { get; init; } = "DEFAULT";
    public string GlobalSenderId { get; init; } = "GLOBAL";
    public bool RestorePanelOnGameEnd { get; init; } = true;
    public string PanelSnapshotPath { get; init; } = "state\\panel-before-game.json";
    public ApiExposeConfig ApiExpose { get; init; } = new();
    public EffectsConfig Effects { get; init; } = new();
    public FrontendFeedbackConfig FrontendFeedback { get; init; } = new();
    public HardwareConfig Hardware { get; init; } = new();
    public IReadOnlyDictionary<string, CommandSenderConfig> Senders { get; init; } =
        new Dictionary<string, CommandSenderConfig>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<int, string> PlayerRouting { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<string, string> TargetRouting { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public CommandTemplates Templates { get; init; } = new();

    public static LedManagerConfig Load(string path)
    {
        var ini = IniDocument.Load(path);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;

        var senders = new Dictionary<string, CommandSenderConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in ini.Sections.Where(s => s.StartsWith("CommandSender:", StringComparison.OrdinalIgnoreCase)))
        {
            var id = section["CommandSender:".Length..].Trim();
            if (id.Length == 0)
            {
                continue;
            }

            senders[id] = new CommandSenderConfig
            {
                Id = id,
                Name = ini.Get(section, "Name", id),
                Enabled = ini.GetBool(section, "Enabled", true),
                Player = ini.GetInt(section, "Player", 0),
                Executable = ResolvePath(baseDir, ini.Get(section, "Executable")),
                Arguments = ini.Get(section, "Arguments"),
                Mode = ini.Get(section, "Mode", "daemon"),
                UseStdIn = ini.GetBool(section, "UseStdIn", true),
                LineEnding = DecodeLineEnding(ini.Get(section, "LineEnding", "\\n")),
                DryRun = ini.GetBool(section, "DryRun", false),
                StartupDelayMs = ini.GetInt(section, "StartupDelayMs", 1500),
                QueueCapacity = ini.GetInt(section, "QueueCapacity", 16),
                MaxQueueAgeMs = ini.GetInt(section, "MaxQueueAgeMs", 150),
                SendIntervalMs = ini.GetInt(section, "SendIntervalMs", 10)
            };
        }

        var playerRouting = new Dictionary<int, string>();
        foreach (var pair in ini.Section("PlayerRouting"))
        {
            if (int.TryParse(pair.Key, out var player))
            {
                playerRouting[player] = pair.Value.Trim();
            }
        }

        foreach (var sender in senders.Values)
        {
            if (sender.Player > 0)
            {
                playerRouting.TryAdd(sender.Player, sender.Id);
            }
        }

        return new LedManagerConfig
        {
            BaseDirectory = baseDir,
            DefaultSenderId = ini.Get("CommandSenders", "Default", "DEFAULT"),
            GlobalSenderId = ini.Get("CommandSenders", "Global", "GLOBAL"),
            RestorePanelOnGameEnd = ini.GetBool("PanelPersistence", "RestoreOnGameEnd", true),
            PanelSnapshotPath = ResolvePath(baseDir, ini.Get("PanelPersistence", "SnapshotPath", "state\\panel-before-game.json")),
            ApiExpose = new ApiExposeConfig
            {
                Enabled = ini.GetBool("APIExpose", "Enabled", false),
                BaseUrl = ini.Get("APIExpose", "BaseUrl", "ws://127.0.0.1:12345"),
                FrontendPath = ini.Get("APIExpose", "FrontendPath", "/ws/frontend"),
                PanelPath = ini.Get("APIExpose", "PanelPath", "/ws/panel"),
                IngamePath = ini.Get("APIExpose", "IngamePath", "/ws/ingame"),
                ArcadePath = ini.Get("APIExpose", "ArcadePath", "/ws/arcade"),
                HiscorePath = ini.Get("APIExpose", "HiscorePath", "/ws/hiscore"),
                DefaultPlayer = ini.GetInt("APIExpose", "DefaultPlayer", 1)
            },
            Effects = new EffectsConfig
            {
                Enabled = ini.GetBool("Effects", "Enabled", true),
                CatalogPath = ResolvePath(baseDir, ini.Get("Effects", "CatalogPath", "default.mem.effects.json"))
            },
            FrontendFeedback = new FrontendFeedbackConfig
            {
                StartSelectPulseOnPanelChange = ini.GetBool("FrontendFeedback", "StartSelectPulseOnPanelChange", true),
                StartSelectPulseColor = ini.Get("FrontendFeedback", "StartSelectPulseColor", "ORANGE"),
                StartSelectPulseOffColor = ini.Get("FrontendFeedback", "StartSelectPulseOffColor", "BLACK"),
                StartSelectPulseMs = Math.Max(0, ini.GetInt("FrontendFeedback", "StartSelectPulseMs", 140))
            },
            Hardware = new HardwareConfig
            {
                PanelPlayers = Math.Max(1, ini.GetInt("Hardware", "PanelPlayers", 1)),
                Strips = ini.GetInt("Hardware", "Strips", 0),
                Circles = ini.GetInt("Hardware", "Circles", 0),
                Matrices = ini.GetInt("Hardware", "Matrices", 0),
                Joysticks = ini.GetInt("Hardware", "Joysticks", 0)
            },
            Senders = senders,
            PlayerRouting = playerRouting,
            TargetRouting = ini.Section("TargetRouting"),
            Templates = CommandTemplates.Load(ini)
        };
    }

    private static string ResolvePath(string baseDir, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(baseDir, value));
    }

    private static string DecodeLineEnding(string value)
    {
        return value.Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal);
    }
}

public sealed class ApiExposeConfig
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "ws://127.0.0.1:12345";
    public string FrontendPath { get; init; } = "/ws/frontend";
    public string PanelPath { get; init; } = "/ws/panel";
    public string IngamePath { get; init; } = "/ws/ingame";
    public string ArcadePath { get; init; } = "/ws/arcade";
    public string HiscorePath { get; init; } = "/ws/hiscore";
    public int DefaultPlayer { get; init; } = 1;

    public Uri BuildUri(string path)
    {
        return new Uri(BaseUrl.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path));
    }
}

public sealed class EffectsConfig
{
    public bool Enabled { get; init; } = true;
    public string CatalogPath { get; init; } = "default.mem.effects.json";
}

public sealed class FrontendFeedbackConfig
{
    public bool StartSelectPulseOnPanelChange { get; init; } = true;
    public string StartSelectPulseColor { get; init; } = "ORANGE";
    public string StartSelectPulseOffColor { get; init; } = "BLACK";
    public int StartSelectPulseMs { get; init; } = 140;
}

public sealed class HardwareConfig
{
    public int PanelPlayers { get; init; } = 1;
    public int Strips { get; init; }
    public int Circles { get; init; }
    public int Matrices { get; init; }
    public int Joysticks { get; init; }

    /// <summary>Used when no [Hardware] declaration is loaded (e.g. unit tests) so nothing is filtered.</summary>
    public static HardwareConfig AllEnabled { get; } = new() { PanelPlayers = 99, Strips = 99, Circles = 99, Matrices = 99, Joysticks = 99 };
}

public sealed class CommandSenderConfig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public int Player { get; init; }
    public string Executable { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Mode { get; init; } = "daemon";
    public bool UseStdIn { get; init; } = true;
    public string LineEnding { get; init; } = "\n";
    public bool DryRun { get; init; }
    public int StartupDelayMs { get; init; } = 1500;

    /// <summary>Max number of pending commands buffered for this sender before the oldest is dropped.</summary>
    public int QueueCapacity { get; init; } = 16;

    /// <summary>Commands waiting longer than this are skipped instead of sent, so the firmware never falls further behind.</summary>
    public int MaxQueueAgeMs { get; init; } = 150;

    /// <summary>Minimum delay between two commands sent to this sender, matching the serial bridge's own pacing.</summary>
    public int SendIntervalMs { get; init; } = 10;
}

public sealed class CommandTemplates
{
    public string SetOutput { get; init; } = "SET {target} {color}";
    public string SetSlot { get; init; } = "SLOT {slot} {color}";
    public string SetSystem { get; init; } = "{target} {state}";
    public string Clear { get; init; } = "CLEAR";
    public string All { get; init; } = "ALL {color}";
    public string Batch { get; init; } = "BATCH {items}";
    public string MatrixScore { get; init; } = "MATRIXSCORE {target} {value} {color}";
    public string MatrixText { get; init; } = "MATRIXTEXT {target} {color} {text}";
    public string Flash { get; init; } = "FLASH {target} {color} {durationMs}";

    public static CommandTemplates Load(IniDocument ini)
    {
        return new CommandTemplates
        {
            SetOutput = ini.Get("CommandTemplates", "SetOutput", "SET {target} {color}"),
            SetSlot = ini.Get("CommandTemplates", "SetSlot", "SLOT {slot} {color}"),
            SetSystem = ini.Get("CommandTemplates", "SetSystem", "{target} {state}"),
            Clear = ini.Get("CommandTemplates", "Clear", "CLEAR"),
            All = ini.Get("CommandTemplates", "All", "ALL {color}"),
            Batch = ini.Get("CommandTemplates", "Batch", "BATCH {items}"),
            MatrixScore = ini.Get("CommandTemplates", "MatrixScore", "MATRIXSCORE {target} {value} {color}"),
            MatrixText = ini.Get("CommandTemplates", "MatrixText", "MATRIXTEXT {target} {color} {text}"),
            Flash = ini.Get("CommandTemplates", "Flash", "FLASH {target} {color} {durationMs}")
        };
    }
}
