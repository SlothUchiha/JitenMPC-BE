# v0.5.1
- Fixed dictionary furigana rendering: Jiten readings such as `一[いち]番[ばん]` are now parsed into ruby text above the corresponding spelling instead of displaying the bracket notation literally.
- Added real graphical pitch-accent diagrams using the word reading and Jiten accent positions; numeric pitch values remain only as a fallback when a diagram cannot be produced or diagrams are disabled.
- Dictionary meaning chunks are now kept as separate entries and numbered `1.`, `2.`, `3.`, etc. instead of being flattened into an unnumbered list.

# v0.5.0
- Fixed JitenMPC-BE remaining alive invisibly after the main/settings window was closed while overlay windows were still open.
- The main/settings window now owns the desktop application lifetime: the dedicated **Close Settings** button may still hide it during playback, but closing it with the window X exits JitenMPC-BE completely.
- JitenMPC-BE now exits automatically when an established MPC-BE slave session disconnects or its player window disappears, including when the settings window is hidden.
- Added launched MPC-BE process-exit monitoring to cover abrupt exits and failed/aborted slave startups without leaving an orphaned JitenMPC-BE process.
- Made runtime and MPC controller disposal idempotent so overlapping shutdown paths cannot double-dispose overlay/controller resources.

# v0.4.0-preview.3

- Wired update checks to `SlothUchiha/JitenMPC-BE` and migrate older blank repository settings automatically.
- Fixed daily update-check bookkeeping so a successful up-to-date check is remembered for 24 hours.
- Added semantic prerelease comparison and release-list checking so preview builds can detect newer previews while stable builds ignore prereleases.
- Added normal per-user Windows installation through Inno Setup using a permanent AppId, Start Menu registration and Windows uninstall support.
- Added real in-app updating: JitenMPC-BE downloads the matching GitHub Release installer, starts it in silent upgrade mode, exits cleanly, and is relaunched by the installer.
- GitHub Actions now builds the Inno installer and automatically attaches it when a GitHub Release is published.
- Reworked the README around end-user requirements, installation and updating.

# v0.4.0-preview.2

- Added configurable **Subtitle earlier** and **Subtitle later** global hotkeys under Keybinds → Subtitle Navigation. Defaults are `Ctrl+Alt+Left` and `Ctrl+Alt+Right`.
- Added a configurable subtitle offset step directly beneath those hotkeys; default is 10 ms.
- Subtitle offset is accumulated in memory for the currently loaded subtitle track and resets when a new subtitle file/track is loaded or MPC-BE disconnects.
- Cue display, previous/next navigation, subtitle looping and mining-media timing all respect the active offset.
- The status line reports the current accumulated offset after each adjustment.

# v0.4.0-preview.1

- Removed the on-video Settings overlay button and its dedicated setting after the click-through experiment proved unreliable with MPC-BE. Previous/Next overlay controls remain and retain hover-pinning.
- Ported JitenMPV mining into the Avalonia/MPC-BE application: target word lists, direct/picker mining, skip-if-present, review-triggered mining, double-click mining, popup Mine action and mining keybind.
- Added Jiten+ status/quota handling and card-media upload support. Text mining continues if media capture is unavailable.
- Added ffmpeg-based MPC-BE media capture for static WebP screenshots, animated WebP clips and Opus audio.
- Added current-position/subtitle-midpoint capture, plain/knowledge-colored subtitle burn-in, screenshot/clip quality and size limits, audio bitrate/stereo/size controls, silence-aware trimming, padding and search margin.
- Added review-before-save with image preview, native WAV audio playback, editable capture/audio times and selectable nearby subtitle context.
- Added existing-media overwrite policies: Always, Once per session and Never.
- Kept ordinary example-sentence mining to the current subtitle line; nearby lines are offered by the media-review context workflow instead of being silently added.
- Media capture is the intentional MPC-BE-specific implementation difference: it reconstructs captures from the current source file with ffmpeg rather than using mpv frame APIs.

# v0.3.0-preview.11

- Replaced low-level-hook interception for the Settings overlay button with a tiny real Avalonia/Win32 input surface positioned exactly over the visible Settings hint.
- The subtitle overlay remains fully click-through; the Settings input surface intentionally is not `WS_EX_TRANSPARENT`, so Windows routes that click to JitenMPC-BE rather than MPC-BE.
- Previous/Next keep their already-working hook path.
- Preserved preview.10 behavior where hovering Settings/Previous/Next pins the control strip open.
- Added logging when the dedicated Settings input surface receives a click.

# v0.3.0-preview.10

- Strengthened Settings overlay click capture: MPC-BE is temporarily disabled for the captured down/up gesture, then Settings is activated and MPC-BE is immediately re-enabled.
- Settings / Previous / Next controls no longer time out while the cursor is stationary over any visible overlay control.


- Fixed the Settings overlay control leaking the same physical click through to MPC-BE.
- Settings clicks are now swallowed for the complete mouse-down/mouse-up gesture before the settings window is shown and activated.
- Previous/Next behavior is unchanged.

# JitenMPC-BE v0.3.0-preview.8

- Fixed fullscreen window tracking by resolving the largest visible top-level window owned by the connected MPC-BE process instead of assuming the slave-API HWND's original root remains the active player window.
- Overlay ownership is refreshed automatically when MPC-BE enters or exits fullscreen.
- Overlay and dictionary popup z-order now follows MPC-BE's current topmost state, preventing fullscreen MPC-BE from covering the popup.
- Popup position/z-order is refreshed during player geometry updates while it remains open.
- Added selective overlay-control click interception: Settings / Previous / Next consume their own left clicks while the rest of the subtitle overlay remains click-through to MPC-BE. The interceptor is active only while an MPC-BE window is foreground, so it cannot swallow clicks in unrelated applications.
- No feature or settings-layout changes.

# Changelog

## 0.3.0-preview.7

- Fixed a startup auto-save race introduced by the larger UI parity XAML: settings are now populated before the auto-save timer can write control defaults back into live configuration.
- Dictionary popup presentation now reasserts its native z-order after positioning, while preserving overlay ownership and non-activating behavior.
- Settings / Previous / Next overlay controls now latch after the lower interaction zone is entered; moving toward the buttons keeps them visible long enough to click.
- No settings layout or reader functionality was otherwise changed.

## 0.3.0-preview.6

- UI/layout-only JitenMPV parity pass; no functionality added or removed.
- Rebuilt the settings window around JitenMPV's current 750x580 / 180px-sidebar structure while retaining JitenMPC-BE's dark/purple palette.
- Replaced the first-pass card-heavy layouts with JitenMPV-style flat sections/dividers in General, Appearance, Popup and Advanced; retained cards for Features and Keybinds where upstream uses them.
- Reworked the dynamic Custom-theme editors to match upstream expander/swatch, color, size, opacity and text-effect geometry.
- Matched upstream field widths, slider/value readouts, conditional panels, alignment grid, footer spacing and API-key Show button behavior more closely.
- Preserved all MPC-BE-specific controls and JitenMPC-BE-only existing settings; Media remains intentionally MPC-BE-specific and mining remains absent.

## 0.3.0-preview.5

- Fixed dictionary popups disappearing behind the continuously-visible layered subtitle overlay introduced in preview.4.
- Dictionary popup is now an Avalonia-owned child of the subtitle overlay, giving the native window chain MPC-BE -> subtitle overlay -> dictionary popup.
- Removed the popup native-owner assignment that previously overwrote Avalonia ownership back to MPC-BE.
- Added a diagnostic log entry when a dictionary popup is shown.


## 0.3.0-preview.4

- Reworked the subtitle overlay as a persistent player-attached surface while the reader is active. Cue changes now clear/replace glyph content without hiding and reshowning the native overlay window, removing another path for stale subtitle frames to flash.
- Added small backwards-position jitter suppression during normal playback so stale MPC-BE position replies cannot momentarily reactivate the previous subtitle. Explicit seeks and larger backwards jumps remain unaffected.
- Fixed MPC-BE mouse blocking by applying `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` at Avalonia window creation and reapplying the same native styles after opening.
- Decoupled Settings / Previous / Next overlay-control visibility from subtitle cue visibility. They can now appear on mouse movement over MPC-BE even between subtitle lines.
- Overlay controls now follow the intended "on mouse movement" behavior and hide after about 1.8 seconds without movement.

## 0.3.0-preview.3

- Fixed a cue-transition flash where the previous subtitle could briefly reappear while the next cue was still being parsed.
- New cue changes now invalidate and clear the previous rendered subtitle immediately.
- The overlay remains hidden while the current cue render is pending and is shown only after that cue has committed.

### Build compatibility fix
- Parenthesized subtitle alignment modulo before the switch expression.
- Corrected two Avalonia `PointToClient` extension calls to use the window instance explicitly.


## 0.3.0-preview.1

- Added the non-mining JitenMPV settings/reader surface to the Avalonia application while retaining the JitenMPC-BE dark/purple color scheme.
- Reorganized settings into General, Appearance, Features, Media, Popup, Keybinds and Advanced tabs. Media remains MPC-BE-specific; mining controls are intentionally omitted.
- Added Custom per-state themes plus JitenReader-theme import.
- Added pitch-accent coloring/underline, i+1 highlighting, Top-N frequency marking and state-based blur/reveal.
- Added autopause enable/delay settings and review support.
- Added configurable popup trigger, timing, placement, appearance, content/actions, state rotation and deck-membership display.
- Added Jiten vocabulary-state and review API actions.
- Added review/state/subtitle-navigation keybinds and current-subtitle looping.
- Added reader autostart/toggle key, preparse/cache, status overlay, overlay navigation/settings controls and debug hitboxes.
- Added GitHub release-check/update scaffolding; repository is intentionally unconfigured until an official JitenMPC-BE repository exists.
- Mining, screenshots/audio/clips and mining keybinds remain intentionally excluded.

## 0.2.0-preview.5

- Made manual subtitle-track switching deterministic. Auto-load and manual extraction now share a generation guard, so stale ffmpeg jobs can no longer overwrite the most recently selected track.
- Added dynamic subtitle scaling based on the current MPC-BE video viewport. The configured font size/border/margins are the fullscreen master values and scale down with smaller windows.
- Made the dictionary popup native window transparent so its rounded Border no longer reveals an opaque black rectangle at the corners.

## 0.2.0-preview.4

- Updated Japanese font glyph coverage probing for Avalonia 12.0.3.
