using System.Diagnostics;
using System.Globalization;
using System.Text;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public sealed class MiningMediaService
{
    private readonly FileLogger _log;
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "JitenMPC-BE", "mining");

    public MiningMediaService(FileLogger log)
    {
        _log = log;
        Directory.CreateDirectory(_tempRoot);
    }

    public async Task<MiningMediaBundle> CaptureAsync(
        string ffmpeg,
        string mediaPath,
        SubtitleCue cue,
        double playerPosition,
        string sentence,
        IReadOnlyList<RenderSegment> segments,
        AppSettings settings,
        double? imageTimeOverride = null,
        double? audioStartOverride = null,
        double? audioEndOverride = null)
    {
        if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
            throw new FileNotFoundException("ffmpeg is required for mining media capture.", ffmpeg);
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            throw new FileNotFoundException("The current media file is not available for capture.", mediaPath);

        var id = Guid.NewGuid().ToString("N");
        var imageTime = imageTimeOverride ?? (settings.MediaImageSource == MediaImageSource.SubtitleMidpoint
            ? (cue.Start + cue.End) / 2.0
            : Math.Clamp(playerPosition, Math.Max(0, cue.Start - 5), cue.End + 5));
        var audioStart = audioStartOverride ?? Math.Max(0, cue.Start - settings.MediaAudioPadLeadMs / 1000.0);
        var audioEnd = audioEndOverride ?? Math.Max(audioStart + .1, cue.End + settings.MediaAudioPadTailMs / 1000.0);
        if (settings.MediaCaptureAudio && settings.MediaAudioAutoTrim && audioStartOverride is null && audioEndOverride is null)
            (audioStart, audioEnd) = await ResolveAudioBoundsAsync(ffmpeg, mediaPath, cue, settings);

        MiningMediaFile? image = null;
        MiningMediaFile? audio = null;
        string? preview = null;
        string? previewAudio = null;

        if (settings.MediaCaptureImage)
        {
            if (settings.MediaCaptureImageAnimated)
            {
                var path = Path.Combine(_tempRoot, $"{id}.webp");
                await CaptureAnimatedWebpAsync(ffmpeg, mediaPath, cue, sentence, segments, settings, path);
                image = new MiningMediaFile(await File.ReadAllBytesAsync(path), "image.webp", "image/webp", "image");
                // Use a PNG frame for the review window; Avalonia's decoder support is more predictable for PNG.
                preview = Path.Combine(_tempRoot, $"{id}-preview.png");
                await CaptureStaticAsync(ffmpeg, mediaPath, imageTime, sentence, segments, settings, preview, png: true);
            }
            else
            {
                var path = Path.Combine(_tempRoot, $"{id}.webp");
                await CaptureStaticAsync(ffmpeg, mediaPath, imageTime, sentence, segments, settings, path, png: false);
                image = new MiningMediaFile(await File.ReadAllBytesAsync(path), "image.webp", "image/webp", "image");
                preview = Path.Combine(_tempRoot, $"{id}-preview.png");
                await CaptureStaticAsync(ffmpeg, mediaPath, imageTime, sentence, segments, settings, preview, png: true);
            }
        }

        if (settings.MediaCaptureAudio)
        {
            var path = Path.Combine(_tempRoot, $"{id}.ogg");
            await CaptureAudioAsync(ffmpeg, mediaPath, audioStart, audioEnd, settings, path);
            audio = new MiningMediaFile(await File.ReadAllBytesAsync(path), "audio.ogg", "audio/ogg", "audio");
            if (settings.MediaReviewPopup)
            {
                previewAudio = Path.Combine(_tempRoot, $"{id}-preview.wav");
                await CapturePreviewWavAsync(ffmpeg, mediaPath, audioStart, audioEnd, settings, previewAudio);
            }
        }

        return new MiningMediaBundle
        {
            Image = image,
            Audio = audio,
            PreviewImagePath = preview,
            PreviewAudioPath = previewAudio,
            ImageTime = imageTime,
            AudioStart = audioStart,
            AudioEnd = audioEnd
        };
    }

    private async Task CaptureStaticAsync(string ffmpeg, string mediaPath, double time, string sentence,
        IReadOnlyList<RenderSegment> segments, AppSettings settings, string output, bool png)
    {
        var filter = await BuildVideoFilterAsync(sentence, segments, settings, settings.MediaImageMaxEdge);
        var args = new List<string> { "-y", "-ss", F(time), "-i", mediaPath, "-frames:v", "1", "-an" };
        if (!string.IsNullOrWhiteSpace(filter)) { args.Add("-vf"); args.Add(filter); }
        if (!png)
        {
            args.AddRange(["-c:v", "libwebp", "-quality", Math.Clamp(settings.MediaImageQuality, 1, 100).ToString(CultureInfo.InvariantCulture)]);
        }
        await RunAsync(ffmpeg, args, output);
    }

    private async Task CaptureAnimatedWebpAsync(string ffmpeg, string mediaPath, SubtitleCue cue, string sentence,
        IReadOnlyList<RenderSegment> segments, AppSettings settings, string output)
    {
        var duration = Math.Max(.15, cue.End - cue.Start);
        var targetFps = Math.Clamp(settings.MediaAnimTargetFps, 1, 60);
        var minFps = Math.Clamp(settings.MediaAnimMinFps, 1, targetFps);
        var fpsByFrames = Math.Max(minFps, Math.Min(targetFps, settings.MediaAnimMaxFrames / Math.Max(.15, duration)));
        var fps = (int)Math.Max(minFps, Math.Floor(fpsByFrames));
        var edge = Math.Clamp(settings.MediaAnimMaxEdge, 160, 3840);
        var quality = Math.Clamp(settings.MediaAnimQuality, 1, 100);

        for (var attempt = 0; attempt < 7; attempt++)
        {
            var filter = await BuildVideoFilterAsync(sentence, segments, settings, edge, fps);
            var args = new List<string>
            {
                "-y", "-ss", F(cue.Start), "-t", F(duration), "-i", mediaPath, "-an",
                "-vf", filter, "-loop", "0", "-c:v", "libwebp", "-quality", quality.ToString(CultureInfo.InvariantCulture)
            };
            await RunAsync(ffmpeg, args, output);
            var size = new FileInfo(output).Length;
            if (size <= settings.MediaAnimMaxBytes || settings.MediaAnimMaxBytes <= 0) return;

            if (fps > minFps) fps = Math.Max(minFps, fps - 2);
            else if (edge > 480) edge = Math.Max(480, (int)(edge * .82));
            else quality = Math.Max(35, quality - 10);
        }
        _log.Write($"Animated mining image remained above target size ({new FileInfo(output).Length} bytes); using best effort.");
    }

    private async Task CapturePreviewWavAsync(string ffmpeg, string mediaPath, double start, double end, AppSettings settings, string output)
    {
        var duration = Math.Max(.1, end - start);
        var args = new List<string> { "-y", "-ss", F(start), "-t", F(duration), "-i", mediaPath, "-vn", "-ac", settings.MediaAudioStereo ? "2" : "1", "-c:a", "pcm_s16le", output };
        await RunRawAsync(ffmpeg, args);
    }

    private async Task CaptureAudioAsync(string ffmpeg, string mediaPath, double start, double end, AppSettings settings, string output)
    {
        var duration = Math.Max(.1, end - start);
        var bitrate = Math.Clamp(settings.MediaAudioBitrateKbps, 16, 320);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var args = new List<string>
            {
                "-y", "-ss", F(start), "-t", F(duration), "-i", mediaPath, "-vn",
                "-c:a", "libopus", "-b:a", $"{bitrate}k", "-ac", settings.MediaAudioStereo ? "2" : "1", output
            };
            await RunRawAsync(ffmpeg, args);
            var size = new FileInfo(output).Length;
            if (size <= settings.MediaAudioMaxBytes || settings.MediaAudioMaxBytes <= 0) return;
            bitrate = Math.Max(24, bitrate - 8);
        }
        _log.Write($"Mining audio remained above target size ({new FileInfo(output).Length} bytes); using best effort.");
    }

    private async Task<(double Start, double End)> ResolveAudioBoundsAsync(string ffmpeg, string mediaPath, SubtitleCue cue, AppSettings settings)
    {
        var margin = Math.Max(0, settings.MediaAudioWindowMarginSeconds);
        if (margin <= 0)
            return (Math.Max(0, cue.Start - settings.MediaAudioPadLeadMs / 1000.0), Math.Max(cue.Start + .1, cue.End + settings.MediaAudioPadTailMs / 1000.0));

        var windowStart = Math.Max(0, cue.Start - margin);
        var windowEnd = cue.End + margin;
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-hide_banner", "-ss", F(windowStart), "-t", F(windowEnd-windowStart), "-i", mediaPath, "-vn", "-af", "silencedetect=noise=-45dB:d=0.08", "-f", "null", "-" }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        if (process is null) return (Math.Max(0, cue.Start - settings.MediaAudioPadLeadMs / 1000.0), cue.End + settings.MediaAudioPadTailMs / 1000.0);
        var stderrTask = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await stderrTask;
        if (process.ExitCode != 0) return (Math.Max(0, cue.Start - settings.MediaAudioPadLeadMs / 1000.0), cue.End + settings.MediaAudioPadTailMs / 1000.0);

        var silenceEnds = new List<double>();
        var silenceStarts = new List<double>();
        foreach (var line in stderr.Split('\n'))
        {
            var ix = line.IndexOf("silence_start:", StringComparison.OrdinalIgnoreCase);
            if (ix >= 0 && double.TryParse(line[(ix+14)..].Trim().Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var st)) silenceStarts.Add(windowStart + st);
            ix = line.IndexOf("silence_end:", StringComparison.OrdinalIgnoreCase);
            if (ix >= 0 && double.TryParse(line[(ix+12)..].Trim().Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var en)) silenceEnds.Add(windowStart + en);
        }
        var speechStart = silenceEnds.Where(x => x <= cue.Start + .05).DefaultIfEmpty(cue.Start).Max();
        var speechEnd = silenceStarts.Where(x => x >= cue.End - .05).DefaultIfEmpty(cue.End).Min();
        var start = Math.Max(0, speechStart - settings.MediaAudioPadLeadMs / 1000.0);
        var end = Math.Max(start + .1, speechEnd + settings.MediaAudioPadTailMs / 1000.0);
        return (start, end);
    }

    private async Task<string> BuildVideoFilterAsync(string sentence, IReadOnlyList<RenderSegment> segments, AppSettings settings, int maxEdge, int? fps = null)
    {
        var filters = new List<string>();
        if (fps is not null) filters.Add($"fps={fps.Value}");
        filters.Add($"scale={Math.Clamp(maxEdge, 160, 7680)}:{Math.Clamp(maxEdge, 160, 7680)}:force_original_aspect_ratio=decrease");
        if (settings.MediaSubtitleBurn != MediaSubtitleBurn.None && !string.IsNullOrWhiteSpace(sentence))
        {
            var assPath = Path.Combine(_tempRoot, $"burn-{Guid.NewGuid():N}.ass");
            await File.WriteAllTextAsync(assPath, MakeAss(sentence, segments, settings), new UTF8Encoding(false));
            filters.Add($"ass='{EscapeFilterPath(assPath)}'");
        }
        return string.Join(',', filters);
    }

    private static string MakeAss(string sentence, IReadOnlyList<RenderSegment> segments, AppSettings settings)
    {
        var fontSize = Math.Clamp(settings.FontSize, 16, 96);
        var outline = Math.Clamp(settings.BorderSize, 0, 10);
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("PlayResX: 1920");
        sb.AppendLine("PlayResY: 1080");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding");
        sb.AppendLine($"Style: Default,{EscapeAssField(settings.FontFamily)},{F(fontSize)},&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,{F(outline)},0,2,40,40,45,1");
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text");

        string text;
        if (settings.MediaSubtitleBurn == MediaSubtitleBurn.Colored && segments.Count > 0)
        {
            var t = new StringBuilder();
            foreach (var seg in segments)
            {
                var state = JitenApiClient.CollapseKnownState(seg.Word);
                var color = state < 0 ? "#FFFFFF" : ThemePresets.For(settings.Theme, state, settings).Text;
                t.Append("{\\c").Append(AssColor(color)).Append('}').Append(EscapeAssText(seg.Text));
            }
            text = t.ToString();
        }
        else text = EscapeAssText(sentence);

        sb.AppendLine($"Dialogue: 0,0:00:00.00,0:10:00.00,Default,,0,0,0,,{text}");
        return sb.ToString();
    }

    private async Task RunAsync(string ffmpeg, List<string> args, string output)
    {
        args.Add(output);
        await RunRawAsync(ffmpeg, args);
    }

    private async Task RunRawAsync(string ffmpeg, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await stderrTask;
        _ = await stdoutTask;
        if (process.ExitCode != 0)
        {
            var tail = stderr.Length > 1500 ? stderr[^1500..] : stderr;
            throw new InvalidOperationException("ffmpeg capture failed: " + tail.Trim());
        }
    }

    private static string EscapeFilterPath(string path) => path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
    private static string EscapeAssField(string text) => text.Replace(",", " ");
    private static string EscapeAssText(string text) => text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r", "").Replace("\n", "\\N");
    private static string AssColor(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6) s = "FFFFFF";
        return $"&H00{s[4]}{s[5]}{s[2]}{s[3]}{s[0]}{s[1]}&";
    }
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
