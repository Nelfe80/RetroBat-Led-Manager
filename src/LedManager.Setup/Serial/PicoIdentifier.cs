using LedManager.Setup.Localization;

namespace LedManager.Setup.Serial;

/// <summary>
/// Physically identifies a Pico: blinks every button of its panel white a few times
/// so the user knows which cabinet/panel answers to the selected sender. Goes through
/// PicoCommandSender (the honest runtime pipeline), so the firmware GPIO profile is
/// initialized first - the blink can take a few seconds on conservative configs.
/// </summary>
public static class PicoIdentifier
{
    public static async Task<string> BlinkAsync(string pluginRoot, string senderId)
    {
        if (LedManagerProcess.IsRunning())
        {
            return L.T("LedManager occupe le port série - impossible de faire clignoter ce Pico maintenant.",
                "LedManager holds the serial port - cannot blink this Pico right now.");
        }

        using var sender = PicoSenderHost.Start(pluginRoot, senderId);
        if (sender is null)
        {
            return L.T("PicoCommandSender.exe introuvable à la racine du plugin.",
                "PicoCommandSender.exe not found at the plugin root.");
        }

        await sender.WaitForReadyAsync(TimeSpan.FromSeconds(30));
        if (!sender.IsAlive)
        {
            return L.T($"Le Pico {senderId} ne répond pas (port occupé ou débranché ?).",
                $"Pico {senderId} does not answer (port busy or unplugged?).");
        }

        for (var i = 0; i < 3; i++)
        {
            sender.Send("ALL WHITE");
            await Task.Delay(280);
            sender.Send("CLEAR");
            await Task.Delay(220);
        }

        return L.T($"Le panneau du Pico {senderId} vient de clignoter 3 fois.",
            $"Pico {senderId}'s panel just blinked 3 times.");
    }
}
