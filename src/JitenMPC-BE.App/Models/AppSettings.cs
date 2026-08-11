using System.Text.Json.Serialization;

namespace JitenMpcBe.Models;

public enum PopupTriggerMode { Hover, Click }
public enum PopupPositionMode { AboveSubtitle, BelowSubtitle, Fixed }
public enum PopupAnchor { TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight }
public enum PitchIndicatorMode { Text, Underline }
public enum DoubleClickAction { None, Mine }
public enum MediaOverwritePrompt { Always, OncePerSession, Never }
public enum MediaImageSource { MpvFrame, SubtitleMidpoint }
public enum MediaSubtitleBurn { None, Original, Colored }

public sealed class CustomStateStyle
{
    public string TextColor { get; set; } = "#EEEEEE";
    public string OutlineColor { get; set; } = "#000000";
    public double OutlineSize { get; set; } = 3;
    public int TextOpacityPercent { get; set; } = 100;
    public bool HasShadow { get; set; }
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowDepth { get; set; } = 0;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string UnderlineColor { get; set; } = "#EEEEEE";
    public double UnderlineThickness { get; set; } = 2;
    public bool Strikethrough { get; set; }

    public CustomStateStyle Clone() => (CustomStateStyle)MemberwiseClone();
}

public sealed class AppSettings
{
    // MPC-BE-specific / bootstrap.
    [JsonPropertyName("mpc_path")] public string MpcPath { get; set; } = "";
    [JsonPropertyName("auto_load_subtitles")] public bool AutoLoadSubtitles { get; set; } = true;
    [JsonPropertyName("ffmpeg_path")] public string FfmpegPath { get; set; } = "";
    [JsonPropertyName("ffprobe_path")] public string FfprobePath { get; set; } = "";
    [JsonPropertyName("overlay_height")] public double OverlayHeight { get; set; } = 230;

    // General - mirrors JitenMPV.
    [JsonPropertyName("api_base_url")] public string ApiBaseUrl { get; set; } = "https://api.jiten.moe";
    [JsonPropertyName("api_key")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("api_timeout_seconds")] public int ApiTimeoutSeconds { get; set; } = 30;
    [JsonPropertyName("update_check_enabled")] public bool UpdateCheckEnabled { get; set; } = true;
    [JsonPropertyName("update_repository")] public string UpdateRepository { get; set; } = "SlothUchiha/JitenMPC-BE";
    [JsonPropertyName("last_update_check_utc")] public DateTime? LastUpdateCheckUtc { get; set; }

    // Appearance.
    [JsonPropertyName("font_family")] public string FontFamily { get; set; } = "Yu Gothic UI";
    [JsonPropertyName("font_size")] public double FontSize { get; set; } = 48;
    [JsonPropertyName("border_size")] public double BorderSize { get; set; } = 3;
    [JsonPropertyName("subtitle_alignment")] public int SubtitleAlignment { get; set; } = 2;
    [JsonPropertyName("subtitle_margin_x")] public double SubtitleMarginX { get; set; } = 0;
    [JsonPropertyName("subtitle_margin_y")] public double SubtitleMarginY { get; set; } = 50;
    [JsonPropertyName("subtitle_single_line")] public bool SubtitleSingleLine { get; set; }
    [JsonPropertyName("theme")] public string Theme { get; set; } = "Default";
    [JsonPropertyName("custom_theme_colors")] public Dictionary<string, CustomStateStyle> CustomThemeColors { get; set; } = CreateDefaultCustomTheme();
    [JsonPropertyName("pitch_coloring_enabled")] public bool PitchColoringEnabled { get; set; }
    [JsonPropertyName("pitch_indicator")] public PitchIndicatorMode PitchIndicator { get; set; } = PitchIndicatorMode.Text;
    [JsonPropertyName("pitch_underline_thickness")] public double PitchUnderlineThickness { get; set; } = 4;
    [JsonPropertyName("pitch_styles")] public Dictionary<string, string> PitchStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Heiban"] = "#60A5FA", ["Atamadaka"] = "#F87171", ["Nakadaka"] = "#FBBF24", ["Odaka"] = "#34D399", ["Unknown"] = "#D4D4D8"
    };

    // Features.
    [JsonPropertyName("i_plus_one_enabled")] public bool IPlusOneEnabled { get; set; } = true;
    [JsonPropertyName("i_plus_one_min_tokens")] public int IPlusOneMinTokens { get; set; } = 3;
    [JsonPropertyName("i_plus_one_max_frequency_rank")] public int IPlusOneMaxFrequencyRank { get; set; } = 15000;
    [JsonPropertyName("frequency_marking_enabled")] public bool FrequencyMarkingEnabled { get; set; }
    [JsonPropertyName("frequency_top_n")] public int FrequencyTopN { get; set; } = 10000;
    [JsonPropertyName("frequency_mark_all_states")] public bool FrequencyMarkAllStates { get; set; }
    [JsonPropertyName("blur_enabled")] public bool BlurEnabled { get; set; }
    [JsonPropertyName("blur_strength")] public double BlurStrength { get; set; } = 6;
    [JsonPropertyName("blur_reveal_on_hover")] public bool BlurRevealOnHover { get; set; } = true;
    [JsonPropertyName("blur_states")] public List<int> BlurStates { get; set; } = [2, 3, 5, 6];
    [JsonPropertyName("blur_reveal_delay_ms")] public int BlurRevealDelayMs { get; set; } = 200;
    [JsonPropertyName("autopause_enabled")] public bool AutopauseEnabled { get; set; } = true;
    // Legacy name retained as alias for old preview settings during deserialization/code migration.
    [JsonIgnore] public bool AutopauseOnHover { get => AutopauseEnabled; set => AutopauseEnabled = value; }
    [JsonPropertyName("autopause_on_hover")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyAutopauseOnHover { get => null; set { if (value.HasValue) AutopauseEnabled = value.Value; } }
    [JsonPropertyName("autopause_delay_ms")] public int AutopauseDelayMs { get; set; }

    // Mining - mirrors JitenMPV.
    [JsonPropertyName("mining_enabled")] public bool MiningEnabled { get; set; } = true;
    [JsonPropertyName("mining_capture_sentence")] public bool MiningCaptureSentence { get; set; } = true;
    [JsonPropertyName("mining_study_deck_id")] public int? MiningStudyDeckId { get; set; }
    [JsonPropertyName("mining_to_study_deck")] public bool MiningToStudyDeck { get; set; }
    [JsonPropertyName("mining_auto_on_review")] public bool MiningAutoOnReview { get; set; }
    [JsonPropertyName("mining_skip_if_present")] public bool MiningSkipIfPresent { get; set; } = true;
    [JsonPropertyName("double_click_action")] public DoubleClickAction DoubleClickAction { get; set; } = DoubleClickAction.Mine;

    [JsonPropertyName("reviews_enabled")] public bool ReviewsEnabled { get; set; } = true;

    // Jiten+ media mining - mirrors JitenMPV.
    [JsonPropertyName("media_capture_enabled")] public bool MediaCaptureEnabled { get; set; }
    [JsonPropertyName("media_capture_image")] public bool MediaCaptureImage { get; set; } = true;
    [JsonPropertyName("media_capture_image_animated")] public bool MediaCaptureImageAnimated { get; set; }
    [JsonPropertyName("media_capture_audio")] public bool MediaCaptureAudio { get; set; } = true;
    [JsonPropertyName("media_review_popup")] public bool MediaReviewPopup { get; set; } = true;
    [JsonPropertyName("media_overwrite_prompt")] public MediaOverwritePrompt MediaOverwritePrompt { get; set; } = MediaOverwritePrompt.Always;
    [JsonPropertyName("media_image_source")] public MediaImageSource MediaImageSource { get; set; } = MediaImageSource.MpvFrame;
    [JsonPropertyName("media_subtitle_burn")] public MediaSubtitleBurn MediaSubtitleBurn { get; set; } = MediaSubtitleBurn.None;
    [JsonPropertyName("media_image_max_edge")] public int MediaImageMaxEdge { get; set; } = 1600;
    [JsonPropertyName("media_image_quality")] public int MediaImageQuality { get; set; } = 95;
    [JsonPropertyName("media_anim_max_frames")] public int MediaAnimMaxFrames { get; set; } = 280;
    [JsonPropertyName("media_anim_target_fps")] public int MediaAnimTargetFps { get; set; } = 15;
    [JsonPropertyName("media_anim_min_fps")] public int MediaAnimMinFps { get; set; } = 5;
    [JsonPropertyName("media_anim_max_edge")] public int MediaAnimMaxEdge { get; set; } = 960;
    [JsonPropertyName("media_anim_quality")] public int MediaAnimQuality { get; set; } = 82;
    [JsonPropertyName("media_anim_max_bytes")] public int MediaAnimMaxBytes { get; set; } = 2_500_000;
    [JsonPropertyName("media_audio_bitrate_kbps")] public int MediaAudioBitrateKbps { get; set; } = 48;
    [JsonPropertyName("media_audio_stereo")] public bool MediaAudioStereo { get; set; }
    [JsonPropertyName("media_audio_max_bytes")] public int MediaAudioMaxBytes { get; set; } = 1_500_000;
    [JsonPropertyName("media_audio_auto_trim")] public bool MediaAudioAutoTrim { get; set; } = true;
    [JsonPropertyName("media_audio_pad_lead_ms")] public int MediaAudioPadLeadMs { get; set; } = 250;
    [JsonPropertyName("media_audio_pad_tail_ms")] public int MediaAudioPadTailMs { get; set; } = 350;
    [JsonPropertyName("media_audio_window_margin_s")] public double MediaAudioWindowMarginSeconds { get; set; } = 5.0;
    [JsonPropertyName("media_sentence_context_lines")] public int MediaSentenceContextLines { get; set; } = 2;

    // Popup.
    [JsonPropertyName("popup_trigger")] public PopupTriggerMode PopupTrigger { get; set; } = PopupTriggerMode.Hover;
    [JsonPropertyName("popup_hover_delay_ms")] public int PopupHoverDelayMs { get; set; } = 30;
    [JsonPropertyName("popup_switch_delay_ms")] public int PopupSwitchDelayMs { get; set; } = 250;
    [JsonPropertyName("popup_auto_hide")] public bool PopupAutoHide { get; set; } = true;
    [JsonPropertyName("popup_auto_hide_delay_ms")] public int PopupAutoHideDelayMs { get; set; } = 500;
    [JsonPropertyName("popup_hide_after_action")] public bool PopupHideAfterAction { get; set; }
    [JsonPropertyName("popup_position")] public PopupPositionMode PopupPosition { get; set; } = PopupPositionMode.AboveSubtitle;
    [JsonPropertyName("popup_fixed_anchor")] public PopupAnchor PopupFixedAnchor { get; set; } = PopupAnchor.TopCenter;
    [JsonPropertyName("popup_offset_px")] public int PopupOffsetPx { get; set; } = 60;
    [JsonPropertyName("popup_font_scale")] public double PopupFontScale { get; set; } = 0.85;
    [JsonPropertyName("popup_bg_opacity")] public int PopupBgOpacity { get; set; } = 78;
    [JsonPropertyName("popup_bg_color")] public string PopupBgColor { get; set; } = "#1A1A1A";
    [JsonPropertyName("popup_max_width_px")] public double PopupMaxWidthPx { get; set; } = 550;
    [JsonPropertyName("popup_max_meanings")] public int PopupMaxMeanings { get; set; } = 10;
    [JsonPropertyName("popup_furigana")] public bool PopupFurigana { get; set; } = true;
    [JsonPropertyName("popup_show_pitch")] public bool PopupShowPitch { get; set; } = true;
    [JsonPropertyName("popup_pitch_diagram")] public bool PopupPitchDiagram { get; set; } = true;
    [JsonPropertyName("popup_show_frequency")] public bool PopupShowFrequency { get; set; } = true;
    [JsonPropertyName("popup_show_conjugation")] public bool PopupShowConjugation { get; set; } = true;
    [JsonPropertyName("popup_show_state_actions")] public bool PopupShowStateActions { get; set; } = true;
    [JsonPropertyName("popup_show_never_forget")] public bool PopupShowNeverForget { get; set; } = true;
    [JsonPropertyName("popup_show_blacklist")] public bool PopupShowBlacklist { get; set; } = true;
    [JsonPropertyName("popup_show_suspend")] public bool PopupShowSuspend { get; set; }
    [JsonPropertyName("popup_show_forget")] public bool PopupShowForget { get; set; }
    [JsonPropertyName("popup_show_deck_membership")] public bool PopupShowDeckMembership { get; set; } = true;
    [JsonPropertyName("popup_disable_headword_link")] public bool PopupDisableHeadwordLink { get; set; }
    [JsonPropertyName("popup_move_actions_bottom")] public bool PopupMoveActionsBottom { get; set; }
    [JsonPropertyName("popup_show_review")] public bool PopupShowReview { get; set; } = true;
    [JsonPropertyName("popup_use_two_grades")] public bool PopupUseTwoGrades { get; set; }
    [JsonPropertyName("rotate_states_enabled")] public bool RotateStatesEnabled { get; set; }
    [JsonPropertyName("popup_show_rotate_actions")] public bool PopupShowRotateActions { get; set; }
    [JsonPropertyName("rotate_cycle")] public bool RotateCycle { get; set; }
    [JsonPropertyName("rotate_cycle_never_forget")] public bool RotateCycleNeverForget { get; set; } = true;
    [JsonPropertyName("rotate_cycle_blacklist")] public bool RotateCycleBlacklist { get; set; } = true;
    [JsonPropertyName("rotate_cycle_suspended")] public bool RotateCycleSuspended { get; set; }

    // Keybinds.
    [JsonPropertyName("popup_keybinds")] public Dictionary<string, string> PopupKeybinds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReviewAgain"] = "1", ["ReviewHard"] = "2", ["ReviewGood"] = "3", ["ReviewEasy"] = "4",
        ["NeverForget"] = "m", ["Blacklist"] = "b", ["Suspend"] = "s", ["Forget"] = "f",
        ["RotateForward"] = "", ["RotateBackward"] = "", ["Mine"] = "d"
    };
    [JsonPropertyName("keybind_prev_sub")] public string KeybindPrevSub { get; set; } = "Ctrl+LEFT";
    [JsonPropertyName("keybind_next_sub")] public string KeybindNextSub { get; set; } = "Ctrl+RIGHT";
    [JsonPropertyName("keybind_loop_sub")] public string KeybindLoopSub { get; set; } = "Ctrl+l";
    [JsonPropertyName("keybind_subtitle_earlier")] public string KeybindSubtitleEarlier { get; set; } = "Ctrl+Alt+LEFT";
    [JsonPropertyName("keybind_subtitle_later")] public string KeybindSubtitleLater { get; set; } = "Ctrl+Alt+RIGHT";
    [JsonPropertyName("subtitle_offset_step_ms")] public int SubtitleOffsetStepMs { get; set; } = 10;

    // Advanced.
    [JsonPropertyName("plugin_autostart")] public bool PluginAutostart { get; set; } = true;
    [JsonPropertyName("plugin_start_key")] public string PluginStartKey { get; set; } = "F10";
    [JsonPropertyName("cache_size")] public int CacheSize { get; set; } = 2000;
    [JsonPropertyName("preparse_enabled")] public bool PreparseEnabled { get; set; } = true;
    [JsonPropertyName("preparse_batch_size")] public int PreparseBatchSize { get; set; } = 60000;
    [JsonPropertyName("mouse_zone_percent")] public int MouseZonePercent { get; set; } = 65;
    [JsonPropertyName("subtitle_nav_buttons_enabled")] public bool SubtitleNavButtonsEnabled { get; set; } = true;
    [JsonPropertyName("status_overlay_enabled")] public bool StatusOverlayEnabled { get; set; } = true;
    [JsonPropertyName("debug_logging")] public bool DebugLogging { get; set; } = false;
    [JsonPropertyName("debug_show_hitboxes")] public bool DebugShowHitboxes { get; set; }
    [JsonPropertyName("auto_save_settings")] public bool AutoSaveSettings { get; set; }

    public static Dictionary<string, CustomStateStyle> CreateDefaultCustomTheme()
    {
        var d = new Dictionary<string, CustomStateStyle>(StringComparer.OrdinalIgnoreCase);
        for (var state = 0; state <= 7; state++)
        {
            var s = ThemePresets.For("Default", state);
            d[state.ToString()] = new CustomStateStyle
            {
                TextColor = s.Text, OutlineColor = s.Outline, OutlineSize = s.OutlineSize,
                TextOpacityPercent = (int)Math.Round(s.Opacity * 100), Bold = s.Bold, Italic = s.Italic,
                Underline = s.Underline, UnderlineColor = string.IsNullOrWhiteSpace(s.UnderlineColor) ? s.Text : s.UnderlineColor,
                UnderlineThickness = s.UnderlineThickness, Strikethrough = s.Strikethrough,
                HasShadow = !string.IsNullOrWhiteSpace(s.ShadowColor) && s.ShadowDepth > 0,
                ShadowColor = string.IsNullOrWhiteSpace(s.ShadowColor) ? "#000000" : s.ShadowColor,
                ShadowDepth = s.ShadowDepth
            };
        }
        return d;
    }

    public CustomStateStyle GetCustomState(int state)
    {
        if (!CustomThemeColors.TryGetValue(state.ToString(), out var style))
        {
            style = CreateDefaultCustomTheme()[state.ToString()];
            CustomThemeColors[state.ToString()] = style;
        }
        return style;
    }
}
