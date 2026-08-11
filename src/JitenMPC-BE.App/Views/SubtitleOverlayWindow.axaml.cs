using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using JitenMpcBe.Controls;
using JitenMpcBe.Models;
using JitenMpcBe.Native;
using JitenMpcBe.Services;

namespace JitenMpcBe.Views;

public sealed partial class SubtitleOverlayWindow : Window
{
    private readonly StackPanel _stack;
    private readonly Border _statusBorder, _prevHint, _nextHint;
    private readonly TextBlock _statusText;
    private readonly List<OutlinedTokenControl> _wordControls = [];
    private IntPtr _mpcRoot;
    private List<RenderSegment> _lastSegments = [];
    private AppSettings? _lastSettings;
    private double _contentScale = 1.0;
    private DateTime _statusUntil;

    public IReadOnlyList<OutlinedTokenControl> WordControls => _wordControls;
    public double ContentScale => _contentScale;
    public IReadOnlyList<RenderSegment> LastSegments => _lastSegments;

    public SubtitleOverlayWindow()
    {
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWindowStylesCallback(this, OverlayWindowStyles);
        AvaloniaXamlLoader.Load(this);
        _stack = this.FindControl<StackPanel>("SubtitleStack")!;
        _statusBorder = this.FindControl<Border>("StatusBorder")!;
        _statusText = this.FindControl<TextBlock>("StatusOverlayText")!;
        _prevHint = this.FindControl<Border>("PrevHint")!;
        _nextHint = this.FindControl<Border>("NextHint")!;
        Opened += (_, _) => ApplyNativeStyles();
    }


    private static (uint style, uint exStyle) OverlayWindowStyles(uint style, uint exStyle)
    {
        const uint clickThrough = (uint)(NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        return (style, exStyle | clickThrough);
    }

    public void SetMpcOwner(IntPtr mpcHwnd)
    {
        _mpcRoot = WindowUtil.GetPlayerHostWindow(mpcHwnd);
        ApplyNativeStyles();
    }

    private void ApplyNativeStyles()
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        WindowUtil.MakeOverlayClickThrough(hwnd);
        if (_mpcRoot != IntPtr.Zero) WindowUtil.SetOwner(hwnd, _mpcRoot);
    }

    public void SetGeometry(IntPtr mpcHwnd, AppSettings settings)
    {
        var host = WindowUtil.GetPlayerHostWindow(mpcHwnd);
        if (host == IntPtr.Zero) return;
        if (host != _mpcRoot)
        {
            _mpcRoot = host;
            ApplyNativeStyles();
        }

        var rect = WindowUtil.GetBestVideoRect(host);
        if (!rect.IsValid) return;
        var dpiScale = WindowUtil.GetScaleForWindow(host);
        var videoWidthDip = rect.Width / dpiScale;
        var videoHeightDip = rect.Height / dpiScale;

        var center = new PixelPoint(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var screen = Screens.ScreenFromPoint(center);
        var screenHeightPx = Math.Max(1, screen?.Bounds.Height ?? rect.Height);
        var nextContentScale = rect.Height >= screenHeightPx * 0.85
            ? 1.0
            : Math.Clamp(rect.Height / (double)screenHeightPx, 0.20, 1.0);

        if (Math.Abs(nextContentScale - _contentScale) >= 0.01)
        {
            _contentScale = nextContentScale;
            if (_lastSettings is not null && _lastSegments.Count > 0) RenderSegmentsCore(_lastSegments, _lastSettings);
        }

        var marginX = Math.Min(Math.Max(0, settings.SubtitleMarginX * _contentScale), Math.Max(0, (videoWidthDip - 100) / 2));
        var marginY = Math.Min(Math.Max(0, settings.SubtitleMarginY * _contentScale), Math.Max(0, (videoHeightDip - 60) / 2));
        var widthDip = Math.Max(100, videoWidthDip - marginX * 2);
        var heightDip = Math.Max(60, videoHeightDip - marginY * 2);
        Position = new PixelPoint(rect.Left + (int)Math.Round(marginX * dpiScale), rect.Top + (int)Math.Round(marginY * dpiScale));
        Width = widthDip;
        Height = heightDip;
        ApplyAlignment(settings.SubtitleAlignment);

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero) WindowUtil.SyncAbovePlayer(hwnd, host);
    }

    private void ApplyAlignment(int alignment)
    {
        alignment = Math.Clamp(alignment, 1, 9);
        _stack.VerticalAlignment = alignment >= 7 ? VerticalAlignment.Top : alignment >= 4 ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        _stack.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public void ClearSubtitle()
    {
        _lastSegments.Clear();
        _stack.Children.Clear();
        _wordControls.Clear();
    }

    public void RenderSegments(IEnumerable<RenderSegment> segments, AppSettings settings)
    {
        _lastSegments = segments.ToList();
        _lastSettings = settings;
        RenderSegmentsCore(_lastSegments, settings);
    }

    private void RenderSegmentsCore(IEnumerable<RenderSegment> sourceSegments, AppSettings settings)
    {
        _stack.Children.Clear();
        _wordControls.Clear();
        var segments = sourceSegments.ToList();
        var alignment = Math.Clamp(settings.SubtitleAlignment, 1, 9);
        var hAlign = (alignment % 3) switch { 1 => HorizontalAlignment.Left, 0 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };

        var interactive = segments.Where(s => s.Word is not null).ToList();
        var iPlusOneCandidate = false;
        JitenWord? iPlusOneWord = null;
        if (settings.IPlusOneEnabled && interactive.Count >= settings.IPlusOneMinTokens)
        {
            var unknown = interactive.Where(s => JitenApiClient.CollapseKnownState(s.Word) == 0)
                .Select(s => s.Word!).DistinctBy(w => (w.WordId, w.ReadingIndex)).ToList();
            if (unknown.Count == 1 && (unknown[0].FrequencyRank is null || unknown[0].FrequencyRank <= settings.IPlusOneMaxFrequencyRank))
            {
                iPlusOneCandidate = true;
                iPlusOneWord = unknown[0];
            }
        }

        // JitenMPV's single-line option only joins when it can fit. Estimate the total width using token controls;
        // if it won't fit, retain the authored line breaks.
        var forceSingleLine = false;
        if (settings.SubtitleSingleLine && segments.Any(s => s.Text.Contains('\n')))
        {
            var estimatedChars = string.Concat(segments.Select(s => s.Text.Replace("\n", ""))).Length;
            var averageEm = settings.FontSize * _contentScale * .82;
            forceSingleLine = estimatedChars * averageEm <= Math.Max(100, Width - 20);
        }

        var line = NewLine(hAlign);
        foreach (var segment in segments)
        {
            var raw = forceSingleLine ? segment.Text.Replace("\n", "") : segment.Text;
            var pieces = raw.Split('\n');
            for (var i = 0; i < pieces.Length; i++)
            {
                if (pieces[i].Length > 0)
                {
                    var state = JitenApiClient.CollapseKnownState(segment.Word);
                    var style = state < 0 ? new WordStyle("#EEEEEE", "#000000", 3) : ThemePresets.For(settings.Theme, state, settings);
                    var frequencyUnderline = settings.FrequencyMarkingEnabled && segment.Word?.FrequencyRank is int rank && rank <= settings.FrequencyTopN && (settings.FrequencyMarkAllStates || state == 0);
                    var blur = settings.BlurEnabled && state >= 0 && settings.BlurStates.Contains(state);
                    var pitchColor = settings.PitchColoringEnabled ? PitchColor(segment.Word, settings) : "";
                    var visual = new TokenVisualOptions(
                        FrequencyUnderline: frequencyUnderline,
                        IPlusOneHighlight: iPlusOneCandidate && segment.Word is not null && iPlusOneWord is not null && segment.Word.WordId == iPlusOneWord.WordId && segment.Word.ReadingIndex == iPlusOneWord.ReadingIndex,
                        Blur: blur,
                        BlurStrength: settings.BlurStrength * _contentScale,
                        PitchColor: pitchColor,
                        PitchUnderline: settings.PitchColoringEnabled && settings.PitchIndicator == PitchIndicatorMode.Underline,
                        PitchUnderlineThickness: settings.PitchUnderlineThickness * _contentScale,
                        DebugHitbox: settings.DebugShowHitboxes);
                    var token = new OutlinedTokenControl();
                    var scaledStyle = style with { ShadowDepth = style.ShadowDepth * _contentScale, UnderlineThickness = style.UnderlineThickness * _contentScale };
                    token.Configure(pieces[i], segment.Word, segment.Token, settings.FontFamily,
                        settings.FontSize * _contentScale, settings.BorderSize * _contentScale, scaledStyle, visual);
                    line.Children.Add(token);
                    if (token.Interactive) _wordControls.Add(token);
                }
                if (i < pieces.Length - 1)
                {
                    _stack.Children.Add(line);
                    line = NewLine(hAlign);
                }
            }
        }
        _stack.Children.Add(line);
    }

    private static string PitchColor(JitenWord? word, AppSettings settings)
    {
        if (word is null) return "";
        var pattern = PitchPattern(word.PitchAccents, word.Reading ?? word.Spelling ?? "");
        return settings.PitchStyles.TryGetValue(pattern, out var color) ? color : settings.PitchStyles.GetValueOrDefault("Unknown", "#D4D4D8");
    }

    private static string PitchPattern(JsonElement element, string reading)
    {
        var numbers = new List<int>();
        CollectNumbers(element, numbers);
        if (numbers.Count == 0) return "Unknown";
        var accent = numbers[0];
        if (accent == 0) return "Heiban";
        if (accent == 1) return "Atamadaka";
        var mora = Math.Max(1, reading.EnumerateRunes().Count());
        return accent >= mora ? "Odaka" : "Nakadaka";
    }

    private static void CollectNumbers(JsonElement e, List<int> result)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) result.Add(n);
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) CollectNumbers(x, result);
        else if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
                if (p.Name.Contains("accent", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("position", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("drop", StringComparison.OrdinalIgnoreCase)) CollectNumbers(p.Value, result);
    }

    private static StackPanel NewLine(HorizontalAlignment align) => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = align,
        ClipToBounds = false
    };

    public OutlinedTokenControl? FindHoveredWord()
    {
        if (!IsVisible || !WindowUtil.TryGetCursor(out var cursor)) return null;
        var local = this.PointToClient(new PixelPoint(cursor.X, cursor.Y));
        foreach (var word in _wordControls)
        {
            if (!word.IsVisible || word.Bounds.Width <= 0 || word.Bounds.Height <= 0) continue;
            var origin = word.TranslatePoint(new Point(0, 0), this);
            if (origin is null) continue;
            if (new Rect(origin.Value, word.Bounds.Size).Contains(local)) return word;
        }
        return null;
    }

    public void SetBlurReveal(OutlinedTokenControl? word, bool revealed)
    {
        if (word is null) return;
        word.SetHoverReveal(revealed);
    }

    public void ShowStatus(string text, int milliseconds = 1800)
    {
        _statusText.Text = text;
        _statusBorder.IsVisible = true;
        _statusUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(300, milliseconds));
    }

    public void UpdateTransientUi(AppSettings settings, bool mouseInInteractionZone)
    {
        if (_statusBorder.IsVisible && DateTime.UtcNow >= _statusUntil) _statusBorder.IsVisible = false;
        _prevHint.IsVisible = mouseInInteractionZone && settings.SubtitleNavButtonsEnabled;
        _nextHint.IsVisible = mouseInInteractionZone && settings.SubtitleNavButtonsEnabled;
    }


    public string? FindMouseAction()
    {
        if (!WindowUtil.TryGetCursor(out var cursor)) return null;
        return FindMouseActionAt(cursor);
    }

    public string? FindMouseActionAt(WinPoint cursor)
    {
        if (!IsVisible) return null;
        var p = this.PointToClient(new PixelPoint(cursor.X, cursor.Y));
        if (_prevHint.IsVisible && Contains(_prevHint, p)) return "Previous";
        if (_nextHint.IsVisible && Contains(_nextHint, p)) return "Next";
        return null;
    }

    private bool Contains(Control control, Point p)
    {
        var origin = control.TranslatePoint(new Point(), this);
        return origin is not null && new Rect(origin.Value, control.Bounds.Size).Contains(p);
    }
}
