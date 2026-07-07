using System.IO.Ports;
using LedManager.Setup.Localization;

namespace LedManager.Setup.Serial;

public sealed record PicoDetectionResult(
    bool Found,
    string? PortName,
    string? FirmwareName,
    string? FirmwareVersion,
    IReadOnlyList<string> ScannedPorts,
    string Message);

/// <summary>Scans serial ports for a responding Pico firmware. The configured port
/// (from PicoCommandSender.ini) is tried first for speed.</summary>
public static class PicoDetector
{
    public static async Task<PicoDetectionResult> DetectAsync(string? preferredPort = null)
    {
        return await Task.Run(() =>
        {
            var ports = SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ordered = new List<string>();
            if (!string.IsNullOrWhiteSpace(preferredPort) && ports.Contains(preferredPort, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(ports.First(p => p.Equals(preferredPort, StringComparison.OrdinalIgnoreCase)));
            }

            ordered.AddRange(ports.Where(p => !ordered.Contains(p, StringComparer.OrdinalIgnoreCase)));

            if (ordered.Count == 0)
            {
                return new PicoDetectionResult(false, null, null, null, ordered,
                    L.T("Aucun port série détecté. Le Pico est-il branché en USB (câble data) ?",
                        "No serial port detected. Is the Pico plugged in over USB (data cable)?"));
            }

            foreach (var port in ordered)
            {
                using var link = PicoLink.TryOpen(port);
                if (link is not null)
                {
                    var version = link.FirmwareVersion is null ? "" : $" {link.FirmwareName} {link.FirmwareVersion}";
                    return new PicoDetectionResult(true, port, link.FirmwareName, link.FirmwareVersion, ordered,
                        L.T($"Pico détecté sur {port}{version}.", $"Pico detected on {port}{version}."));
                }
            }

            return new PicoDetectionResult(false, null, null, null, ordered,
                L.T($"Aucun Pico ne répond sur les ports testés ({string.Join(", ", ordered)}). " +
                    "Firmware installé ? LedManager arrêté (il occupe le port) ?",
                    $"No Pico answers on the tested ports ({string.Join(", ", ordered)}). " +
                    "Firmware installed? LedManager stopped (it holds the port)?"));
        });
    }
}
