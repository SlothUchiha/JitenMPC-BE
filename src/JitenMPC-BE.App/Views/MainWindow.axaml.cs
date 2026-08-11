using System.Diagnostics;
using System.Reflection;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JitenMpcBe.Models;
using JitenMpcBe.Services;

namespace JitenMpcBe.Views;

public sealed partial class MainWindow : Window
{
    private sealed class StateEditor
    {
        public required ColorPicker Text, Outline, Shadow, Underline;
        public required Slider OutlineSize, ShadowDepth, UnderlineThickness, Opacity;
        public required CheckBox HasShadow, Bold, Italic, UnderlineEnabled, Strike;
    }

    private readonly AppRuntime _runtime;
    private readonly ScrollViewer[] _panels;
    private readonly Dictionary<int, StateEditor> _stateEditors = [];
    private readonly Dictionary<string, ColorPicker> _pitchPickers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _popupKeyBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _loading = true;
    private bool _updatingTracks;
    private int _alignment = 2;
    private bool _apiKeyVisible;

    private readonly TextBlock _status, _version, _media, _subtitle, _updateStatus;
    private readonly ComboBox _track, _theme, _font;

    public MainWindow(AppRuntime runtime)
    {
        _runtime = runtime;
        AvaloniaXamlLoader.Load(this);
        F<TextBlock>("AppVersionText").Text = $"v{GetApplicationVersion()}";
        _panels = [F<ScrollViewer>("GeneralPanel"), F<ScrollViewer>("AppearancePanel"), F<ScrollViewer>("FeaturesPanel"), F<ScrollViewer>("MediaPanel"), F<ScrollViewer>("PopupPanel"), F<ScrollViewer>("KeybindsPanel"), F<ScrollViewer>("AdvancedPanel")];
        _track = F<ComboBox>("SubtitleTrackBox"); _theme = F<ComboBox>("ThemeBox"); _font = F<ComboBox>("FontBox");
        _status = F<TextBlock>("StatusText"); _version = F<TextBlock>("MpcVersionText"); _media = F<TextBlock>("MediaPathText"); _subtitle = F<TextBlock>("SubtitlePathText"); _updateStatus = F<TextBlock>("UpdateStatusText");

        BuildCustomThemeEditors(); BuildPitchEditors(); BuildPopupKeybindEditors(); WireEvents();
        _runtime.SubtitleTracksChanged += tracks => Dispatcher.UIThread.Post(() => UpdateTracks(tracks));
        _runtime.ConnectionInfoChanged += () => Dispatcher.UIThread.Post(UpdateConnection);
        _runtime.MediaInfoChanged += () => Dispatcher.UIThread.Post(UpdateMedia);
        _runtime.StatusChanged += text => Dispatcher.UIThread.Post(() => SetStatus(text));
        _runtime.UpdateInfoChanged += info => Dispatcher.UIThread.Post(() => UpdateUpdateUi(info));
        _runtime.StudyDecksChanged += decks => Dispatcher.UIThread.Post(() => UpdateMiningDecks(decks));
        _runtime.JitenPlusChanged += plus => Dispatcher.UIThread.Post(() => UpdateJitenPlus(plus));

        _autoSaveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, async (_, _) => await AutoSaveTickAsync());
        Opened += (_, _) =>
        {
            PopulateFromSettings();
            _autoSaveTimer.Start();
            if (!string.IsNullOrWhiteSpace(_runtime.Settings.ApiKey))
            {
                _ = _runtime.LoadStudyDecksAsync();
                _ = _runtime.RefreshJitenPlusStatusAsync();
            }
        };
        Closing += (_, _) => { try { PullSettings(); _runtime.SaveSettingsQuietly(); } catch { } };
    }

    private T F<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private static string GetApplicationVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var buildMetadata = version.IndexOf('+');
        return buildMetadata >= 0 ? version[..buildMetadata] : version;
    }

    private void WireEvents()
    {
        F<ListBox>("NavList").SelectionChanged += (_, _) => { if (!_loading) SaveIfAuto(); SwitchPanel(F<ListBox>("NavList").SelectedIndex); };
        F<Button>("SaveButton").Click += async (_, _) => { PullSettings(); _runtime.SaveSettings(); await _runtime.RerenderCurrentCueAsync(); SetStatus("Settings saved."); };
        F<Button>("LaunchButton").Click += (_, _) => TryAction(() => { PullSettings(); _runtime.SaveSettingsQuietly(); _runtime.LaunchMpc(); });
        F<Button>("CloseSettingsButton").Click += (_, _) => { PullSettings(); _runtime.SaveSettingsQuietly(); Hide(); };
        F<Button>("ResetSectionButton").Click += async (_, _) => { ResetCurrentSection(); PopulateFromSettings(); _runtime.SaveSettings(); await _runtime.RerenderCurrentCueAsync(); };

        F<Button>("ShowApiKeyButton").Click += (_, _) =>
        {
            _apiKeyVisible = !_apiKeyVisible;
            F<TextBox>("ApiKeyBox").PasswordChar = _apiKeyVisible ? '\0' : '*';
            F<Button>("ShowApiKeyButton").Content = _apiKeyVisible ? "Hide" : "Show";
        };
        F<Button>("TestApiButton").Click += async (_, _) => { PullSettings(); _runtime.SaveSettingsQuietly(); var ok = await _runtime.TestApiAsync(); F<TextBlock>("ApiStatusText").Text = ok ? "Connected" : "Connection failed"; if (ok) { await _runtime.LoadStudyDecksAsync(); await _runtime.RefreshJitenPlusStatusAsync(); } };
        F<Button>("GetApiKeyButton").Click += (_, _) => Process.Start(new ProcessStartInfo("https://jiten.moe/settings") { UseShellExecute = true });
        F<Button>("CheckUpdatesButton").Click += async (_, _) => { PullSettings(); await _runtime.CheckUpdatesAsync(); };
        F<Button>("InstallUpdateButton").Click += async (sender, _) =>
        {
            var button = (Button)sender!;
            button.IsEnabled = false;
            PullSettings();
            _runtime.SaveSettingsQuietly();
            var installerStarted = await _runtime.InstallPendingUpdateAsync();
            if (installerStarted && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
                return;
            }
            button.IsEnabled = true;
        };

        F<Button>("BrowseMpcButton").Click += async (_, _) => await BrowseIntoAsync(F<TextBox>("MpcPathBox"), "Choose MPC-BE", ["*.exe"]);
        F<Button>("BrowseFfmpegButton").Click += async (_, _) => await BrowseIntoAsync(F<TextBox>("FfmpegPathBox"), "Choose ffmpeg", ["ffmpeg.exe", "*.exe"]);
        F<Button>("BrowseFfprobeButton").Click += async (_, _) => await BrowseIntoAsync(F<TextBox>("FfprobePathBox"), "Choose ffprobe", ["ffprobe.exe", "*.exe"]);
        F<Button>("DetectFfmpegButton").Click += (_, _) => { PullSettings(); var p = _runtime.RedetectFfmpeg(); F<TextBox>("FfmpegPathBox").Text = p.ffmpeg; F<TextBox>("FfprobePathBox").Text = p.ffprobe; };

        F<Button>("ThemeImportButton").Click += async (_, _) => { PullSettings(); if (_runtime.ImportJitenReaderTheme(F<TextBox>("ThemeImportBox").Text ?? "", out var status)) { F<TextBlock>("ThemeImportStatus").Text = status; PopulateAppearanceOnly(); await _runtime.RerenderCurrentCueAsync(); } else F<TextBlock>("ThemeImportStatus").Text = status; };
        F<Button>("ResetPitchColorsButton").Click += (_, _) => ResetPitchColors();
        _theme.SelectionChanged += async (_, _) => { if (_loading) return; F<StackPanel>("CustomThemePanel").IsVisible = string.Equals(_theme.SelectedItem as string, "Custom", StringComparison.OrdinalIgnoreCase); PullSettings(); await _runtime.RerenderCurrentCueAsync(); };
        _font.SelectionChanged += async (_, _) => { UpdateFontPreview(); if (_loading) return; PullSettings(); await _runtime.RerenderCurrentCueAsync(); };
        _font.LostFocus += async (_, _) => { UpdateFontPreview(); if (_loading) return; PullSettings(); await _runtime.RerenderCurrentCueAsync(); };
        F<Slider>("FontSizeSlider").PropertyChanged += async (_, e) => { if (e.Property == Slider.ValueProperty) { F<TextBlock>("FontSizeValue").Text = $"{F<Slider>("FontSizeSlider").Value:0}"; if (!_loading) { PullSettings(); await _runtime.RerenderCurrentCueAsync(); } } };
        F<Slider>("BorderSizeBox").PropertyChanged += async (_, e) => { if (e.Property == Slider.ValueProperty) { F<TextBlock>("BorderSizeValue").Text = $"{F<Slider>("BorderSizeBox").Value:0.0}"; if (!_loading) { PullSettings(); await _runtime.RerenderCurrentCueAsync(); } } };
        F<Slider>("PitchUnderlineSlider").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("PitchUnderlineValue").Text = $"{F<Slider>("PitchUnderlineSlider").Value:0}px"; };
        F<Slider>("PopupFontScaleSlider").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("PopupFontScaleValue").Text = $"{F<Slider>("PopupFontScaleSlider").Value:0.00}"; };
        F<Slider>("PopupBgOpacitySlider").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("PopupBgOpacityValue").Text = $"{F<Slider>("PopupBgOpacitySlider").Value:0}%"; };
        F<Slider>("PopupMaxWidthBox").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("PopupMaxWidthValue").Text = $"{F<Slider>("PopupMaxWidthBox").Value:0} px"; };
        F<Slider>("MouseZoneSlider").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("MouseZoneValue").Text = $"Bottom {F<Slider>("MouseZoneSlider").Value:0}%"; };
        F<Slider>("MarginXSlider").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("MarginXValue").Text = $"{F<Slider>("MarginXSlider").Value:0}"; };
        F<Slider>("MarginYSlider").PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) F<TextBlock>("MarginYValue").Text = $"{F<Slider>("MarginYSlider").Value:0}"; };
        foreach (var n in Enumerable.Range(1, 9)) { var a = n; F<ToggleButton>("Align" + n).Click += async (_, _) => { _alignment = a; UpdateAlignmentButtons(); if (!_loading) { PullSettings(); await _runtime.RerenderCurrentCueAsync(); } }; }

        F<Button>("LoadExternalSubtitleButton").Click += async (_, _) => await BrowseSubtitleAsync();
        _track.SelectionChanged += async (_, _) => { if (_updatingTracks || _track.SelectedItem is not SubtitleStreamInfo info) return; await _runtime.SelectSubtitleTrackAsync(info.Index); };
        F<Button>("OpenConfigFolderButton").Click += (_, _) => _runtime.OpenConfigFolder();
        F<Button>("LoadMiningDecksButton").Click += async (_, _) => { PullSettings(); _runtime.SaveSettingsQuietly(); await _runtime.LoadStudyDecksAsync(); };
        F<Button>("RefreshJitenPlusButton").Click += async (_, _) => { PullSettings(); _runtime.SaveSettingsQuietly(); await _runtime.RefreshJitenPlusStatusAsync(); };
        F<ToggleSwitch>("MiningToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<ToggleSwitch>("MediaCaptureToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("MediaImageCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("MediaAudioCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("MediaReviewCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<ComboBox>("MiningDeckBox").SelectionChanged += (_, _) => { if (!_loading) F<TextBlock>("MiningDeckStatus").Text = F<ComboBox>("MiningDeckBox").SelectedItem is StudyDeckInfo d ? $"Target: {d.Name}" : "Choose a target list."; };

        F<ToggleSwitch>("IPlusOneToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<ToggleSwitch>("FrequencyToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<ToggleSwitch>("BlurToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("BlurRevealCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<ToggleSwitch>("AutopauseToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<ToggleSwitch>("PitchColorToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<RadioButton>("PitchTextRadio").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<RadioButton>("PitchUnderlineRadio").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("PopupAutoHideCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<RadioButton>("PopupAboveRadio").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<RadioButton>("PopupBelowRadio").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<RadioButton>("PopupFixedRadio").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("PopupStateActionsCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("PopupPitchCheck").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
        F<CheckBox>("RotateStatesToggle").IsCheckedChanged += (_, _) => UpdateConditionalVisibility();
    }

    private void BuildCustomThemeEditors()
    {
        var host = F<StackPanel>("CustomThemePanel");
        host.Children.Add(SectionHeader("WORD STATE STYLES"));
        host.Children.Add(new TextBlock
        {
            Text = "Customize the appearance for each word knowledge state",
            FontSize = 12,
            Foreground = Brush("#71717A")
        });

        for (var i = 0; i < ThemePresets.StateNames.Length; i++)
        {
            var text = Picker();
            var outline = Picker();
            var shadow = Picker();
            var underline = Picker();
            var outlineSize = StateSlider(0, 10, .5, 200);
            var shadowDepth = StateSlider(0, 10, .5, 200);
            var underlineThickness = StateSlider(1, 10, .5, 200);
            var opacity = StateSlider(0, 100, 1, 200);
            var hasShadow = Check("");
            var bold = Check("Bold");
            var italic = Check("Italic");
            var under = Check("Underline");
            var strike = Check("Strikethrough");

            var swatch = new Border
            {
                Width = 14, Height = 14, CornerRadius = new Avalonia.CornerRadius(3),
                Background = Brush("#D4D4D8"), VerticalAlignment = VerticalAlignment.Center
            };
            text.PropertyChanged += (_, e) =>
            {
                if (e.Property == ColorPicker.ColorProperty)
                    swatch.Background = new SolidColorBrush(text.Color);
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            header.Children.Add(swatch);
            header.Children.Add(new TextBlock
            {
                Text = ThemePresets.StateNames[i], FontSize = 14, FontWeight = FontWeight.Medium,
                Foreground = Brush("#D4D4D8"), VerticalAlignment = VerticalAlignment.Center
            });

            var content = new StackPanel { Spacing = 12 };

            content.Children.Add(SectionHeader("COLORS"));
            var colors = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("90,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), RowSpacing = 8
            };
            AddGridLabel(colors, "Text", 0); AddGridControl(colors, text, 0);
            AddGridLabel(colors, "Outline", 1); AddGridControl(colors, outline, 1);
            AddGridLabel(colors, "Shadow", 2);
            var shadowRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            shadowRow.Children.Add(hasShadow); shadowRow.Children.Add(shadow);
            AddGridControl(colors, shadowRow, 2);
            AddGridLabel(colors, "Underline", 3); AddGridControl(colors, underline, 3);
            content.Children.Add(colors);

            content.Children.Add(SectionHeader("SIZES"));
            var sizes = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("90,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), RowSpacing = 8
            };
            AddGridLabel(sizes, "Outline", 0); AddGridControl(sizes, SliderWithValue(outlineSize, "0.0", 30), 0);
            AddGridLabel(sizes, "Shadow", 1); AddGridControl(sizes, SliderWithValue(shadowDepth, "0.0", 30), 1);
            AddGridLabel(sizes, "Underline", 2); AddGridControl(sizes, SliderWithValue(underlineThickness, "0.0", 30), 2);
            content.Children.Add(sizes);

            content.Children.Add(SectionHeader("OPACITY"));
            var opacityGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
            AddGridLabel(opacityGrid, "Text", 0); AddGridControl(opacityGrid, SliderWithValue(opacity, "0", 40, "%"), 0);
            content.Children.Add(opacityGrid);

            content.Children.Add(SectionHeader("TEXT EFFECTS"));
            var effects = new WrapPanel { Orientation = Orientation.Horizontal };
            bold.Margin = new Avalonia.Thickness(0, 0, 16, 4);
            italic.Margin = new Avalonia.Thickness(0, 0, 16, 4);
            under.Margin = new Avalonia.Thickness(0, 0, 16, 4);
            effects.Children.Add(bold); effects.Children.Add(italic); effects.Children.Add(under); effects.Children.Add(strike);
            content.Children.Add(effects);

            var card = new Border
            {
                Background = Brush("#18181B"), BorderBrush = Brush("#3F3F46"), BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(16), Margin = new Avalonia.Thickness(0, 4, 0, 0),
                Child = content
            };
            var expander = new Expander { Header = header, Content = card, Margin = new Avalonia.Thickness(0, 2) };
            host.Children.Add(expander);

            void UpdateEnabledState()
            {
                shadow.IsEnabled = hasShadow.IsChecked == true;
                shadowDepth.IsEnabled = hasShadow.IsChecked == true;
                underline.IsEnabled = under.IsChecked == true;
                underlineThickness.IsEnabled = under.IsChecked == true;
            }
            hasShadow.IsCheckedChanged += (_, _) => UpdateEnabledState();
            under.IsCheckedChanged += (_, _) => UpdateEnabledState();

            _stateEditors[i] = new StateEditor
            {
                Text=text, Outline=outline, Shadow=shadow, Underline=underline,
                OutlineSize=outlineSize, ShadowDepth=shadowDepth, UnderlineThickness=underlineThickness, Opacity=opacity,
                HasShadow=hasShadow, Bold=bold, Italic=italic, UnderlineEnabled=under, Strike=strike
            };
        }
    }

    private void BuildPitchEditors()
    {
        var host = F<StackPanel>("PitchColorPanel");
        foreach (var name in new[] { "Heiban", "Atamadaka", "Nakadaka", "Odaka", "Unknown" })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,Auto"), Margin = new Avalonia.Thickness(0, 0, 0, 6) };
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#A1A1AA"), FontSize = 13 };
            var picker = Picker(); picker.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(picker, 1); row.Children.Add(label); row.Children.Add(picker);
            host.Children.Add(row); _pitchPickers[name] = picker;
        }
    }

    private void BuildPopupKeybindEditors()
    {
        AddKeybindRows(F<Grid>("ReviewKeybindGrid"),
        [
            ("ReviewAgain", "Again / Fail", "e.g. 1"), ("ReviewHard", "Hard", "e.g. 2"),
            ("ReviewGood", "Good / Pass", "e.g. 3"), ("ReviewEasy", "Easy", "e.g. 4")
        ]);
        AddKeybindRows(F<Grid>("StateKeybindGrid"),
        [
            ("Mine", "Mine", "e.g. d"), ("NeverForget", "Never Forget", "e.g. m"), ("Blacklist", "Blacklist", "e.g. b"),
            ("Suspend", "Suspend", "e.g. s"), ("Forget", "Forget", "e.g. f"),
            ("RotateForward", "Rotate forward", "unbound"), ("RotateBackward", "Rotate backward", "unbound")
        ]);
    }

    private void AddKeybindRows(Grid grid, IReadOnlyList<(string Key, string Label, string Placeholder)> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var l = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#A1A1AA"), FontSize = 13 };
            var b = new TextBox { Watermark = rows[i].Placeholder, MaxWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(l, i); Grid.SetColumn(l, 0); Grid.SetRow(b, i); Grid.SetColumn(b, 1);
            grid.Children.Add(l); grid.Children.Add(b); _popupKeyBoxes[rows[i].Key] = b;
        }
    }

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#71717A"), Margin = new Avalonia.Thickness(0, 4, 0, 0)
    };
    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#A1A1AA"), FontSize = 13
    };
    private static void AddGridLabel(Grid grid, string text, int row)
    {
        var label = FieldLabel(text); Grid.SetRow(label, row); grid.Children.Add(label);
    }
    private static void AddGridControl(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row); Grid.SetColumn(control, 1); grid.Children.Add(control);
    }
    private static StackPanel SliderWithValue(Slider slider, string format, double width, string suffix = "")
    {
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = width, Foreground = Brush("#FFFFFF") };
        void Update() => value.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture) + suffix;
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) Update(); };
        Update();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(slider); panel.Children.Add(value); return panel;
    }
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static CheckBox Check(string text) => new() { Content = text, Foreground = Brush("#E4E4E7") };
    private static ColorPicker Picker() => new() { IsAlphaVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
    private static Slider StateSlider(double min, double max, double inc, double width) => new() { Minimum = min, Maximum = max, TickFrequency = inc, Width = width, IsSnapToTickEnabled = true };

    private void PopulateFromSettings()
    {
        _loading = true;
        try
        {
            var s = _runtime.Settings;
            F<TextBox>("ApiBaseBox").Text=s.ApiBaseUrl; F<TextBox>("ApiKeyBox").Text=s.ApiKey; F<NumericUpDown>("ApiTimeoutBox").Value=s.ApiTimeoutSeconds; F<CheckBox>("UpdateCheckToggle").IsChecked=s.UpdateCheckEnabled;
            F<TextBox>("MpcPathBox").Text=s.MpcPath; F<TextBox>("FfmpegPathBox").Text=s.FfmpegPath; F<TextBox>("FfprobePathBox").Text=s.FfprobePath;
            PopulateAppearanceOnly();

            F<ToggleSwitch>("IPlusOneToggle").IsChecked=s.IPlusOneEnabled; N("IPlusOneMinBox",s.IPlusOneMinTokens); N("IPlusOneFreqBox",s.IPlusOneMaxFrequencyRank);
            F<ToggleSwitch>("FrequencyToggle").IsChecked=s.FrequencyMarkingEnabled; N("FrequencyTopBox",s.FrequencyTopN); F<CheckBox>("FrequencyAllStatesCheck").IsChecked=s.FrequencyMarkAllStates;
            F<ToggleSwitch>("BlurToggle").IsChecked=s.BlurEnabled; F<Slider>("BlurStrengthSlider").Value=s.BlurStrength; F<CheckBox>("BlurRevealCheck").IsChecked=s.BlurRevealOnHover; N("BlurDelayBox",s.BlurRevealDelayMs);
            for(var i=0;i<8;i++) F<CheckBox>("BlurState"+i).IsChecked=s.BlurStates.Contains(i);
            F<ToggleSwitch>("AutopauseToggle").IsChecked=s.AutopauseEnabled; N("AutopauseDelayBox",s.AutopauseDelayMs);
            F<ToggleSwitch>("MiningToggle").IsChecked=s.MiningEnabled; F<CheckBox>("MiningCaptureSentenceCheck").IsChecked=s.MiningCaptureSentence;
            F<CheckBox>("MiningToDeckCheck").IsChecked=s.MiningToStudyDeck; F<CheckBox>("MiningAutoReviewCheck").IsChecked=s.MiningAutoOnReview; F<CheckBox>("MiningSkipPresentCheck").IsChecked=s.MiningSkipIfPresent;
            F<ComboBox>("DoubleClickActionBox").ItemsSource=Enum.GetNames<DoubleClickAction>(); F<ComboBox>("DoubleClickActionBox").SelectedItem=s.DoubleClickAction.ToString();
            UpdateMiningDecks(_runtime.StudyDecks);
            F<ToggleSwitch>("ReviewsToggle").IsChecked=s.ReviewsEnabled;

            F<ToggleSwitch>("MediaCaptureToggle").IsChecked=s.MediaCaptureEnabled; F<CheckBox>("MediaImageCheck").IsChecked=s.MediaCaptureImage; F<CheckBox>("MediaAnimatedCheck").IsChecked=s.MediaCaptureImageAnimated; F<CheckBox>("MediaAudioCheck").IsChecked=s.MediaCaptureAudio;
            F<RadioButton>("MediaImageCurrentRadio").IsChecked=s.MediaImageSource==MediaImageSource.MpvFrame; F<RadioButton>("MediaImageMidpointRadio").IsChecked=s.MediaImageSource==MediaImageSource.SubtitleMidpoint;
            F<RadioButton>("MediaBurnNoneRadio").IsChecked=s.MediaSubtitleBurn==MediaSubtitleBurn.None; F<RadioButton>("MediaBurnOriginalRadio").IsChecked=s.MediaSubtitleBurn==MediaSubtitleBurn.Original; F<RadioButton>("MediaBurnColoredRadio").IsChecked=s.MediaSubtitleBurn==MediaSubtitleBurn.Colored;
            F<CheckBox>("MediaStereoCheck").IsChecked=s.MediaAudioStereo; F<CheckBox>("MediaAutoTrimCheck").IsChecked=s.MediaAudioAutoTrim; F<CheckBox>("MediaReviewCheck").IsChecked=s.MediaReviewPopup; N("MediaContextLinesBox",s.MediaSentenceContextLines);
            F<RadioButton>("MediaOverwriteAlwaysRadio").IsChecked=s.MediaOverwritePrompt==MediaOverwritePrompt.Always; F<RadioButton>("MediaOverwriteSessionRadio").IsChecked=s.MediaOverwritePrompt==MediaOverwritePrompt.OncePerSession; F<RadioButton>("MediaOverwriteNeverRadio").IsChecked=s.MediaOverwritePrompt==MediaOverwritePrompt.Never;
            N("MediaImageMaxEdgeBox",s.MediaImageMaxEdge); N("MediaImageQualityBox",s.MediaImageQuality); N("MediaAnimMaxFramesBox",s.MediaAnimMaxFrames); N("MediaAnimTargetFpsBox",s.MediaAnimTargetFps); N("MediaAnimMinFpsBox",s.MediaAnimMinFps); N("MediaAnimMaxEdgeBox",s.MediaAnimMaxEdge); N("MediaAnimQualityBox",s.MediaAnimQuality); N("MediaAnimMaxMbBox",s.MediaAnimMaxBytes/1_000_000d);
            N("MediaAudioBitrateBox",s.MediaAudioBitrateKbps); N("MediaAudioMaxMbBox",s.MediaAudioMaxBytes/1_000_000d); N("MediaAudioLeadBox",s.MediaAudioPadLeadMs); N("MediaAudioTailBox",s.MediaAudioPadTailMs); N("MediaAudioMarginBox",s.MediaAudioWindowMarginSeconds);
            UpdateJitenPlus(_runtime.JitenPlus);
            F<TextBlock>("MiningFfmpegStatusText").Text=File.Exists(s.FfmpegPath)?s.FfmpegPath:"ffmpeg not found — media mining will be skipped";
            F<CheckBox>("AutoLoadSubtitlesCheck").IsChecked=s.AutoLoadSubtitles;
            F<RadioButton>("PopupHoverRadio").IsChecked=s.PopupTrigger==PopupTriggerMode.Hover; F<RadioButton>("PopupClickRadio").IsChecked=s.PopupTrigger==PopupTriggerMode.Click;
            N("PopupHoverDelayBox",s.PopupHoverDelayMs); N("PopupSwitchDelayBox",s.PopupSwitchDelayMs); F<CheckBox>("PopupAutoHideCheck").IsChecked=s.PopupAutoHide; N("PopupHideDelayBox",s.PopupAutoHideDelayMs); F<CheckBox>("PopupHideAfterActionCheck").IsChecked=s.PopupHideAfterAction;
            F<RadioButton>("PopupAboveRadio").IsChecked=s.PopupPosition==PopupPositionMode.AboveSubtitle; F<RadioButton>("PopupBelowRadio").IsChecked=s.PopupPosition==PopupPositionMode.BelowSubtitle; F<RadioButton>("PopupFixedRadio").IsChecked=s.PopupPosition==PopupPositionMode.Fixed;
            F<ComboBox>("PopupAnchorBox").ItemsSource=Enum.GetNames<PopupAnchor>(); F<ComboBox>("PopupAnchorBox").SelectedItem=s.PopupFixedAnchor.ToString(); N("PopupOffsetBox",s.PopupOffsetPx);
            F<Slider>("PopupFontScaleSlider").Value=s.PopupFontScale; F<Slider>("PopupBgOpacitySlider").Value=s.PopupBgOpacity; F<ColorPicker>("PopupBgColorPicker").Color=SafeColor(s.PopupBgColor); F<Slider>("PopupMaxWidthBox").Value=s.PopupMaxWidthPx; F<TextBlock>("PopupMaxWidthValue").Text=$"{s.PopupMaxWidthPx:0}px"; N("PopupMaxMeaningsBox",s.PopupMaxMeanings);
            F<CheckBox>("PopupFuriganaCheck").IsChecked=s.PopupFurigana; F<CheckBox>("PopupPitchCheck").IsChecked=s.PopupShowPitch; F<CheckBox>("PopupPitchDiagramCheck").IsChecked=s.PopupPitchDiagram; F<CheckBox>("PopupFrequencyCheck").IsChecked=s.PopupShowFrequency; F<CheckBox>("PopupConjugationCheck").IsChecked=s.PopupShowConjugation;
            F<CheckBox>("PopupStateActionsCheck").IsChecked=s.PopupShowStateActions; F<CheckBox>("PopupNeverForgetCheck").IsChecked=s.PopupShowNeverForget; F<CheckBox>("PopupBlacklistCheck").IsChecked=s.PopupShowBlacklist; F<CheckBox>("PopupSuspendCheck").IsChecked=s.PopupShowSuspend; F<CheckBox>("PopupForgetCheck").IsChecked=s.PopupShowForget;
            F<CheckBox>("PopupDeckCheck").IsChecked=s.PopupShowDeckMembership; F<CheckBox>("PopupReviewCheck").IsChecked=s.PopupShowReview; F<CheckBox>("PopupTwoGradesCheck").IsChecked=s.PopupUseTwoGrades; F<CheckBox>("PopupActionsBottomCheck").IsChecked=s.PopupMoveActionsBottom; F<CheckBox>("PopupHeadwordLinkCheck").IsChecked=!s.PopupDisableHeadwordLink;
            F<CheckBox>("RotateStatesToggle").IsChecked=s.RotateStatesEnabled; F<CheckBox>("PopupRotateActionsCheck").IsChecked=s.PopupShowRotateActions; F<CheckBox>("RotateCycleCheck").IsChecked=s.RotateCycle; F<CheckBox>("RotateNeverForgetCheck").IsChecked=s.RotateCycleNeverForget; F<CheckBox>("RotateBlacklistCheck").IsChecked=s.RotateCycleBlacklist; F<CheckBox>("RotateSuspendedCheck").IsChecked=s.RotateCycleSuspended;

            foreach(var kv in _popupKeyBoxes) kv.Value.Text=s.PopupKeybinds.GetValueOrDefault(kv.Key,"");
            F<TextBox>("PrevSubKeyBox").Text=s.KeybindPrevSub; F<TextBox>("NextSubKeyBox").Text=s.KeybindNextSub; F<TextBox>("LoopSubKeyBox").Text=s.KeybindLoopSub; F<TextBox>("SubtitleEarlierKeyBox").Text=s.KeybindSubtitleEarlier; F<TextBox>("SubtitleLaterKeyBox").Text=s.KeybindSubtitleLater; N("SubtitleOffsetStepBox",s.SubtitleOffsetStepMs);
            F<CheckBox>("AutostartToggle").IsChecked=s.PluginAutostart; F<TextBox>("StartKeyBox").Text=s.PluginStartKey; N("CacheSizeBox",s.CacheSize); F<CheckBox>("PreparseToggle").IsChecked=s.PreparseEnabled;
            F<Slider>("MouseZoneSlider").Value=s.MouseZonePercent; F<CheckBox>("SubtitleNavButtonsCheck").IsChecked=s.SubtitleNavButtonsEnabled;
            F<CheckBox>("StatusOverlayCheck").IsChecked=s.StatusOverlayEnabled; F<CheckBox>("DebugLoggingCheck").IsChecked=s.DebugLogging; F<CheckBox>("ShowHitboxesCheck").IsChecked=s.DebugShowHitboxes; N("OverlayHeightBox",s.OverlayHeight);
            F<TextBlock>("ConfigPathText").Text=_runtime.SettingsPath; F<CheckBox>("AutoSaveToggle").IsChecked=s.AutoSaveSettings;
            UpdateConnection(); UpdateMedia(); UpdateTracks(_runtime.SubtitleStreams); UpdateUpdateUi(_runtime.PendingUpdate); UpdateConditionalVisibility(); UpdateFontPreview();
        }
        finally { _loading=false; }
    }

    private void PopulateAppearanceOnly()
    {
        var s=_runtime.Settings;
        _font.ItemsSource=_runtime.GetJapaneseFonts(); _font.SelectedItem=s.FontFamily; _font.Text=s.FontFamily;
        _theme.ItemsSource=ThemePresets.Names; _theme.SelectedItem=s.Theme; F<StackPanel>("CustomThemePanel").IsVisible=string.Equals(s.Theme,"Custom",StringComparison.OrdinalIgnoreCase);
        F<Slider>("FontSizeSlider").Value=s.FontSize; F<TextBlock>("FontSizeValue").Text=$"{s.FontSize:0}"; F<Slider>("BorderSizeBox").Value=s.BorderSize; F<TextBlock>("BorderSizeValue").Text=$"{s.BorderSize:0.0}";
        _alignment=s.SubtitleAlignment; UpdateAlignmentButtons(); F<Slider>("MarginXSlider").Value=s.SubtitleMarginX; F<TextBlock>("MarginXValue").Text=$"{s.SubtitleMarginX:0}"; F<Slider>("MarginYSlider").Value=s.SubtitleMarginY; F<TextBlock>("MarginYValue").Text=$"{s.SubtitleMarginY:0}"; F<CheckBox>("SingleLineCheck").IsChecked=s.SubtitleSingleLine;
        F<ToggleSwitch>("PitchColorToggle").IsChecked=s.PitchColoringEnabled; F<RadioButton>("PitchTextRadio").IsChecked=s.PitchIndicator==PitchIndicatorMode.Text; F<RadioButton>("PitchUnderlineRadio").IsChecked=s.PitchIndicator==PitchIndicatorMode.Underline; F<Slider>("PitchUnderlineSlider").Value=s.PitchUnderlineThickness;
        foreach(var kv in _pitchPickers) kv.Value.Color=SafeColor(s.PitchStyles.GetValueOrDefault(kv.Key,"#D4D4D8"));
        foreach(var kv in _stateEditors)
        {
            var c=s.GetCustomState(kv.Key); var e=kv.Value; e.Text.Color=SafeColor(c.TextColor); e.Outline.Color=SafeColor(c.OutlineColor); e.Shadow.Color=SafeColor(c.ShadowColor); e.Underline.Color=SafeColor(c.UnderlineColor); e.OutlineSize.Value=c.OutlineSize; e.ShadowDepth.Value=c.ShadowDepth; e.UnderlineThickness.Value=c.UnderlineThickness; e.Opacity.Value=c.TextOpacityPercent; e.HasShadow.IsChecked=c.HasShadow; e.Bold.IsChecked=c.Bold; e.Italic.IsChecked=c.Italic; e.UnderlineEnabled.IsChecked=c.Underline; e.Strike.IsChecked=c.Strikethrough;
        }
    }

    private void PullSettings()
    {
        var s=_runtime.Settings;
        s.ApiBaseUrl=T("ApiBaseBox",s.ApiBaseUrl); s.ApiKey=F<TextBox>("ApiKeyBox").Text??""; s.ApiTimeoutSeconds=I("ApiTimeoutBox",30,5,300); s.UpdateCheckEnabled=F<CheckBox>("UpdateCheckToggle").IsChecked==true;
        s.MpcPath=T("MpcPathBox",s.MpcPath); s.FfmpegPath=T("FfmpegPathBox",s.FfmpegPath); s.FfprobePath=T("FfprobePathBox",s.FfprobePath);
        var font=!string.IsNullOrWhiteSpace(_font.Text)?_font.Text!.Trim():_font.SelectedItem as string; if(!string.IsNullOrWhiteSpace(font))s.FontFamily=font; if(_theme.SelectedItem is string th)s.Theme=th;
        s.FontSize=F<Slider>("FontSizeSlider").Value; s.BorderSize=Math.Clamp(F<Slider>("BorderSizeBox").Value,0,10); s.SubtitleAlignment=_alignment; s.SubtitleMarginX=F<Slider>("MarginXSlider").Value; s.SubtitleMarginY=F<Slider>("MarginYSlider").Value; s.SubtitleSingleLine=F<CheckBox>("SingleLineCheck").IsChecked==true;
        s.PitchColoringEnabled=F<ToggleSwitch>("PitchColorToggle").IsChecked==true; s.PitchIndicator=F<RadioButton>("PitchUnderlineRadio").IsChecked==true?PitchIndicatorMode.Underline:PitchIndicatorMode.Text; s.PitchUnderlineThickness=F<Slider>("PitchUnderlineSlider").Value; foreach(var kv in _pitchPickers)s.PitchStyles[kv.Key]=Hex(kv.Value.Color);
        foreach(var kv in _stateEditors){var c=s.GetCustomState(kv.Key);var e=kv.Value;c.TextColor=Hex(e.Text.Color);c.OutlineColor=Hex(e.Outline.Color);c.ShadowColor=Hex(e.Shadow.Color);c.UnderlineColor=Hex(e.Underline.Color);c.OutlineSize=e.OutlineSize.Value;c.ShadowDepth=e.ShadowDepth.Value;c.UnderlineThickness=e.UnderlineThickness.Value;c.TextOpacityPercent=(int)e.Opacity.Value;c.HasShadow=e.HasShadow.IsChecked==true;c.Bold=e.Bold.IsChecked==true;c.Italic=e.Italic.IsChecked==true;c.Underline=e.UnderlineEnabled.IsChecked==true;c.Strikethrough=e.Strike.IsChecked==true;}
        s.IPlusOneEnabled=F<ToggleSwitch>("IPlusOneToggle").IsChecked==true;s.IPlusOneMinTokens=I("IPlusOneMinBox",3,2,10);s.IPlusOneMaxFrequencyRank=I("IPlusOneFreqBox",15000,1000,50000);s.FrequencyMarkingEnabled=F<ToggleSwitch>("FrequencyToggle").IsChecked==true;s.FrequencyTopN=I("FrequencyTopBox",10000,1000,50000);s.FrequencyMarkAllStates=F<CheckBox>("FrequencyAllStatesCheck").IsChecked==true;
        s.BlurEnabled=F<ToggleSwitch>("BlurToggle").IsChecked==true;s.BlurStrength=F<Slider>("BlurStrengthSlider").Value;s.BlurRevealOnHover=F<CheckBox>("BlurRevealCheck").IsChecked==true;s.BlurRevealDelayMs=I("BlurDelayBox",200,0,1000);s.BlurStates=Enumerable.Range(0,8).Where(i=>F<CheckBox>("BlurState"+i).IsChecked==true).ToList();s.AutopauseEnabled=F<ToggleSwitch>("AutopauseToggle").IsChecked==true;s.AutopauseDelayMs=I("AutopauseDelayBox",0,0,2000);
        s.MiningEnabled=F<ToggleSwitch>("MiningToggle").IsChecked==true;s.MiningCaptureSentence=C("MiningCaptureSentenceCheck");s.MiningToStudyDeck=C("MiningToDeckCheck");s.MiningAutoOnReview=C("MiningAutoReviewCheck");s.MiningSkipIfPresent=C("MiningSkipPresentCheck");if(F<ComboBox>("MiningDeckBox").SelectedItem is StudyDeckInfo deck)s.MiningStudyDeckId=deck.Id;if(Enum.TryParse<DoubleClickAction>(F<ComboBox>("DoubleClickActionBox").SelectedItem as string,out var dc))s.DoubleClickAction=dc;s.ReviewsEnabled=F<ToggleSwitch>("ReviewsToggle").IsChecked==true;
        s.MediaCaptureEnabled=F<ToggleSwitch>("MediaCaptureToggle").IsChecked==true;s.MediaCaptureImage=C("MediaImageCheck");s.MediaCaptureImageAnimated=C("MediaAnimatedCheck");s.MediaCaptureAudio=C("MediaAudioCheck");s.MediaImageSource=F<RadioButton>("MediaImageMidpointRadio").IsChecked==true?MediaImageSource.SubtitleMidpoint:MediaImageSource.MpvFrame;s.MediaSubtitleBurn=F<RadioButton>("MediaBurnColoredRadio").IsChecked==true?MediaSubtitleBurn.Colored:F<RadioButton>("MediaBurnOriginalRadio").IsChecked==true?MediaSubtitleBurn.Original:MediaSubtitleBurn.None;s.MediaAudioStereo=C("MediaStereoCheck");s.MediaAudioAutoTrim=C("MediaAutoTrimCheck");s.MediaReviewPopup=C("MediaReviewCheck");s.MediaSentenceContextLines=I("MediaContextLinesBox",2,0,5);s.MediaOverwritePrompt=F<RadioButton>("MediaOverwriteNeverRadio").IsChecked==true?MediaOverwritePrompt.Never:F<RadioButton>("MediaOverwriteSessionRadio").IsChecked==true?MediaOverwritePrompt.OncePerSession:MediaOverwritePrompt.Always;
        s.MediaImageMaxEdge=I("MediaImageMaxEdgeBox",1600,640,2560);s.MediaImageQuality=I("MediaImageQualityBox",95,40,100);s.MediaAnimMaxFrames=I("MediaAnimMaxFramesBox",280,30,300);s.MediaAnimTargetFps=I("MediaAnimTargetFpsBox",15,5,30);s.MediaAnimMinFps=I("MediaAnimMinFpsBox",5,1,30);s.MediaAnimMaxEdge=I("MediaAnimMaxEdgeBox",960,320,1600);s.MediaAnimQuality=I("MediaAnimQualityBox",82,20,100);s.MediaAnimMaxBytes=(int)(D("MediaAnimMaxMbBox",2.5,.5,5)*1_000_000);s.MediaAudioBitrateKbps=I("MediaAudioBitrateBox",48,24,128);s.MediaAudioMaxBytes=(int)(D("MediaAudioMaxMbBox",1.5,.2,5)*1_000_000);s.MediaAudioPadLeadMs=I("MediaAudioLeadBox",250,0,2000);s.MediaAudioPadTailMs=I("MediaAudioTailBox",350,0,2000);s.MediaAudioWindowMarginSeconds=D("MediaAudioMarginBox",5,1,20);
        s.AutoLoadSubtitles=F<CheckBox>("AutoLoadSubtitlesCheck").IsChecked==true;
        s.PopupTrigger=F<RadioButton>("PopupClickRadio").IsChecked==true?PopupTriggerMode.Click:PopupTriggerMode.Hover;s.PopupHoverDelayMs=I("PopupHoverDelayBox",30,0,3000);s.PopupSwitchDelayMs=I("PopupSwitchDelayBox",250,0,2000);s.PopupAutoHide=F<CheckBox>("PopupAutoHideCheck").IsChecked==true;s.PopupAutoHideDelayMs=I("PopupHideDelayBox",500,0,5000);s.PopupHideAfterAction=F<CheckBox>("PopupHideAfterActionCheck").IsChecked==true;
        s.PopupPosition=F<RadioButton>("PopupFixedRadio").IsChecked==true?PopupPositionMode.Fixed:F<RadioButton>("PopupBelowRadio").IsChecked==true?PopupPositionMode.BelowSubtitle:PopupPositionMode.AboveSubtitle;if(Enum.TryParse<PopupAnchor>(F<ComboBox>("PopupAnchorBox").SelectedItem as string,out var pa))s.PopupFixedAnchor=pa;s.PopupOffsetPx=I("PopupOffsetBox",60,0,600);s.PopupFontScale=F<Slider>("PopupFontScaleSlider").Value;s.PopupBgOpacity=(int)F<Slider>("PopupBgOpacitySlider").Value;s.PopupBgColor=Hex(F<ColorPicker>("PopupBgColorPicker").Color);s.PopupMaxWidthPx=Math.Clamp(F<Slider>("PopupMaxWidthBox").Value,250,1200);s.PopupMaxMeanings=I("PopupMaxMeaningsBox",10,1,20);
        s.PopupFurigana=C("PopupFuriganaCheck");s.PopupShowPitch=C("PopupPitchCheck");s.PopupPitchDiagram=C("PopupPitchDiagramCheck");s.PopupShowFrequency=C("PopupFrequencyCheck");s.PopupShowConjugation=C("PopupConjugationCheck");s.PopupShowStateActions=C("PopupStateActionsCheck");s.PopupShowNeverForget=C("PopupNeverForgetCheck");s.PopupShowBlacklist=C("PopupBlacklistCheck");s.PopupShowSuspend=C("PopupSuspendCheck");s.PopupShowForget=C("PopupForgetCheck");s.PopupShowDeckMembership=C("PopupDeckCheck");s.PopupShowReview=C("PopupReviewCheck");s.PopupUseTwoGrades=C("PopupTwoGradesCheck");s.PopupMoveActionsBottom=C("PopupActionsBottomCheck");s.PopupDisableHeadwordLink=!C("PopupHeadwordLinkCheck");s.RotateStatesEnabled=F<CheckBox>("RotateStatesToggle").IsChecked==true;s.PopupShowRotateActions=C("PopupRotateActionsCheck");s.RotateCycle=C("RotateCycleCheck");s.RotateCycleNeverForget=C("RotateNeverForgetCheck");s.RotateCycleBlacklist=C("RotateBlacklistCheck");s.RotateCycleSuspended=C("RotateSuspendedCheck");
        foreach(var kv in _popupKeyBoxes)s.PopupKeybinds[kv.Key]=(kv.Value.Text??"").Trim();s.KeybindPrevSub=T("PrevSubKeyBox",s.KeybindPrevSub);s.KeybindNextSub=T("NextSubKeyBox",s.KeybindNextSub);s.KeybindLoopSub=T("LoopSubKeyBox",s.KeybindLoopSub);s.KeybindSubtitleEarlier=T("SubtitleEarlierKeyBox",s.KeybindSubtitleEarlier);s.KeybindSubtitleLater=T("SubtitleLaterKeyBox",s.KeybindSubtitleLater);s.SubtitleOffsetStepMs=I("SubtitleOffsetStepBox",10,1,5000);
        s.PluginAutostart=F<CheckBox>("AutostartToggle").IsChecked==true;s.PluginStartKey=T("StartKeyBox",s.PluginStartKey);s.CacheSize=I("CacheSizeBox",2000,500,10000);s.PreparseEnabled=F<CheckBox>("PreparseToggle").IsChecked==true;s.MouseZonePercent=(int)F<Slider>("MouseZoneSlider").Value;s.SubtitleNavButtonsEnabled=C("SubtitleNavButtonsCheck");s.StatusOverlayEnabled=C("StatusOverlayCheck");s.DebugLogging=C("DebugLoggingCheck");s.DebugShowHitboxes=C("ShowHitboxesCheck");s.OverlayHeight=D("OverlayHeightBox",230,80,600);s.AutoSaveSettings=F<CheckBox>("AutoSaveToggle").IsChecked==true;
    }

    private async Task AutoSaveTickAsync()
    {
        if(_loading||F<CheckBox>("AutoSaveToggle").IsChecked!=true)return;
        var before=JsonSerializer.Serialize(_runtime.Settings);PullSettings();var after=JsonSerializer.Serialize(_runtime.Settings);if(before==after)return;_runtime.SaveSettingsQuietly();await _runtime.RerenderCurrentCueAsync();
    }

    private void SaveIfAuto(){if(!_loading&&F<CheckBox>("AutoSaveToggle").IsChecked==true){PullSettings();_runtime.SaveSettingsQuietly();}}
    private void SwitchPanel(int index){for(var i=0;i<_panels.Length;i++)_panels[i].IsVisible=i==Math.Clamp(index,0,_panels.Length-1);}

    private void ResetCurrentSection()
    {
        var d=new AppSettings();var s=_runtime.Settings;var i=F<ListBox>("NavList").SelectedIndex;
        if(i==0){s.ApiTimeoutSeconds=d.ApiTimeoutSeconds;s.UpdateCheckEnabled=d.UpdateCheckEnabled;}
        else if(i==1){s.FontFamily=d.FontFamily;s.FontSize=d.FontSize;s.BorderSize=d.BorderSize;s.SubtitleAlignment=d.SubtitleAlignment;s.SubtitleMarginX=d.SubtitleMarginX;s.SubtitleMarginY=d.SubtitleMarginY;s.SubtitleSingleLine=d.SubtitleSingleLine;s.Theme=d.Theme;s.CustomThemeColors=AppSettings.CreateDefaultCustomTheme();s.PitchColoringEnabled=d.PitchColoringEnabled;s.PitchIndicator=d.PitchIndicator;s.PitchUnderlineThickness=d.PitchUnderlineThickness;s.PitchStyles=d.PitchStyles;}
        else if(i==2){s.IPlusOneEnabled=d.IPlusOneEnabled;s.IPlusOneMinTokens=d.IPlusOneMinTokens;s.IPlusOneMaxFrequencyRank=d.IPlusOneMaxFrequencyRank;s.FrequencyMarkingEnabled=d.FrequencyMarkingEnabled;s.FrequencyTopN=d.FrequencyTopN;s.FrequencyMarkAllStates=d.FrequencyMarkAllStates;s.BlurEnabled=d.BlurEnabled;s.BlurStrength=d.BlurStrength;s.BlurRevealOnHover=d.BlurRevealOnHover;s.BlurStates=d.BlurStates;s.BlurRevealDelayMs=d.BlurRevealDelayMs;s.AutopauseEnabled=d.AutopauseEnabled;s.AutopauseDelayMs=d.AutopauseDelayMs;s.MiningEnabled=d.MiningEnabled;s.MiningCaptureSentence=d.MiningCaptureSentence;s.MiningStudyDeckId=d.MiningStudyDeckId;s.MiningToStudyDeck=d.MiningToStudyDeck;s.MiningAutoOnReview=d.MiningAutoOnReview;s.MiningSkipIfPresent=d.MiningSkipIfPresent;s.DoubleClickAction=d.DoubleClickAction;s.ReviewsEnabled=d.ReviewsEnabled;}
        else if(i==3){s.MediaCaptureEnabled=d.MediaCaptureEnabled;s.MediaCaptureImage=d.MediaCaptureImage;s.MediaCaptureImageAnimated=d.MediaCaptureImageAnimated;s.MediaCaptureAudio=d.MediaCaptureAudio;s.MediaReviewPopup=d.MediaReviewPopup;s.MediaOverwritePrompt=d.MediaOverwritePrompt;s.MediaImageSource=d.MediaImageSource;s.MediaSubtitleBurn=d.MediaSubtitleBurn;s.MediaImageMaxEdge=d.MediaImageMaxEdge;s.MediaImageQuality=d.MediaImageQuality;s.MediaAnimMaxFrames=d.MediaAnimMaxFrames;s.MediaAnimTargetFps=d.MediaAnimTargetFps;s.MediaAnimMinFps=d.MediaAnimMinFps;s.MediaAnimMaxEdge=d.MediaAnimMaxEdge;s.MediaAnimQuality=d.MediaAnimQuality;s.MediaAnimMaxBytes=d.MediaAnimMaxBytes;s.MediaAudioBitrateKbps=d.MediaAudioBitrateKbps;s.MediaAudioStereo=d.MediaAudioStereo;s.MediaAudioMaxBytes=d.MediaAudioMaxBytes;s.MediaAudioAutoTrim=d.MediaAudioAutoTrim;s.MediaAudioPadLeadMs=d.MediaAudioPadLeadMs;s.MediaAudioPadTailMs=d.MediaAudioPadTailMs;s.MediaAudioWindowMarginSeconds=d.MediaAudioWindowMarginSeconds;s.MediaSentenceContextLines=d.MediaSentenceContextLines;s.AutoLoadSubtitles=d.AutoLoadSubtitles;}
        else if(i==4){s.PopupTrigger=d.PopupTrigger;s.PopupHoverDelayMs=d.PopupHoverDelayMs;s.PopupSwitchDelayMs=d.PopupSwitchDelayMs;s.PopupAutoHide=d.PopupAutoHide;s.PopupAutoHideDelayMs=d.PopupAutoHideDelayMs;s.PopupHideAfterAction=d.PopupHideAfterAction;s.PopupPosition=d.PopupPosition;s.PopupFixedAnchor=d.PopupFixedAnchor;s.PopupOffsetPx=d.PopupOffsetPx;s.PopupFontScale=d.PopupFontScale;s.PopupBgOpacity=d.PopupBgOpacity;s.PopupBgColor=d.PopupBgColor;s.PopupMaxWidthPx=d.PopupMaxWidthPx;s.PopupMaxMeanings=d.PopupMaxMeanings;s.PopupFurigana=d.PopupFurigana;s.PopupShowPitch=d.PopupShowPitch;s.PopupPitchDiagram=d.PopupPitchDiagram;s.PopupShowFrequency=d.PopupShowFrequency;s.PopupShowConjugation=d.PopupShowConjugation;s.PopupShowStateActions=d.PopupShowStateActions;s.PopupShowNeverForget=d.PopupShowNeverForget;s.PopupShowBlacklist=d.PopupShowBlacklist;s.PopupShowSuspend=d.PopupShowSuspend;s.PopupShowForget=d.PopupShowForget;s.PopupShowDeckMembership=d.PopupShowDeckMembership;s.PopupDisableHeadwordLink=d.PopupDisableHeadwordLink;s.PopupMoveActionsBottom=d.PopupMoveActionsBottom;s.PopupShowReview=d.PopupShowReview;s.PopupUseTwoGrades=d.PopupUseTwoGrades;s.RotateStatesEnabled=d.RotateStatesEnabled;s.PopupShowRotateActions=d.PopupShowRotateActions;s.RotateCycle=d.RotateCycle;s.RotateCycleNeverForget=d.RotateCycleNeverForget;s.RotateCycleBlacklist=d.RotateCycleBlacklist;s.RotateCycleSuspended=d.RotateCycleSuspended;}
        else if(i==5){s.PopupKeybinds=d.PopupKeybinds;s.KeybindPrevSub=d.KeybindPrevSub;s.KeybindNextSub=d.KeybindNextSub;s.KeybindLoopSub=d.KeybindLoopSub;s.KeybindSubtitleEarlier=d.KeybindSubtitleEarlier;s.KeybindSubtitleLater=d.KeybindSubtitleLater;s.SubtitleOffsetStepMs=d.SubtitleOffsetStepMs;}
        else if(i==6){s.PluginAutostart=d.PluginAutostart;s.PluginStartKey=d.PluginStartKey;s.CacheSize=d.CacheSize;s.PreparseEnabled=d.PreparseEnabled;s.MouseZonePercent=d.MouseZonePercent;s.SubtitleNavButtonsEnabled=d.SubtitleNavButtonsEnabled;s.StatusOverlayEnabled=d.StatusOverlayEnabled;s.DebugLogging=d.DebugLogging;s.DebugShowHitboxes=d.DebugShowHitboxes;s.AutoSaveSettings=d.AutoSaveSettings;}
        SetStatus("Current section reset to defaults.");
    }

    private void UpdateConditionalVisibility()
    {
        F<StackPanel>("IPlusOneOptionsPanel").IsVisible = F<ToggleSwitch>("IPlusOneToggle").IsChecked == true;
        F<StackPanel>("FrequencyOptionsPanel").IsVisible = F<ToggleSwitch>("FrequencyToggle").IsChecked == true;
        F<StackPanel>("BlurOptionsPanel").IsVisible = F<ToggleSwitch>("BlurToggle").IsChecked == true;
        F<StackPanel>("BlurRevealOptionsPanel").IsVisible = F<ToggleSwitch>("BlurToggle").IsChecked == true && F<CheckBox>("BlurRevealCheck").IsChecked == true;
        F<StackPanel>("AutopauseOptionsPanel").IsVisible = F<ToggleSwitch>("AutopauseToggle").IsChecked == true;
        F<StackPanel>("MiningOptionsPanel").IsVisible = F<ToggleSwitch>("MiningToggle").IsChecked == true;
        F<StackPanel>("MediaCaptureOptionsPanel").IsVisible = F<ToggleSwitch>("MediaCaptureToggle").IsChecked == true;
        F<StackPanel>("MediaImageOptionsPanel").IsVisible = F<ToggleSwitch>("MediaCaptureToggle").IsChecked == true && F<CheckBox>("MediaImageCheck").IsChecked == true;
        F<StackPanel>("MediaAudioOptionsPanel").IsVisible = F<ToggleSwitch>("MediaCaptureToggle").IsChecked == true && F<CheckBox>("MediaAudioCheck").IsChecked == true;
        F<StackPanel>("MediaReviewOptionsPanel").IsVisible = F<ToggleSwitch>("MediaCaptureToggle").IsChecked == true && F<CheckBox>("MediaReviewCheck").IsChecked == true;
        F<StackPanel>("PitchOptionsPanel").IsVisible = F<ToggleSwitch>("PitchColorToggle").IsChecked == true;
        F<StackPanel>("PitchUnderlineOptionsPanel").IsVisible = F<ToggleSwitch>("PitchColorToggle").IsChecked == true && F<RadioButton>("PitchUnderlineRadio").IsChecked == true;
        F<StackPanel>("PopupAutoHideOptionsPanel").IsVisible = F<CheckBox>("PopupAutoHideCheck").IsChecked == true;
        F<StackPanel>("PopupFixedOptionsPanel").IsVisible = F<RadioButton>("PopupFixedRadio").IsChecked == true;
        F<StackPanel>("PopupStateActionsOptionsPanel").IsVisible = F<CheckBox>("PopupStateActionsCheck").IsChecked == true;
        F<StackPanel>("PopupPitchDiagramPanel").IsVisible = F<CheckBox>("PopupPitchCheck").IsChecked == true;
        F<StackPanel>("RotateOptionsPanel").IsVisible = F<CheckBox>("RotateStatesToggle").IsChecked == true;
    }

    private void UpdateFontPreview()
    {
        var family = (_font.Text ?? _font.SelectedItem as string ?? "").Trim();
        if (family.Length == 0) return;
        try { F<TextBlock>("FontPreviewText").FontFamily = new FontFamily(family); } catch { }
    }

    private void ResetPitchColors(){var d=new AppSettings();foreach(var kv in _pitchPickers)kv.Value.Color=SafeColor(d.PitchStyles[kv.Key]);}
    private void UpdateAlignmentButtons(){for(var i=1;i<=9;i++)F<ToggleButton>("Align"+i).IsChecked=i==_alignment;}
    private void UpdateTracks(IReadOnlyList<SubtitleStreamInfo> tracks){_updatingTracks=true;try{_track.ItemsSource=tracks;_track.SelectedIndex=tracks.Count>0?0:-1;}finally{_updatingTracks=false;}}
    private void UpdateMiningDecks(IReadOnlyList<StudyDeckInfo> decks)
    {
        var box=F<ComboBox>("MiningDeckBox");var wanted=_runtime.Settings.MiningStudyDeckId;box.ItemsSource=decks;box.SelectedItem=decks.FirstOrDefault(d=>d.Id==wanted);
        F<TextBlock>("MiningDeckStatus").Text=decks.Count==0?"No study lists loaded.":box.SelectedItem is StudyDeckInfo d?$"Target: {d.Name}":"Choose a target list.";
    }
    private void UpdateJitenPlus(JitenPlusInfo plus)
    {
        F<TextBlock>("JitenPlusTierText").Text=plus.TierLabel;F<TextBlock>("JitenPlusQuotaText").Text=plus.QuotaLabel;F<TextBlock>("JitenPlusStatusText").Text=plus.Status;
        F<Border>("JitenPlusLockedBorder").IsVisible=!plus.IsPlus;
        F<StackPanel>("MediaJitenPlusPanel").IsEnabled=plus.IsPlus;
    }

    private void UpdateConnection()=>_version.Text="Connected version: "+(_runtime.Mpc.IsConnected&&!string.IsNullOrWhiteSpace(_runtime.Mpc.Version)?_runtime.Mpc.Version:"Not connected");
    private void UpdateMedia(){_media.Text=string.IsNullOrWhiteSpace(_runtime.Mpc.MediaPath)?"(none)":_runtime.Mpc.MediaPath;_subtitle.Text=string.IsNullOrWhiteSpace(_runtime.SubtitlePath)?"(none)":_runtime.SubtitlePath;}
    private void UpdateUpdateUi(UpdateInfo? info)
    {
        var button = F<Button>("InstallUpdateButton");
        button.IsVisible = info is not null;
        button.Content = info?.CanInstall == true ? "Install update" : "View release";
        _updateStatus.Text = info is null
            ? "No update available."
            : info.CanInstall
                ? $"{info.Name} is available."
                : $"{info.Name} is available, but its installer asset is not available yet.";
    }
    public void SetStatus(string text)=>_status.Text=text;

    private async Task BrowseIntoAsync(TextBox target,string title,IReadOnlyList<string> patterns){var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=title,AllowMultiple=false,FileTypeFilter=[new FilePickerFileType(title){Patterns=patterns}]});if(files.Count>0)target.Text=files[0].Path.LocalPath;}
    private async Task BrowseSubtitleAsync(){var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Load subtitle",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Subtitles"){Patterns=["*.ass","*.ssa","*.srt"]}]});if(files.Count>0)await _runtime.LoadExternalSubtitleAsync(files[0].Path.LocalPath);}
    private void TryAction(Action action){try{action();}catch(Exception ex){SetStatus(ex.Message);}}
    private bool C(string n)=>F<CheckBox>(n).IsChecked==true;
    private string T(string n,string fallback){var x=(F<TextBox>(n).Text??"").Trim();return x.Length==0?fallback:x;}
    private void N(string n,double value)=>F<NumericUpDown>(n).Value=(decimal)value;
    private int I(string n,int fallback,int min,int max)=>(int)Math.Clamp((double)(F<NumericUpDown>(n).Value??fallback),min,max);
    private double D(string n,double fallback,double min,double max)=>Math.Clamp((double)(F<NumericUpDown>(n).Value??((decimal)fallback)),min,max);
    private static Color SafeColor(string value){try{return Color.Parse(value);}catch{return Color.Parse("#D4D4D8");}}
    private static string Hex(Color c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
