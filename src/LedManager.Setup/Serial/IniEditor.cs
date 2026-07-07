using System.IO;
using System.Text;

namespace LedManager.Setup.Serial;

/// <summary>
/// Line-preserving INI editor: keeps every comment, blank line and ordering, and only
/// rewrites the keys it is told to. Used to update PicoCommandSender.ini from the
/// wizard without destroying its bilingual comments. Backs up to .bak before saving.
/// </summary>
public sealed class IniEditor
{
    private readonly List<string> _lines;
    private readonly string _path;

    private IniEditor(string path, List<string> lines)
    {
        _path = path;
        _lines = lines;
    }

    public static IniEditor Load(string path)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        return new IniEditor(path, lines);
    }

    /// <summary>
    /// Sets a key inside a section, preserving any inline comment already on that line.
    /// Creates the section (at the end) and/or the key if missing.
    /// </summary>
    public void Set(string section, string key, string value)
    {
        var sectionStart = FindSectionLine(section);
        if (sectionStart < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0)
            {
                _lines.Add("");
            }

            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        var sectionEnd = FindSectionEnd(sectionStart);
        for (var i = sectionStart + 1; i < sectionEnd; i++)
        {
            var trimmed = _lines[i].TrimStart();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#') || trimmed.Length == 0)
            {
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var comment = ExtractInlineComment(_lines[i]);
                _lines[i] = $"{key}={value}{comment}";
                return;
            }
        }

        // Key not found: insert at the end of the section (before trailing blank lines).
        var insertAt = sectionEnd;
        while (insertAt - 1 > sectionStart && _lines[insertAt - 1].Trim().Length == 0)
        {
            insertAt--;
        }

        _lines.Insert(insertAt, $"{key}={value}");
    }

    public void Save()
    {
        if (File.Exists(_path))
        {
            try
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }

        File.WriteAllText(_path, string.Join(Environment.NewLine, _lines) + Environment.NewLine, new UTF8Encoding(false));
    }

    private int FindSectionLine(string section)
    {
        var header = $"[{section}]";
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSectionEnd(int sectionStart)
    {
        for (var i = sectionStart + 1; i < _lines.Count; i++)
        {
            if (_lines[i].TrimStart().StartsWith('['))
            {
                return i;
            }
        }

        return _lines.Count;
    }

    private static string ExtractInlineComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && (line[i] == ';' || line[i] == '#'))
            {
                return "  " + line[i..];
            }
        }

        return "";
    }
}
