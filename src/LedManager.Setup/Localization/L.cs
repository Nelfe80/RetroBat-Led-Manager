using System.IO;
using System.Text.RegularExpressions;

namespace LedManager.Setup.Localization;

/// <summary>
/// Two-language UI strings, chosen once at startup: `--lang fr|en` argument, then
/// the RetroBat/EmulationStation language (es_settings.cfg), then the Windows UI
/// culture. Call sites keep both texts inline: L.T("texte", "text").
/// </summary>
public static class L
{
    public static bool French { get; private set; } = ResolveFrench();

    public static string T(string fr, string en) => French ? fr : en;

    /// <summary>Runtime switch (FR/EN button): views are rebuilt by the caller.</summary>
    public static void Set(bool french) => French = french;

    private static bool ResolveFrench()
    {
        // explicit user choice first (LedManager.ini [Setup] Language=fr|en)
        if (TryReadIniLanguage() is { } iniLanguage)
        {
            return iniLanguage.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
        }

        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1].StartsWith("fr", StringComparison.OrdinalIgnoreCase);
            }

            if (args[i].StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i]["--lang=".Length..].StartsWith("fr", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (TryReadEsLanguage() is { } esLanguage)
        {
            return esLanguage.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
        }

        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("fr", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadIniLanguage()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var ini = Path.Combine(dir.FullName, "LedManager.ini");
                if (File.Exists(ini))
                {
                    var match = Regex.Match(File.ReadAllText(ini),
                        @"^\s*Language\s*=\s*([A-Za-z-]+)", RegexOptions.Multiline);
                    return match.Success ? match.Groups[1].Value : null;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // fall through to the other sources
        }

        return null;
    }

    /// <summary>RetroBat root is two levels above the plugin folder.</summary>
    private static string? TryReadEsLanguage()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var settings = Path.Combine(dir.FullName, "emulationstation", ".emulationstation", "es_settings.cfg");
                if (File.Exists(settings))
                {
                    var match = Regex.Match(File.ReadAllText(settings),
                        "name=\"Language\"\\s+value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    return match.Success ? match.Groups[1].Value : null;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // fall back to the Windows culture
        }

        return null;
    }
}
