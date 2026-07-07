using System.IO;
using LedManager.Core.Ini;

namespace LedManager.Setup.Serial;

/// <summary>
/// Rewrites [GPIO:P1] so the wiring matches the expected layout, in software rather
/// than by re-soldering. If lighting SLOT 3 turns on the button the user identifies as
/// B4, then B3 must be driven by B4's old GPIOs: newGpio[lit] = oldGpio[clicked].
/// </summary>
public static class GpioMappingFixer
{
    public sealed record Result(bool Success, string Message);

    /// <param name="map">lit key → clicked key. Keys are "B3", "START", "SELECT".</param>
    public static Result Apply(string pluginRoot, string sender, IReadOnlyDictionary<string, string> map)
    {
        var iniPath = Path.Combine(pluginRoot, "PicoCommandSender.ini");
        if (!File.Exists(iniPath))
        {
            return new Result(false, "PicoCommandSender.ini introuvable.");
        }

        var gpioSection = $"GPIO:{sender}";
        var current = IniDocument.Load(iniPath).Section(gpioSection);

        var toFix = map.Where(kv => !kv.Key.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (toFix.Count == 0)
        {
            return new Result(true, "Aucune correction nécessaire : le câblage correspond déjà.");
        }

        // Safety: the clicked keys must be a permutation of the lit keys (a clean swap),
        // otherwise correcting would create ambiguous or missing GPIO assignments.
        var litKeys = toFix.Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clickedKeys = toFix.Select(kv => kv.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (clickedKeys.Count != toFix.Count || !litKeys.SetEquals(clickedKeys))
        {
            return new Result(false,
                "Correction impossible automatiquement : les correspondances ne forment pas un simple échange "
                + "(un bouton cliqué en double, ou un bouton hors du groupe). Vérifiez le câblage et relancez le test.");
        }

        // Every clicked key must have a known GPIO to move over.
        foreach (var (_, clicked) in toFix)
        {
            if (!current.ContainsKey(clicked))
            {
                return new Result(false, $"GPIO du bouton {clicked} introuvable dans [{gpioSection}].");
            }
        }

        var newValues = toFix.ToDictionary(kv => kv.Key, kv => current[kv.Value], StringComparer.OrdinalIgnoreCase);

        var editor = IniEditor.Load(iniPath);
        foreach (var (key, value) in newValues)
        {
            editor.Set(gpioSection, key, value);
        }

        editor.Save();

        var lines = string.Join(", ", newValues.Select(kv => $"{kv.Key}→[{kv.Value}]"));
        return new Result(true, $"{newValues.Count} correspondance(s) GPIO corrigée(s) dans [{gpioSection}] : {lines}. "
            + "Sauvegarde .bak créée. Relancez le test pour confirmer.");
    }
}
