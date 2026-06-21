using System.Globalization;

namespace LedManager.Core.Ini;

public sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    private IniDocument(Dictionary<string, Dictionary<string, string>> sections)
    {
        _sections = sections;
    }

    public IEnumerable<string> Sections => _sections.Keys;

    public static IniDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("INI file not found.", path);
        }

        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "Default";
        sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                sections.TryAdd(current, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                continue;
            }

            var idx = line.IndexOf('=');
            if (idx < 0)
            {
                continue;
            }

            sections[current][line[..idx].Trim()] = Unquote(line[(idx + 1)..].Trim());
        }

        return new IniDocument(sections);
    }

    public string Get(string section, string key, string fallback = "")
    {
        return _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
            ? value
            : fallback;
    }

    public bool GetBool(string section, string key, bool fallback = false)
    {
        var value = Get(section, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" or "enabled" => true,
            "0" or "false" or "no" or "off" or "disabled" => false,
            _ => fallback
        };
    }

    public int GetInt(string section, string key, int fallback = 0)
    {
        return int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public IReadOnlyDictionary<string, string> Section(string section)
    {
        return _sections.TryGetValue(section, out var values)
            ? values
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuote = !inQuote;
            }

            if (!inQuote && (line[i] == ';' || line[i] == '#'))
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string Unquote(string value)
    {
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }
}
