using System.Globalization;
using System.Text.RegularExpressions;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public static partial class SubtitleParser
{
    [GeneratedRegex(@"^(\d+):(\d{2}):(\d{2})[.](\d{1,2})$")]
    private static partial Regex AssTimeRegex();
    [GeneratedRegex(@"^(\d{2}):(\d{2}):(\d{2}),(\d{3})$")]
    private static partial Regex SrtTimeRegex();
    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2},\d{3})")]
    private static partial Regex SrtRangeRegex();
    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssTagRegex();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    public static IReadOnlyList<SubtitleCue> LoadFile(string path)
    {
        var text = File.ReadAllText(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ass" or ".ssa" => ParseAss(text),
            ".srt" => ParseSrt(text),
            _ => throw new NotSupportedException("Unsupported subtitle format: " + Path.GetExtension(path))
        };
    }

    public static List<SubtitleCue> ParseAss(string content)
    {
        var cues = new List<SubtitleCue>();
        var inEvents = false;
        string[] format = [];
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Equals("[Events]", StringComparison.OrdinalIgnoreCase)) { inEvents = true; continue; }
            if (line.StartsWith('[') && !line.Equals("[Events]", StringComparison.OrdinalIgnoreCase)) { inEvents = false; continue; }
            if (!inEvents) continue;
            if (line.StartsWith("Format", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.IndexOf(':');
                if (colon >= 0) format = line[(colon + 1)..].Split(',').Select(x => x.Trim()).ToArray();
                continue;
            }
            if (!line.StartsWith("Dialogue", StringComparison.OrdinalIgnoreCase) || format.Length == 0) continue;
            var colon2 = line.IndexOf(':');
            if (colon2 < 0) continue;
            var parts = line[(colon2 + 1)..].TrimStart().Split(',', format.Length);
            if (parts.Length < format.Length) continue;
            var si = Array.FindIndex(format, x => x.Equals("Start", StringComparison.OrdinalIgnoreCase));
            var ei = Array.FindIndex(format, x => x.Equals("End", StringComparison.OrdinalIgnoreCase));
            var ti = Array.FindIndex(format, x => x.Equals("Text", StringComparison.OrdinalIgnoreCase));
            if (si < 0 || ei < 0 || ti < 0) continue;
            var start = ParseAssTime(parts[si]);
            var end = ParseAssTime(parts[ei]);
            var text = AssTagRegex().Replace(parts[ti], "").Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ").Trim();
            if (text.Length > 0 && end >= start) cues.Add(new SubtitleCue(start, end, text));
        }
        return cues.OrderBy(x => x.Start).ThenBy(x => x.End).ToList();
    }

    public static List<SubtitleCue> ParseSrt(string content)
    {
        var cues = new List<SubtitleCue>();
        foreach (var block in Regex.Split(content.Replace("\r\n", "\n"), "\\n\\s*\\n"))
        {
            var lines = block.Split('\n');
            if (lines.Length < 2) continue;
            Match? match = null;
            var timeIndex = -1;
            for (var i = 0; i < Math.Min(3, lines.Length); i++)
            {
                var m = SrtRangeRegex().Match(lines[i]);
                if (m.Success) { match = m; timeIndex = i; break; }
            }
            if (match is null) continue;
            var text = HtmlTagRegex().Replace(string.Join("\n", lines.Skip(timeIndex + 1)), "").Trim();
            if (text.Length > 0) cues.Add(new SubtitleCue(ParseSrtTime(match.Groups[1].Value), ParseSrtTime(match.Groups[2].Value), text));
        }
        return cues.OrderBy(x => x.Start).ThenBy(x => x.End).ToList();
    }

    public static SubtitleCue? CueAt(IReadOnlyList<SubtitleCue> cues, double position)
    {
        if (cues.Count == 0) return null;
        var lo = 0; var hi = cues.Count - 1; var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cues[mid].Start <= position) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (best < 0) return null;
        var active = new List<SubtitleCue>();
        for (var i = best; i >= 0; i--)
        {
            var c = cues[i];
            if (position - c.Start > 15) break;
            if (position >= c.Start && position <= c.End) active.Insert(0, c);
        }
        for (var i = best + 1; i < cues.Count; i++)
        {
            var c = cues[i];
            if (c.Start > position) break;
            if (position <= c.End) active.Add(c);
        }
        if (active.Count == 0) return null;
        if (active.Count == 1) return active[0];
        return new SubtitleCue(active.Min(c => c.Start), active.Max(c => c.End), string.Join("\n", active.Select(c => c.Text)));
    }

    private static double ParseAssTime(string value)
    {
        var m = AssTimeRegex().Match(value.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value) + int.Parse(m.Groups[4].Value) / 100.0 : 0;
    }

    private static double ParseSrtTime(string value)
    {
        var m = SrtTimeRegex().Match(value.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value) + int.Parse(m.Groups[4].Value) / 1000.0 : 0;
    }
}
