using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using JitenMpcBe.Controls;
using JitenMpcBe.Models;
using JitenMpcBe.Native;
using JitenMpcBe.Services;
using JitenMpcBe.Text;

namespace JitenMpcBe.Views;

public sealed record PopupCommand(string Name, OutlinedTokenControl Token, int Rating = 0);

public sealed partial class DictionaryPopupWindow : Window
{
    private readonly Border _border;
    private readonly StackPanel _root;
    private readonly Button _headwordButton;
    private readonly TextBlock _headword, _reading, _meta, _meaning, _conjugation, _deck, _state;
    private readonly StackPanel _furiganaPanel;
    private readonly WrapPanel _pitchDiagramPanel, _stateActions, _reviewPanel;
    private readonly Border _deckPicker;
    private readonly StackPanel _deckPickerPanel;
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);
    private OutlinedTokenControl? _token;
    private AppSettings? _settings;

    public OutlinedTokenControl? CurrentToken => _token;
    public event Action<PopupCommand>? CommandRequested;

    public DictionaryPopupWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _border = this.FindControl<Border>("PopupBorder")!;
        _root = this.FindControl<StackPanel>("RootStack")!;
        _headwordButton = this.FindControl<Button>("HeadwordButton")!;
        _headword = this.FindControl<TextBlock>("HeadwordText")!;
        _furiganaPanel = this.FindControl<StackPanel>("FuriganaPanel")!;
        _reading = this.FindControl<TextBlock>("ReadingText")!;
        _pitchDiagramPanel = this.FindControl<WrapPanel>("PitchDiagramPanel")!;
        _meta = this.FindControl<TextBlock>("MetaText")!;
        _meaning = this.FindControl<TextBlock>("MeaningText")!;
        _conjugation = this.FindControl<TextBlock>("ConjugationText")!;
        _deck = this.FindControl<TextBlock>("DeckText")!;
        _state = this.FindControl<TextBlock>("StateText")!;
        _stateActions = this.FindControl<WrapPanel>("StateActionsPanel")!;
        _deckPicker = this.FindControl<Border>("DeckPickerBorder")!;
        _deckPickerPanel = this.FindControl<StackPanel>("DeckPickerPanel")!;
        _reviewPanel = this.FindControl<WrapPanel>("ReviewPanel")!;

        Add("Mine", "MineButton");
        Add("NeverForget", "NeverForgetButton"); Add("Blacklist", "BlacklistButton"); Add("Suspend", "SuspendButton"); Add("Forget", "ForgetButton");
        Add("RotateBackward", "RotateBackwardButton"); Add("RotateForward", "RotateForwardButton");
        Add("ReviewAgain", "AgainButton", 1); Add("ReviewHard", "HardButton", 2); Add("ReviewGood", "GoodButton", 3); Add("ReviewEasy", "EasyButton", 4);
        _headwordButton.Click += (_, _) => Fire("OpenHeadword");
        Opened += (_, _) => ApplyNativeStyles();
    }

    private void Add(string name, string control, int rating = 0)
    {
        var button = this.FindControl<Button>(control)!;
        _buttons[name] = button;
        button.Click += (_, _) => Fire(name, rating);
    }

    private void Fire(string name, int rating = 0)
    {
        if (_token is not null) CommandRequested?.Invoke(new PopupCommand(name, _token, rating));
    }

    // Kept for call-site compatibility. Native ownership is intentionally not assigned here:
    // the popup is shown as an Avalonia-owned child of the subtitle overlay so it always
    // remains above that continuously-visible layered window.
    public void SetMpcOwner(IntPtr mpcHwnd) => ApplyNativeStyles();

    public void EnsureAboveOwner(IntPtr mpcHwnd)
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero) WindowUtil.SyncAbovePlayer(hwnd, mpcHwnd);
    }

    private void ApplyNativeStyles()
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        WindowUtil.MakeNonActivatingToolWindow(hwnd);
    }

    public void Populate(OutlinedTokenControl token, AppSettings settings)
    {
        var word = token.Word;
        if (word is null) return;
        _token = token; _settings = settings;
        var color = Color.Parse(settings.PopupBgColor);
        _border.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(settings.PopupBgOpacity * 255 / 100, 0, 255), color.R, color.G, color.B));
        _border.MaxWidth = Math.Max(250, settings.PopupMaxWidthPx);
        _root.MaxWidth = Math.Max(250, settings.PopupMaxWidthPx);
        var scale = Math.Clamp(settings.PopupFontScale, .5, 1.5);
        _headword.FontSize = 23 * scale; _reading.FontSize = 15 * scale;
        _meta.FontSize = 12 * scale; _meaning.FontSize = 14 * scale; _conjugation.FontSize = 12 * scale; _deck.FontSize = 12 * scale; _state.FontSize = 12 * scale;

        var spelling = string.IsNullOrWhiteSpace(word.Spelling) ? token.Surface : word.Spelling;
        _headword.Text = spelling;
        _headwordButton.IsEnabled = !settings.PopupDisableHeadwordLink;
        PopulateFurigana(spelling, word.Reading ?? "", settings.PopupFurigana, scale);

        var pitchAccents = ReadPitchAccents(word.PitchAccents).Distinct().ToList();
        var hasPitchDiagrams = PopulatePitchDiagrams(word.Reading ?? "", pitchAccents, settings, scale);

        var meta = new List<string>();
        if (settings.PopupShowFrequency && word.FrequencyRank is not null) meta.Add("Frequency #" + word.FrequencyRank.Value);
        var pos = FlattenStrings(word.PartsOfSpeech);
        if (pos.Count > 0) meta.Add(string.Join(", ", pos.Distinct()));
        // Match JitenMPV: when graphical diagrams are available they replace the numeric fallback.
        if (settings.PopupShowPitch && !hasPitchDiagrams && pitchAccents.Count > 0)
            meta.Add("Pitch: " + string.Join(", ", pitchAccents));
        _meta.Text = string.Join("  |  ", meta);

        var meanings = ReadMeaningEntries(word.MeaningsChunks)
            .Take(Math.Clamp(settings.PopupMaxMeanings, 1, 20))
            .Select((meaning, index) => $"{index + 1}. {meaning}");
        _meaning.Text = string.Join("\n", meanings);
        var conj = token.Token is null ? [] : FlattenStrings(token.Token.Conjugations);
        _conjugation.Text = settings.PopupShowConjugation && conj.Count > 0 ? "Conjugation: " + string.Join(" -> ", conj.Distinct()) : "";
        _deck.Text = "";
        var state = JitenApiClient.CollapseKnownState(word);
        _state.Text = state >= 0 ? "State: " + StateName(state) : "";

        _stateActions.IsVisible = settings.PopupShowStateActions || settings.MiningEnabled;
        _buttons["Mine"].IsVisible = settings.MiningEnabled;
        _buttons["Mine"].Content = "Mine";
        HideDeckPicker();
        _buttons["NeverForget"].IsVisible = settings.PopupShowStateActions && settings.PopupShowNeverForget;
        _buttons["Blacklist"].IsVisible = settings.PopupShowStateActions && settings.PopupShowBlacklist;
        _buttons["Suspend"].IsVisible = settings.PopupShowStateActions && settings.PopupShowSuspend;
        _buttons["Forget"].IsVisible = settings.PopupShowStateActions && settings.PopupShowForget;
        _buttons["RotateForward"].IsVisible = settings.PopupShowStateActions && settings.RotateStatesEnabled && settings.PopupShowRotateActions;
        _buttons["RotateBackward"].IsVisible = settings.PopupShowStateActions && settings.RotateStatesEnabled && settings.PopupShowRotateActions;
        _reviewPanel.IsVisible = settings.ReviewsEnabled && settings.PopupShowReview;
        _buttons["ReviewHard"].IsVisible = !settings.PopupUseTwoGrades;
        _buttons["ReviewEasy"].IsVisible = !settings.PopupUseTwoGrades;

        _root.Children.Remove(_stateActions);
        _root.Children.Remove(_deckPicker);
        _root.Children.Remove(_reviewPanel);
        if (settings.PopupMoveActionsBottom)
        {
            _root.Children.Add(_stateActions);
            _root.Children.Add(_deckPicker);
            _root.Children.Add(_reviewPanel);
        }
        else
        {
            var insert = Math.Min(4, _root.Children.Count);
            _root.Children.Insert(insert, _stateActions);
            _root.Children.Insert(insert + 1, _deckPicker);
            _root.Children.Insert(insert + 2, _reviewPanel);
        }
    }

    public void ShowDeckPicker(IEnumerable<StudyDeckInfo> decks)
    {
        _deckPickerPanel.Children.Clear();
        foreach (var deck in decks)
        {
            var button = new Button
            {
                Content = deck.Name, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.Parse("#27272A")), Foreground = new SolidColorBrush(Color.Parse("#E4E4E7")),
                Padding = new Thickness(8, 5)
            };
            var id = deck.Id;
            button.Click += (_, _) => Fire("MineDeck:" + id);
            _deckPickerPanel.Children.Add(button);
        }
        if (_deckPickerPanel.Children.Count == 0)
            _deckPickerPanel.Children.Add(new TextBlock { Text = "No word lists found.", Foreground = new SolidColorBrush(Color.Parse("#A1A1AA")), FontSize = 12 });
        _deckPicker.IsVisible = true;
    }

    public void HideDeckPicker() => _deckPicker.IsVisible = false;

    public void SetMineState(bool mined)
    {
        if (!_buttons.TryGetValue("Mine", out var mine)) return;
        mine.Content = mined ? "Mined" : "Mine";
        mine.IsEnabled = !(mined && _settings?.MiningToStudyDeck == true);
        mine.Opacity = mined ? .72 : 1.0;
    }

    public void SetDeckMembership(IEnumerable<string> names)
    {
        if (_settings?.PopupShowDeckMembership != true) { _deck.Text = ""; return; }
        var list = names.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _deck.Text = list.Count == 0 ? "" : "Lists: " + string.Join(", ", list);
    }

    public void PositionFor(OutlinedTokenControl token, AppSettings settings, IntPtr mpcHwnd)
    {
        var p0 = token.PointToScreen(new Point(0, 0));
        var p1 = token.PointToScreen(new Point(token.Bounds.Width, token.Bounds.Height));
        var screen = Screens.ScreenFromPoint(p0);
        var working = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        var widthPx = (int)Math.Ceiling(Math.Max(250, settings.PopupMaxWidthPx) * scale);
        var heightPx = (int)Math.Ceiling(Math.Max(140, Height > 0 ? Height * scale : 300));
        int x, y;

        if (settings.PopupPosition == PopupPositionMode.Fixed)
        {
            var rect = WindowUtil.GetBestVideoRect(mpcHwnd);
            var anchor = AnchorPoint(rect, settings.PopupFixedAnchor);
            x = anchor.X - widthPx / 2;
            y = anchor.Y - heightPx / 2 + settings.PopupOffsetPx;
        }
        else
        {
            x = p0.X + (p1.X - p0.X - widthPx) / 2;
            y = settings.PopupPosition == PopupPositionMode.BelowSubtitle
                ? p1.Y + settings.PopupOffsetPx
                : p0.Y - heightPx - settings.PopupOffsetPx;
        }
        x = Math.Clamp(x, working.X, Math.Max(working.X, working.Right - widthPx));
        y = Math.Clamp(y, working.Y, Math.Max(working.Y, working.Bottom - heightPx));
        Position = new PixelPoint(x, y);
    }

    private static PixelPoint AnchorPoint(WinRect rect, PopupAnchor anchor)
    {
        var x = anchor is PopupAnchor.TopLeft or PopupAnchor.CenterLeft or PopupAnchor.BottomLeft ? rect.Left
            : anchor is PopupAnchor.TopRight or PopupAnchor.CenterRight or PopupAnchor.BottomRight ? rect.Right
            : rect.Left + rect.Width / 2;
        var y = anchor is PopupAnchor.TopLeft or PopupAnchor.TopCenter or PopupAnchor.TopRight ? rect.Top
            : anchor is PopupAnchor.BottomLeft or PopupAnchor.BottomCenter or PopupAnchor.BottomRight ? rect.Bottom
            : rect.Top + rect.Height / 2;
        return new PixelPoint(x, y);
    }

    public bool IsCursorInside()
    {
        if (!IsVisible || !WindowUtil.TryGetCursor(out var p)) return false;
        var local = this.PointToClient(new PixelPoint(p.X, p.Y));
        return local.X >= 0 && local.Y >= 0 && local.X <= ClientSize.Width && local.Y <= ClientSize.Height;
    }

    public static string StateName(int state) => state switch
    {
        0 => "New", 1 => "Young", 2 => "Mature", 3 => "Blacklisted", 4 => "Due", 5 => "Mastered", 6 => "Redundant", 7 => "Suspended", _ => "Unknown"
    };

    private void PopulateFurigana(string spelling, string reading, bool enabled, double scale)
    {
        _furiganaPanel.Children.Clear();
        _furiganaPanel.IsVisible = false;
        _headword.IsVisible = true;
        _reading.Text = "";

        if (!enabled || string.IsNullOrWhiteSpace(reading)) return;

        var segments = FuriganaParser.ForSpelling(spelling, reading);
        if (segments is null)
        {
            var plainReading = FuriganaParser.ToKana(reading);
            _reading.Text = !string.IsNullOrWhiteSpace(plainReading) && plainReading != spelling ? plainReading : "";
            return;
        }

        foreach (var segment in segments)
        {
            // Keep an ideographic-space placeholder over unannotated runs (e.g. okurigana).
            // This keeps every base segment on the same baseline while still using Japanese font metrics.
            var ruby = new TextBlock
            {
                Text = segment.Ruby.Length > 0 ? segment.Ruby : "　",
                FontSize = 11.5 * scale,
                Foreground = new SolidColorBrush(Color.Parse("#A1A1AA")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, -2 * scale)
            };
            var baseText = new TextBlock
            {
                Text = segment.Text,
                FontSize = 23 * scale,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            _furiganaPanel.Children.Add(new StackPanel { Children = { ruby, baseText } });
        }

        _headword.IsVisible = false;
        _furiganaPanel.IsVisible = true;
    }

    private bool PopulatePitchDiagrams(string reading, IReadOnlyList<int> accents, AppSettings settings, double scale)
    {
        _pitchDiagramPanel.Children.Clear();
        _pitchDiagramPanel.IsVisible = false;
        if (!settings.PopupShowPitch || !settings.PopupPitchDiagram || accents.Count == 0 || string.IsNullOrWhiteSpace(reading))
            return false;

        foreach (var accent in accents.Take(4))
        {
            if (PitchAccent.BuildDiagram(reading, accent) is not { } diagram) continue;
            var pitchColor = settings.PitchStyles.TryGetValue(diagram.Class.ToString(), out var configuredColor)
                ? configuredColor
                : PitchAccent.DefaultColor(diagram.Class);
            _pitchDiagramPanel.Children.Add(new PitchDiagramControl
            {
                Diagram = diagram,
                AccentColor = pitchColor,
                ScaleFactor = scale,
                Margin = new Thickness(0, 0, 6 * scale, 0)
            });
        }

        _pitchDiagramPanel.IsVisible = _pitchDiagramPanel.Children.Count > 0;
        return _pitchDiagramPanel.IsVisible;
    }

    private static List<int> ReadPitchAccents(JsonElement element)
    {
        var result = new List<int>();
        CollectPitchAccents(element, result);
        return result.Where(x => x >= 0).ToList();
    }

    private static void CollectPitchAccents(JsonElement element, List<int> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var value)) result.Add(value);
                break;
            case JsonValueKind.String:
                if (int.TryParse(element.GetString(), out var parsed)) result.Add(parsed);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectPitchAccents(item, result);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    if (property.Name.Contains("accent", StringComparison.OrdinalIgnoreCase)
                        || property.NameEquals("value")
                        || property.NameEquals("pitch"))
                        CollectPitchAccents(property.Value, result);
                break;
        }
    }

    private static List<string> ReadMeaningEntries(JsonElement element)
    {
        var entries = new List<string>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var chunk in element.EnumerateArray())
            {
                var parts = FlattenStrings(chunk).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                if (parts.Count > 0) entries.Add(string.Join("; ", parts));
            }
        }
        else
        {
            var parts = FlattenStrings(element).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (parts.Count > 0) entries.Add(string.Join("; ", parts));
        }
        return entries;
    }

    private static List<string> FlattenStrings(JsonElement element)
    {
        var result = new List<string>(); Collect(element, result);
        return result.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static void Collect(JsonElement e, List<string> result)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: result.Add(e.GetString() ?? ""); break;
            case JsonValueKind.Number: result.Add(e.ToString()); break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Collect(x, result); break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    if (p.NameEquals("meaning") || p.NameEquals("text") || p.NameEquals("name") || p.NameEquals("value") || p.Name.Contains("accent", StringComparison.OrdinalIgnoreCase)) Collect(p.Value, result);
                break;
        }
    }
}
