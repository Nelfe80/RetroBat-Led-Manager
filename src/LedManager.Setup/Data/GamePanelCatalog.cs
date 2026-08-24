using System.IO;
using System.Text.Json;
using LedManager.Core.Runtime;

namespace LedManager.Setup.Data;

/// <summary>
/// Arcade game panels: lists the curated per-game dynpanels
/// (APIExpose\resources\dynpanels\games) and resolves a game's slot colors through
/// the EXACT runtime pipeline (PanelState.EnrichFromDynpanel) - what you see in the
/// editor is what LedManager lights.
/// </summary>
public sealed class GamePanelCatalog
{
    public sealed record GamePanel(
        string System,
        string Rom,
        string GameName,
        IReadOnlyDictionary<int, (string Color, string Label)> BySlot)
    {
        /// <summary>
        /// Folder used for the override patch. The curated dynpanels say "mame" but
        /// the RetroBat ES system (and the media tree) is "arcade" - the runtime
        /// accepts both, the setup writes the canonical one.
        /// </summary>
        public string OverrideSystem => System.Equals("mame", StringComparison.OrdinalIgnoreCase) ? "arcade" : System;
    }

    private readonly string _pluginRoot;
    private readonly string _gamesDir;
    private IReadOnlyList<string>? _games;
    private IReadOnlyDictionary<string, string>? _systemByGame;

    public GamePanelCatalog(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        _gamesDir = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "resources", "dynpanels", "games"));
    }

    public bool Available => Directory.Exists(_gamesDir);

    public IReadOnlyList<string> ListGames()
    {
        if (_games != null)
        {
            return _games;
        }

        if (!Available)
        {
            return _games = Array.Empty<string>();
        }

        return _games = Directory.EnumerateFiles(_gamesDir, "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// rom → override system (mame folded into "arcade"), built by scanning every
    /// game json once. ~3300 small files: call from a background thread, the result
    /// is cached for the session.
    /// </summary>
    public IReadOnlyDictionary<string, string> ListGamesWithSystems()
    {
        if (_systemByGame != null)
        {
            return _systemByGame;
        }

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Available)
        {
            foreach (var path in Directory.EnumerateFiles(_gamesDir, "*.json", SearchOption.AllDirectories))
            {
                var rom = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(rom) || index.ContainsKey(rom))
                {
                    continue;
                }

                var system = "mame";
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("system", out var sys) && sys.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(sys.GetString()))
                    {
                        system = sys.GetString()!.Trim().ToLowerInvariant();
                    }
                }
                catch
                {
                    // unreadable json: keep the default system rather than hiding the game
                }

                index[rom] = system.Equals("mame", StringComparison.OrdinalIgnoreCase) ? "arcade" : system;
            }
        }

        return _systemByGame = index;
    }

    /// <summary>Resolves a game's pack colors (no overrides) for the given layout.</summary>
    public GamePanel? Load(string rom, string layoutId)
    {
        var path = FindGameFile(rom);
        if (path == null)
        {
            return null;
        }

        string system = "mame", gameName = rom;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("system", out var sys) && sys.ValueKind == JsonValueKind.String)
            {
                system = sys.GetString() ?? system;
            }

            if (doc.RootElement.TryGetProperty("game_name", out var name) && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                gameName = name.GetString()!;
            }
        }
        catch
        {
            return null;
        }

        // same code path as the runtime: dynpanel enrichment keyed by the layout id
        var state = new PanelState { System = system, Rom = rom, LayoutId = layoutId }
            .EnrichFromDynpanel(_pluginRoot);

        var bySlot = new Dictionary<int, (string Color, string Label)>();
        foreach (var output in state.Outputs)
        {
            if (output.Slot is not { } slot || slot is < 1 or > 8)
            {
                continue;
            }

            if (output.Player is > 1)
            {
                continue; // the editor paints player 1
            }

            var color = string.IsNullOrWhiteSpace(output.Color) ? "BLACK" : output.Color.Trim().ToUpperInvariant();
            var label = output.Name ?? output.Function ?? output.Id ?? "";
            if (!bySlot.ContainsKey(slot) || bySlot[slot].Color == "BLACK")
            {
                bySlot[slot] = (color, label);
            }
        }

        return new GamePanel(system, rom, gameName, bySlot);
    }

    private string? FindGameFile(string rom)
    {
        var flat = Path.Combine(_gamesDir, rom + ".json");
        if (File.Exists(flat))
        {
            return flat;
        }

        return Available
            ? Directory.EnumerateFiles(_gamesDir, rom + ".json", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }
}
