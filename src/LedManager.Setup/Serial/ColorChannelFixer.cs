using System.IO;
using LedManager.Core.Ini;

namespace LedManager.Setup.Serial;

/// <summary>
/// Fixes the R,G,B wire order in [GPIO:P1]. The color test drives one channel at a
/// time (ALLPCT 100 0 0 → the pins the ini believes are "R"): if the user reports
/// seeing GREEN, the first pin of each triplet actually drives the green die. The
/// fix reorders every B* triplet so each position drives the color it claims.
/// Assumes the kit's uniform wiring across buttons (same wire colors everywhere).
/// </summary>
public static class ColorChannelFixer
{
    public sealed record Result(bool Success, string Message);

    /// <param name="seen">Colors reported for driven channels R, G, B — e.g. seen[0]
    /// is what lit up when the R channel was driven. Values: "R", "G" or "B".</param>
    public static Result Apply(string pluginRoot, string sender, IReadOnlyList<string> seen)
    {
        if (seen.Count != 3)
        {
            return new Result(false, "Test incomplet : trois réponses attendues.");
        }

        var normalized = seen.Select(s => s.ToUpperInvariant()).ToArray();
        if (!new HashSet<string>(normalized).SetEquals(new[] { "R", "G", "B" }))
        {
            return new Result(false,
                "Réponses incohérentes (une couleur vue deux fois). Deux fils peuvent être en court-circuit "
                + "ou une LED défaillante — refaites le test ou vérifiez le câblage.");
        }

        if (normalized[0] == "R" && normalized[1] == "G" && normalized[2] == "B")
        {
            return new Result(true, "L'ordre des canaux est déjà correct, rien à corriger.");
        }

        var iniPath = Path.Combine(pluginRoot, "PicoCommandSender.ini");
        if (!File.Exists(iniPath))
        {
            return new Result(false, "PicoCommandSender.ini introuvable.");
        }

        var gpioSection = $"GPIO:{sender}";
        var current = IniDocument.Load(iniPath).Section(gpioSection);

        // Driving ini-position i lights color seen[i] ⇒ the pin that really produces
        // color C sits at position indexOf(C, seen). New triplet = (pin(R), pin(G), pin(B)).
        var order = new[]
        {
            Array.IndexOf(normalized, "R"),
            Array.IndexOf(normalized, "G"),
            Array.IndexOf(normalized, "B")
        };

        var editor = IniEditor.Load(iniPath);
        var fixedKeys = new List<string>();
        foreach (var (key, value) in current)
        {
            var pins = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (pins.Length != 3)
            {
                continue; // START/SELECT single-pin outputs are not RGB
            }

            editor.Set(gpioSection, key, $"{pins[order[0]]},{pins[order[1]]},{pins[order[2]]}");
            fixedKeys.Add(key);
        }

        if (fixedKeys.Count == 0)
        {
            return new Result(false, $"Aucun triplet RGB trouvé dans [{gpioSection}].");
        }

        editor.Save();
        return new Result(true,
            $"Ordre des canaux corrigé sur {fixedKeys.Count} bouton(s) dans [{gpioSection}] "
            + $"(vu {normalized[0]}/{normalized[1]}/{normalized[2]} → réordonné). Sauvegarde .bak créée.");
    }
}
