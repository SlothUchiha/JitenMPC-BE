using System.Diagnostics;
using System.Text.Json;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public sealed class SubtitleTrackService
{
    private static readonly HashSet<string> TextCodecs = new(StringComparer.OrdinalIgnoreCase) { "ass", "ssa", "subrip", "webvtt", "mov_text", "text" };
    private readonly FileLogger _log;
    public SubtitleTrackService(FileLogger log) => _log = log;

    public async Task<List<SubtitleStreamInfo>> ProbeAsync(string media, string ffprobe)
    {
        if (!File.Exists(media) || !File.Exists(ffprobe)) return [];
        _log.Write("Probing embedded subtitle streams for " + media);
        var output = await RunCaptureAsync(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", media]);
        using var doc = JsonDocument.Parse(output);
        var result = new List<SubtitleStreamInfo>();
        if (!doc.RootElement.TryGetProperty("streams", out var streams)) return result;
        foreach (var st in streams.EnumerateArray())
        {
            if (!st.TryGetProperty("codec_type", out var type) || type.GetString() != "subtitle") continue;
            var index = st.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1;
            var codec = st.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
            var lang = "und"; var title = "";
            if (st.TryGetProperty("tags", out var tags))
            {
                if (tags.TryGetProperty("language", out var l) && !string.IsNullOrWhiteSpace(l.GetString())) lang = l.GetString()!;
                if (tags.TryGetProperty("title", out var t)) title = t.GetString() ?? "";
            }
            var isDefault = false; var isForced = false;
            if (st.TryGetProperty("disposition", out var disp))
            {
                if (disp.TryGetProperty("default", out var d)) isDefault = d.GetInt32() == 1;
                if (disp.TryGetProperty("forced", out var f)) isForced = f.GetInt32() == 1;
            }
            result.Add(new SubtitleStreamInfo
            {
                Index = index, Codec = codec, Language = lang, Title = title,
                IsDefault = isDefault, IsForced = isForced, IsText = TextCodecs.Contains(codec)
            });
        }
        _log.Write($"ffprobe found {result.Count} subtitle stream(s): {string.Join(" || ", result.Select(x => x.Display))}");
        return result;
    }

    public SubtitleStreamInfo? ChooseAuto(IEnumerable<SubtitleStreamInfo> streams)
        => streams.Where(s => s.IsText)
            .Select(s => (s, score: Score(s)))
            .OrderByDescending(x => x.score).ThenBy(x => x.s.Index)
            .Select(x => x.s).FirstOrDefault();

    private static int Score(SubtitleStreamInfo s)
    {
        var score = 0;
        if (s.Language is "jpn" or "ja" or "jp") score += 100;
        if (s.Title.Contains("japanese", StringComparison.OrdinalIgnoreCase) || s.Title.Contains("jpn", StringComparison.OrdinalIgnoreCase)) score += 70;
        if (ContainsAny(s.Title, "full", "dialog", "dialogue", "caption")) score += 15;
        if (ContainsAny(s.Title, "sign", "song", "forced")) score -= 30;
        if (s.IsDefault) score += 5;
        if (s.IsForced) score -= 10;
        return score;
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    public string FindExternal(string media)
    {
        if (!File.Exists(media)) return "";
        var dir = Path.GetDirectoryName(media)!;
        var basename = Path.GetFileNameWithoutExtension(media);
        foreach (var suffix in new[] { ".ja.ass", ".jpn.ass", ".jp.ass", ".ja.ssa", ".jpn.ssa", ".jp.ssa", ".ja.srt", ".jpn.srt", ".jp.srt", ".ass", ".ssa", ".srt" })
        {
            var path = Path.Combine(dir, basename + suffix);
            if (File.Exists(path)) return path;
        }
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(p => new[] { ".ass", ".ssa", ".srt" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .Where(p => Path.GetFileNameWithoutExtension(p).StartsWith(basename, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => RegexJapanese(Path.GetFileNameWithoutExtension(p)) ? 0 : 1).ThenBy(Path.GetFileName)
                .FirstOrDefault() ?? "";
        }
        catch { return ""; }
    }

    private static bool RegexJapanese(string name)
        => System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)(^|[._ -])(jpn|ja|jp|japanese)([._ -]|$)");

    public async Task<string> ExtractAsync(string media, SubtitleStreamInfo stream, string ffmpeg)
    {
        if (!stream.IsText) return "";
        var ext = stream.Codec is "ass" or "ssa" ? ".ass" : ".srt";
        var dir = Path.Combine(Path.GetTempPath(), "JitenMPC-BE");
        Directory.CreateDirectory(dir);
        var safeHash = unchecked((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(media));
        var output = Path.Combine(dir, $"subtitle-{safeHash}-{stream.Index}{ext}");
        _log.Write($"Extracting {stream.Display} to {output}");
        var (exit, stderr) = await RunAsync(ffmpeg, ["-y", "-v", "error", "-i", media, "-map", $"0:{stream.Index}", output]);
        if (exit == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
        {
            _log.Write($"Extracted subtitle stream #{stream.Index} to {output}");
            return output;
        }
        _log.Write("ffmpeg extraction failed: " + stderr);
        return "";
    }

    private static async Task<string> RunCaptureAsync(string exe, IReadOnlyList<string> args)
    {
        var (exit, stdout, stderr) = await RunBothAsync(exe, args);
        if (exit != 0) throw new InvalidOperationException(stderr.Length > 0 ? stderr : $"{Path.GetFileName(exe)} exited with code {exit}");
        return stdout;
    }

    private static async Task<(int exit, string stderr)> RunAsync(string exe, IReadOnlyList<string> args)
    {
        var (exit, _, stderr) = await RunBothAsync(exe, args);
        return (exit, stderr);
    }

    private static async Task<(int exit, string stdout, string stderr)> RunBothAsync(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + exe);
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }
}
