using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LedManager.Setup.Localization;
using LedManager.Setup.Serial;

namespace LedManager.Setup.Controls;

/// <summary>
/// Contextual "Contrôles" card embedded at the bottom of SystemsView and
/// GamesView: deploys the input configuration of the object being edited
/// (system → RetroArch remap, arcade game → MAME cfg merge) through the
/// APIExpose endpoints, plus the matching bulk action. APIExpose does all the
/// writing with its guards (markers, registry, .bak, input-only merge, refusal
/// while MAME runs); the card pushes and reports in plain words.
/// </summary>
public sealed class ControlsDeployCard : UserControl
{
    private enum Scope { None, System, MameGame }

    private readonly string _baseUrl;
    private readonly string _mamePackDir;
    private readonly TextBlock _label;
    private readonly Button _deployOne;
    private readonly Button _deployAll;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private readonly TextBox _details;

    private Scope _scope = Scope.None;
    private string? _target;

    public ControlsDeployCard()
    {
        var pluginRoot = HardwareDescription.FindPluginRoot() ?? Directory.GetCurrentDirectory();
        _baseUrl = ApiExposeClient.ResolveBaseUrl(pluginRoot);
        _mamePackDir = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "resources", "controls", "mame"));

        var header = new TextBlock
        {
            Text = L.T("Contrôles", "Controls"),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Ui.Brush(Color.FromRgb(0xE8, 0xE8, 0xF0))
        };

        _label = new TextBlock
        {
            FontSize = 12,
            Foreground = Ui.Brush(Color.FromRgb(0xB8, 0xB8, 0xC6)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        };

        _deployOne = MakeButton();
        _deployOne.Click += (_, _) => _ = DeployAsync(single: true);
        _deployAll = MakeButton();
        _deployAll.Click += (_, _) => _ = DeployAsync(single: false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_deployOne);
        buttons.Children.Add(_deployAll);

        _progress = new ProgressBar
        {
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };

        _status = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.Brush(Color.FromRgb(0xB8, 0xB8, 0xC6)),
            TextWrapping = TextWrapping.Wrap
        };

        _details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            MaxHeight = 140,
            Margin = new Thickness(0, 6, 0, 0),
            Background = Ui.Brush(Color.FromRgb(0x16, 0x16, 0x20)),
            Foreground = Ui.Brush(Color.FromRgb(0xB8, 0xB8, 0xC6)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Visibility = Visibility.Collapsed
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_label);
        stack.Children.Add(buttons);
        stack.Children.Add(_progress);
        stack.Children.Add(_status);
        stack.Children.Add(_details);

        Content = new Border
        {
            Background = Ui.Brush(Color.FromRgb(0x1D, 0x1D, 0x2A)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 14, 0, 0),
            Child = stack
        };

        ShowNone(L.T("Choisissez une cible pour voir ses contrôles.", "Pick a target to see its controls."));
    }

    /// <summary>Card follows the system being edited: RetroArch remap deployment.</summary>
    public void ShowSystem(string systemId)
    {
        _scope = Scope.System;
        _target = systemId;
        _label.Text = L.T($"Configuration manette RetroArch du système « {systemId} » — suit le gabarit choisi et la cartographie de votre panel.",
            $"RetroArch controller configuration of system \"{systemId}\" — follows the chosen template and your panel's cartography.");
        _deployOne.Content = L.T("Mettre à jour ce système", "Update this system");
        _deployAll.Content = L.T("Tous les systèmes", "All systems");
        _deployOne.IsEnabled = true;
        _deployAll.IsEnabled = true;
        ResetReport();
    }

    /// <summary>Card follows the arcade game being edited: MAME cfg merge deployment.</summary>
    public void ShowMameGame(string rom)
    {
        _scope = Scope.MameGame;
        _target = rom;
        var inPack = File.Exists(Path.Combine(_mamePackDir, rom + ".cfg"));
        _label.Text = inPack
            ? L.T($"Configuration des boutons MAME du jeu « {rom} » — vos réglages personnels sont conservés. MAME doit être fermé.",
                $"MAME button configuration of game \"{rom}\" — your personal settings are preserved. MAME must be closed.")
            : L.T($"Le jeu « {rom} » n'a pas de configuration dans le pack.",
                $"Game \"{rom}\" has no configuration in the pack.");
        _deployOne.Content = L.T("Mettre à jour ce jeu", "Update this game");
        _deployAll.Content = L.T("Tous les jeux", "All games");
        _deployOne.IsEnabled = inPack;
        _deployAll.IsEnabled = true;
        ResetReport();
    }

    public void ShowNone(string hint)
    {
        _scope = Scope.None;
        _target = null;
        _label.Text = hint;
        _deployOne.Content = L.T("Mettre à jour", "Update");
        _deployAll.Content = L.T("Tout mettre à jour", "Update all");
        _deployOne.IsEnabled = false;
        _deployAll.IsEnabled = false;
        ResetReport();
    }

    private void ResetReport()
    {
        _status.Text = "";
        _details.Text = "";
        _details.Visibility = Visibility.Collapsed;
    }

    private static Button MakeButton()
    {
        return new Button { Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
    }

    private async Task DeployAsync(bool single)
    {
        if (_scope == Scope.None || (single && _target is null))
        {
            return;
        }

        var oneEnabled = _deployOne.IsEnabled;
        _deployOne.IsEnabled = false;
        _deployAll.IsEnabled = false;
        _status.Text = L.T("Mise à jour en cours…", "Updating…");
        _details.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        try
        {
            if (_scope == Scope.MameGame && !single)
            {
                await DeployAllMameChunkedAsync();
            }
            else
            {
                var path = _scope == Scope.System
                    ? "/api/v1/panels/controls/remaps/deploy" + (single ? $"?system={_target}" : "")
                    : $"/api/v1/panels/controls/mamecfg/deploy?rom={_target}";
                var (ok, body) = await ApiExposeClient.PostAsync(_baseUrl, path);
                var (headline, details) = DescribeReport(ok, body);
                _status.Text = headline;
                _details.Text = details;
                _details.Visibility = string.IsNullOrWhiteSpace(details) ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            _progress.IsIndeterminate = false;
            _deployOne.IsEnabled = oneEnabled;
            _deployAll.IsEnabled = true;
        }
    }

    /// <summary>
    /// Whole-pack MAME deployment, chunked so the user sees a real progress bar and
    /// the games being updated instead of a frozen screen for ~3000 files.
    /// </summary>
    private async Task DeployAllMameChunkedAsync()
    {
        const int chunkSize = 200;
        var offset = 0;
        int written = 0, merged = 0, upToDate = 0, failed = 0, packTotal = -1;
        var changedLines = new List<string>();

        while (true)
        {
            var (ok, body) = await ApiExposeClient.PostAsync(_baseUrl, $"/api/v1/panels/controls/mamecfg/deploy?offset={offset}&limit={chunkSize}");
            if (!ok)
            {
                var (headline, _) = DescribeReport(ok, body);
                _status.Text = headline;
                return;
            }

            int processed;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                int Count(string name) => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
                processed = Count("total");
                written += Count("written");
                merged += Count("merged");
                upToDate += Count("upToDate");
                failed += Count("failed");
                packTotal = Count("packTotal");

                if (root.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in changes.EnumerateArray())
                    {
                        var id = item.TryGetProperty("rom", out var r) ? r.GetString() : "?";
                        var status = item.TryGetProperty("status", out var st) ? st.GetString() : "";
                        changedLines.Add($"{id,-20} {DescribeStatus(status)}");
                    }
                }
            }
            catch
            {
                _status.Text = L.T("La mise à jour a échoué (réponse illisible).", "The update failed (unreadable response).");
                return;
            }

            offset += processed;
            if (packTotal > 0)
            {
                _progress.IsIndeterminate = false;
                _progress.Maximum = packTotal;
                _progress.Value = Math.Min(offset, packTotal);
            }

            var lastChanged = changedLines.Count > 0 ? changedLines[^1].Split(' ')[0] : null;
            _status.Text = L.T($"Mise à jour des jeux… {Math.Min(offset, Math.Max(packTotal, offset))}/{packTotal}",
                    $"Updating games… {Math.Min(offset, Math.Max(packTotal, offset))}/{packTotal}")
                + (lastChanged is null ? "" : L.T($" — dernier modifié : {lastChanged}", $" — last changed: {lastChanged}"));

            if (changedLines.Count > 0)
            {
                _details.Text = L.T("Ce qui a changé :", "What changed:") + Environment.NewLine
                    + string.Join(Environment.NewLine, changedLines.TakeLast(300));
                _details.Visibility = Visibility.Visible;
            }

            if (processed == 0 || (packTotal > 0 && offset >= packTotal))
            {
                break;
            }
        }

        _status.Text = L.T($"{packTotal} jeux vérifiés : {written} installés, {merged} mis à jour (réglages conservés), {upToDate} déjà à jour" + (failed > 0 ? $", {failed} en échec." : "."),
            $"{packTotal} games checked: {written} installed, {merged} updated (settings preserved), {upToDate} already up to date" + (failed > 0 ? $", {failed} failed." : "."));
        if (changedLines.Count == 0)
        {
            _details.Text = L.T("Aucun changement : tout était déjà à jour.", "No change: everything was already up to date.");
            _details.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Turns the API report into plain words: a one-line outcome, plus the
    /// detail of what changed for batch runs.</summary>
    private static (string Headline, string Details) DescribeReport(bool ok, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var message))
            {
                var text = message.GetString() ?? "";
                if (text.Contains("MAME is running", StringComparison.OrdinalIgnoreCase))
                {
                    return (L.T("MAME est ouvert : fermez-le puis relancez la mise à jour (il réécrit les fichiers en quittant un jeu).",
                        "MAME is open: close it and run the update again (it rewrites files when a game exits)."), "");
                }

                return (text, "");
            }

            int Count(string name) => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
            var total = Count("total");
            var written = Count("written");
            var merged = Count("merged");
            var upToDate = Count("upToDate");
            var failed = Count("failed");
            var isMameReport = root.TryGetProperty("merged", out _);
            var items = root.TryGetProperty("items", out var i) ? i : root.TryGetProperty("changes", out var c) ? c : default;

            if (total == 1)
            {
                return (DescribeSingle(isMameReport, items), "");
            }

            var headline = isMameReport
                ? L.T($"{total} jeux vérifiés : {written} installés, {merged} mis à jour (réglages conservés), {upToDate} déjà à jour" + (failed > 0 ? $", {failed} en échec." : "."),
                    $"{total} games checked: {written} installed, {merged} updated (settings preserved), {upToDate} already up to date" + (failed > 0 ? $", {failed} failed." : "."))
                : L.T($"{total} systèmes vérifiés : {written} mis à jour, {upToDate} déjà à jour, {total - written - upToDate} non concernés.",
                    $"{total} systems checked: {written} updated, {upToDate} already up to date, {total - written - upToDate} not applicable.");

            var lines = new List<string>();
            if (items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var status = item.TryGetProperty("status", out var st) ? st.GetString() : "";
                    if (status == "up-to-date")
                    {
                        continue;
                    }

                    var id = item.TryGetProperty("systemId", out var s) ? s.GetString() : item.TryGetProperty("rom", out var r) ? r.GetString() : "?";
                    var detail = item.TryGetProperty("detail", out var d) ? d.GetString() : "";
                    lines.Add($"{id,-20} {DescribeStatus(status)} {(string.IsNullOrWhiteSpace(detail) ? "" : $"({detail})")}");
                }
            }

            var details = lines.Count == 0
                ? ""
                : L.T("Ce qui a changé :", "What changed:") + Environment.NewLine + string.Join(Environment.NewLine, lines.Take(300));
            return (headline, details);
        }
        catch
        {
            return (ok ? L.T("Mise à jour terminée.", "Update finished.") : L.T("La mise à jour a échoué (APIExpose joignable ?).", "The update failed (is APIExpose reachable?)."), body);
        }
    }

    private static string DescribeSingle(bool isMameReport, JsonElement items)
    {
        string status = "", detail = "", id = "";
        if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
        {
            var item = items[0];
            status = item.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            detail = item.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
            id = item.TryGetProperty("systemId", out var s) ? s.GetString() ?? "" : item.TryGetProperty("rom", out var r) ? r.GetString() ?? "" : "";
        }
        else
        {
            status = "up-to-date"; // the MAME report lists only changes: empty = nothing to do
        }

        if (isMameReport)
        {
            return status switch
            {
                "written" => L.T("Configuration des boutons installée pour ce jeu.", "Button configuration installed for this game."),
                "merged" => L.T("Configuration mise à jour : vos réglages ont été conservés, les commandes du panel ont été ajoutées.",
                    "Configuration updated: your settings were preserved, the panel commands were added."),
                "missing" => L.T("Ce jeu n'a pas de configuration dans le pack.", "This game has no configuration in the pack."),
                "failed" => L.T($"Échec de la mise à jour : {detail}", $"Update failed: {detail}"),
                _ => L.T("Pas de déploiement : la configuration des boutons de ce jeu est déjà à jour.",
                    "No deployment: this game's button configuration is already up to date.")
            };
        }

        return status switch
        {
            "written" => L.T($"La configuration manette de « {id} » a été mise à jour.", $"The controller configuration of \"{id}\" was updated."),
            "up-to-date" => L.T($"Rien à faire : la configuration manette de « {id} » est déjà à jour.", $"Nothing to do: the controller configuration of \"{id}\" is already up to date."),
            "kept-user-file" => L.T("Fichier personnel détecté : il n'a pas été touché.", "Personal file detected: it was left untouched."),
            "failed" => L.T($"Échec de la mise à jour : {detail}", $"Update failed: {detail}"),
            _ => L.T($"Ce système n'est pas concerné ({detail}).", $"This system is not applicable ({detail}).")
        };
    }

    private static string DescribeStatus(string? status)
    {
        return status switch
        {
            "written" => L.T("mis à jour", "updated"),
            "merged" => L.T("mis à jour, réglages conservés", "updated, settings preserved"),
            "missing" => L.T("absent du pack", "not in pack"),
            "failed" => L.T("échec", "failed"),
            "kept-user-file" => L.T("fichier personnel, non touché", "personal file, untouched"),
            "skipped" => L.T("non concerné", "not applicable"),
            _ => status ?? ""
        };
    }
}
