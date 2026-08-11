using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using JitenMpcBe.Controls;
using JitenMpcBe.Models;
using JitenMpcBe.Native;
using JitenMpcBe.Views;

namespace JitenMpcBe.Services;

public sealed class AppRuntime : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly FileLogger _log;
    private readonly ToolDiscoveryService _tools;
    private readonly SubtitleTrackService _tracks;
    private readonly JitenApiClient _jiten;
    private readonly MpcBeController _mpc;
    private readonly KeybindService _keys = new();
    private readonly UpdateService _updates;
    private readonly MiningMediaService _miningMedia;
    private readonly DispatcherTimer _timer;
    private readonly SubtitleOverlayWindow _overlay = new();
    private readonly DictionaryPopupWindow _popup = new();
    private MouseClickInterceptor? _mouseClickInterceptor;
    private readonly List<SubtitleCue> _cues = [];
    private MainWindow? _main;
    private List<SubtitleStreamInfo> _embedded = [];
    private string _subtitlePath = "";
    private SubtitleCue? _currentCue;
    private string _currentCueKey = "";
    private int _renderGeneration;
    private bool _cueRenderPending;
    private OutlinedTokenControl? _hovered;
    private DateTime _hoverSince = DateTime.MinValue;
    private DateTime _lastHoverSeen = DateTime.MinValue;
    private DateTime _pauseHoverSince = DateTime.MinValue;
    private DateTime _blurHoverSince = DateTime.MinValue;
    private OutlinedTokenControl? _blurWord;
    private bool _hoverHold;
    private bool _pausedByHover;
    private DateTime _lastPoll = DateTime.MinValue;
    private DateTime _lastGeometry = DateTime.MinValue;
    private CancellationTokenSource? _mediaLoadCts;
    private CancellationTokenSource? _preparseCts;
    private int _subtitleLoadGeneration;
    private bool _readerActive;
    private bool _loopCurrentCue;
    private int _subtitleOffsetMs;
    private UpdateInfo? _pendingUpdate;
    private WinPoint _lastCursor;
    private bool _haveLastCursor;
    private DateTime _lastMouseMovement = DateTime.MinValue;
    private DateTime _interactionUiUntil = DateTime.MinValue;
    private List<StudyDeckInfo> _studyDecks = [];
    private JitenPlusInfo _jitenPlus = new(false, "Unknown", 0, 0, "Not checked.");
    private bool _mediaOverwriteApprovedThisSession;
    private DateTime _lastWordClickAt = DateTime.MinValue;
    private string _lastWordClickKey = "";
    private bool _miningBusy;
    private bool _hadMpcConnection;
    private bool _applicationExitRequested;
    private bool _disposed;

    public AppSettings Settings => _settingsService.Current;
    public MpcBeController Mpc => _mpc;
    public IReadOnlyList<SubtitleStreamInfo> SubtitleStreams { get; private set; } = [SubtitleStreamInfo.Auto];
    public string SubtitlePath => _subtitlePath;
    public string DataDirectory => _settingsService.DataDirectory;
    public string SettingsPath => _settingsService.SettingsPath;
    public UpdateInfo? PendingUpdate => _pendingUpdate;
    public IReadOnlyList<StudyDeckInfo> StudyDecks => _studyDecks;
    public JitenPlusInfo JitenPlus => _jitenPlus;

    public event Action<IReadOnlyList<SubtitleStreamInfo>>? SubtitleTracksChanged;
    public event Action<string>? StatusChanged;
    public event Action? ConnectionInfoChanged;
    public event Action? MediaInfoChanged;
    public event Action<UpdateInfo?>? UpdateInfoChanged;
    public event Action<IReadOnlyList<StudyDeckInfo>>? StudyDecksChanged;
    public event Action<JitenPlusInfo>? JitenPlusChanged;
    public event Action? ApplicationExitRequested;

    public AppRuntime()
    {
        _settingsService = new SettingsService();
        _log = new FileLogger(_settingsService.DataDirectory) { Enabled = Settings.DebugLogging };
        _tools = new ToolDiscoveryService(_settingsService.ApplicationDirectory, _log);
        _tracks = new SubtitleTrackService(_log);
        _jiten = new JitenApiClient(_log);
        _mpc = new MpcBeController(_log);
        _updates = new UpdateService(_log);
        _miningMedia = new MiningMediaService(_log);
        // Older previews persisted an empty repository value; migrate them automatically.
        if (string.IsNullOrWhiteSpace(Settings.UpdateRepository))
            Settings.UpdateRepository = UpdateService.DefaultRepository;
        _readerActive = Settings.PluginAutostart;

        var mpc = _tools.FindMpc(Settings.MpcPath);
        if (!string.IsNullOrWhiteSpace(mpc)) Settings.MpcPath = mpc;
        var ff = _tools.FindFfmpegPair(Settings.FfmpegPath, Settings.FfprobePath);
        if (!string.IsNullOrWhiteSpace(ff.ffmpeg)) Settings.FfmpegPath = ff.ffmpeg;
        if (!string.IsNullOrWhiteSpace(ff.ffprobe)) Settings.FfprobePath = ff.ffprobe;
        TrySave();

        _popup.CommandRequested += command => _ = HandlePopupCommandAsync(command);
        _mpc.Connected += OnConnected;
        _mpc.Disconnected += OnDisconnected;
        _mpc.LaunchedProcessExited += OnMpcProcessExited;
        _mpc.VersionChanged += _ => ConnectionInfoChanged?.Invoke();
        _mpc.MediaPathChanged += path => { MediaInfoChanged?.Invoke(); _ = LoadForMediaAsync(path); };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, (_, _) => Tick());
        _timer.Start();
    }

    public void AttachMainWindow(MainWindow main)
    {
        _main = main;
        _mouseClickInterceptor ??= new MouseClickInterceptor(
            point =>
            {
                if (!WindowUtil.IsPlayerForeground(_mpc.Hwnd)) return null;
                return _overlay.FindMouseActionAt(point);
            },
            ExecuteMouseAction,
            () => WindowUtil.GetPlayerHostWindow(_mpc.Hwnd),
            _log);
        Status("Ready.");
        if (Settings.UpdateCheckEnabled && (Settings.LastUpdateCheckUtc is null || DateTime.UtcNow - Settings.LastUpdateCheckUtc > TimeSpan.FromDays(1)))
            _ = CheckUpdatesAsync(false);
    }

    public async Task<IReadOnlyList<StudyDeckInfo>> LoadStudyDecksAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            _studyDecks = []; StudyDecksChanged?.Invoke(_studyDecks); return _studyDecks;
        }
        _studyDecks = await _jiten.GetStudyDecksAsync(Settings.ApiBaseUrl, Settings.ApiKey, Settings.ApiTimeoutSeconds);
        StudyDecksChanged?.Invoke(_studyDecks);
        return _studyDecks;
    }

    public async Task<JitenPlusInfo> RefreshJitenPlusStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
            _jitenPlus = new(false, "Free", 0, 0, "Configure a Jiten API key first.");
        else
            _jitenPlus = await _jiten.GetJitenPlusStatusAsync(Settings.ApiBaseUrl, Settings.ApiKey, Settings.ApiTimeoutSeconds);
        JitenPlusChanged?.Invoke(_jitenPlus);
        return _jitenPlus;
    }

    public void SaveSettings()
    {
        _log.Enabled = Settings.DebugLogging;
        _settingsService.Save();
        _jiten.ClearCache();
        _log.Write("Settings saved.");
        _ = RerenderCurrentCueAsync();
    }

    public void SaveSettingsQuietly()
    {
        _log.Enabled = Settings.DebugLogging;
        _settingsService.Save();
    }

    private void TrySave() { try { _settingsService.Save(); } catch { } }

    public void LaunchMpc()
    {
        var path = _tools.FindMpc(Settings.MpcPath);
        if (string.IsNullOrWhiteSpace(path)) throw new FileNotFoundException("MPC-BE was not found. Choose mpc-be64.exe first.");
        Settings.MpcPath = path; SaveSettingsQuietly();
        _mpc.Launch(path);
        Status("Launching MPC-BE and waiting for slave API connection...");
    }

    public (string ffmpeg, string ffprobe) RedetectFfmpeg()
    {
        var pair = _tools.FindFfmpegPair(Settings.FfmpegPath, Settings.FfprobePath);
        if (!string.IsNullOrWhiteSpace(pair.ffmpeg)) Settings.FfmpegPath = pair.ffmpeg;
        if (!string.IsNullOrWhiteSpace(pair.ffprobe)) Settings.FfprobePath = pair.ffprobe;
        SaveSettingsQuietly();
        Status(!string.IsNullOrWhiteSpace(pair.ffmpeg) && !string.IsNullOrWhiteSpace(pair.ffprobe)
            ? "Ready. ffmpeg and ffprobe detected automatically." : "Could not find both ffmpeg and ffprobe.");
        return pair;
    }

    public async Task<bool> TestApiAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey)) { Status("Enter a Jiten API key first."); return false; }
        try
        {
            var ok = await _jiten.PingAsync(Settings.ApiBaseUrl, Settings.ApiKey, Settings.ApiTimeoutSeconds);
            Status(ok ? "Jiten API connection succeeded." : "Jiten API test failed.");
            return ok;
        }
        catch (Exception ex) { Status("Jiten API test failed: " + ex.Message); return false; }
    }

    public async Task<UpdateInfo?> CheckUpdatesAsync(bool userInitiated = true)
    {
        if (userInitiated) Status("Checking for updates...");
        var assembly = Assembly.GetExecutingAssembly();
        var current = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var result = await _updates.CheckAsync(Settings, current);
        SaveSettingsQuietly();

        if (!result.Succeeded)
        {
            if (userInitiated) Status("Update check failed: " + result.Error);
            return null;
        }

        _pendingUpdate = result.Update;
        UpdateInfoChanged?.Invoke(_pendingUpdate);
        if (userInitiated) Status(_pendingUpdate is null ? "JitenMPC-BE is up to date." : $"Update available: {_pendingUpdate.Name}");
        return _pendingUpdate;
    }

    public async Task<bool> InstallPendingUpdateAsync()
    {
        if (_pendingUpdate is null) return false;

        try
        {
            if (!_pendingUpdate.CanInstall)
            {
                _updates.OpenRelease(_pendingUpdate);
                Status("This release has no installer asset yet; opened the GitHub release page instead.");
                return false;
            }

            Status($"Downloading {_pendingUpdate.Name}...");
            var installerPath = await _updates.DownloadInstallerAsync(_pendingUpdate);
            SaveSettingsQuietly();
            Status("Starting update installer...");
            _updates.LaunchInstaller(installerPath);
            return true;
        }
        catch (Exception ex)
        {
            _log.Write("Update install failed: " + ex.Message);
            Status("Update install failed: " + ex.Message);
            return false;
        }
    }

    public bool ImportJitenReaderTheme(string code, out string status)
    {
        var ok = ThemeImportService.TryImport(code, Settings, out status);
        if (ok) { SaveSettings(); Status(status); }
        return ok;
    }

    public void OpenConfigFolder()
    {
        Directory.CreateDirectory(DataDirectory);
        Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
    }

    public async Task LoadExternalSubtitleAsync(string path)
    {
        ++_subtitleLoadGeneration;
        if (LoadSubtitleFile(path)) await RerenderCurrentCueAsync();
    }

    public async Task SelectSubtitleTrackAsync(int index)
    {
        if (string.IsNullOrWhiteSpace(_mpc.MediaPath) || !File.Exists(_mpc.MediaPath)) return;
        var generation = ++_subtitleLoadGeneration;
        _mediaLoadCts?.Cancel();
        _log.Write($"Subtitle track request generation={generation}; index={index}");
        if (index < 0) { await LoadForMediaAsync(_mpc.MediaPath, generation); return; }
        var stream = _embedded.FirstOrDefault(s => s.Index == index);
        if (stream is null || !stream.IsText) return;
        await ExtractAndLoadAsync(_mpc.MediaPath, stream, generation);
    }

    private Task LoadForMediaAsync(string media) => LoadForMediaAsync(media, ++_subtitleLoadGeneration);

    private async Task LoadForMediaAsync(string media, int generation)
    {
        _mediaLoadCts?.Cancel();
        _mediaLoadCts = new CancellationTokenSource();
        var token = _mediaLoadCts.Token;
        try
        {
            if (!Settings.AutoLoadSubtitles) return;
            if (!File.Exists(media)) { Status("MPC-BE reported a media path that JitenMPC-BE cannot access."); return; }
            List<SubtitleStreamInfo> streams = [];
            if (File.Exists(Settings.FfprobePath))
            {
                streams = await _tracks.ProbeAsync(media, Settings.FfprobePath);
                if (token.IsCancellationRequested || generation != _subtitleLoadGeneration) return;
            }
            _embedded = streams;
            SubtitleStreams = [SubtitleStreamInfo.Auto, .. streams];
            SubtitleTracksChanged?.Invoke(SubtitleStreams);

            var external = _tracks.FindExternal(media);
            if (!string.IsNullOrWhiteSpace(external))
            {
                if (generation == _subtitleLoadGeneration) LoadSubtitleFile(external);
                return;
            }
            if (!File.Exists(Settings.FfprobePath) || !File.Exists(Settings.FfmpegPath)) { Status("Embedded subtitle extraction needs both ffmpeg and ffprobe."); return; }
            Status("No external subtitle found; checking embedded tracks...");
            var chosen = _tracks.ChooseAuto(streams);
            if (chosen is null) { Status("No supported text subtitle tracks were found."); return; }
            await ExtractAndLoadAsync(media, chosen, generation);
        }
        catch (Exception ex) { _log.Write("Auto subtitle load failed: " + ex); Status("Subtitle load failed: " + ex.Message); }
    }

    private async Task ExtractAndLoadAsync(string media, SubtitleStreamInfo stream, int generation)
    {
        Status("Extracting " + stream.Display + "...");
        var path = await _tracks.ExtractAsync(media, stream, Settings.FfmpegPath);
        if (generation != _subtitleLoadGeneration) { _log.Write($"Discarding stale subtitle extraction for {stream.Display}; a newer track request exists."); return; }
        if (string.IsNullOrWhiteSpace(path)) { Status("ffmpeg could not extract the selected subtitle stream."); return; }
        if (LoadSubtitleFile(path))
        {
            _log.Write($"Committed subtitle generation={generation}: {stream.Display}");
            Status($"Loaded {stream.Display} | {_cues.Count} cues");
        }
    }

    private bool LoadSubtitleFile(string path)
    {
        try
        {
            var cues = SubtitleParser.LoadFile(path);
            if (cues.Count == 0) throw new InvalidOperationException("No dialogue cues were found.");
            _cues.Clear(); _cues.AddRange(cues); _subtitlePath = path; _currentCueKey = ""; _currentCue = null; _cueRenderPending = false; _subtitleOffsetMs = 0;
            _jiten.ClearCache();
            _log.Write($"Loaded subtitle {path} ({_cues.Count} cues)");
            Status($"Loaded {_cues.Count} cues: {Path.GetFileName(path)}");
            MediaInfoChanged?.Invoke();
            StartPreparse();
            return true;
        }
        catch (Exception ex) { _log.Write("Subtitle load failed: " + ex); Status("Subtitle load failed: " + ex.Message); return false; }
    }

    private void StartPreparse()
    {
        _preparseCts?.Cancel();
        if (!Settings.PreparseEnabled || string.IsNullOrWhiteSpace(Settings.ApiKey) || _cues.Count == 0) return;
        _preparseCts = new CancellationTokenSource();
        var snapshot = _cues.Select(c => c.Text).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var token = _preparseCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var text in snapshot)
                {
                    if (token.IsCancellationRequested) return;
                    await _jiten.ParseAsync(Settings.ApiBaseUrl, Settings.ApiKey, text, Settings.ApiTimeoutSeconds, Settings.CacheSize);
                    await Task.Delay(8, token);
                }
                _log.Write($"Preparsed {snapshot.Count} unique subtitle cues.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.Write("Preparse failed: " + ex.Message); }
        }, token);
    }

    private void OnConnected()
    {
        _hadMpcConnection = true;
        _overlay.SetMpcOwner(_mpc.Hwnd); _popup.SetMpcOwner(_mpc.Hwnd);
        ConnectionInfoChanged?.Invoke(); Status("MPC-BE connected.");
    }

    private void OnDisconnected()
    {
        _preparseCts?.Cancel();
        _currentCue = null; _currentCueKey = ""; _cueRenderPending = false; _cues.Clear(); _subtitlePath = ""; _embedded = []; _subtitleOffsetMs = 0;
        SubtitleStreams = [SubtitleStreamInfo.Auto]; SubtitleTracksChanged?.Invoke(SubtitleStreams);
        HidePlayerOverlays(); ConnectionInfoChanged?.Invoke(); MediaInfoChanged?.Invoke(); Status("MPC-BE disconnected.");
        if (_hadMpcConnection) RequestApplicationExit("MPC-BE disconnected; closing JitenMPC-BE.");
    }

    private void OnMpcProcessExited()
    {
        // Process.Exited is raised off the UI thread. Give the slave connection a brief chance
        // to finish establishing in case MPC-BE delegated startup internally, then only shut
        // down if no live slave window exists.
        _ = Task.Run(async () =>
        {
            await Task.Delay(400);
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && !_mpc.IsConnected)
                    RequestApplicationExit("The MPC-BE slave process exited; closing JitenMPC-BE.");
            });
        });
    }

    private void RequestApplicationExit(string reason)
    {
        if (_disposed || _applicationExitRequested) return;
        _applicationExitRequested = true;
        _log.Write(reason);
        // Defer the lifetime shutdown until the current MPC message/timer callback has returned;
        // this avoids disposing the hidden slave-message window from inside its own handler.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) ApplicationExitRequested?.Invoke();
        }, DispatcherPriority.Background);
    }

    private void Tick()
    {
        // MPC-BE normally sends CMD_DISCONNECT, but also catch abrupt/window-level exits so a
        // hidden settings window can never leave an orphaned JitenMPC-BE process behind.
        if (_hadMpcConnection && !_mpc.IsConnected)
        {
            RequestApplicationExit("The MPC-BE slave window disappeared; closing JitenMPC-BE.");
            return;
        }

        if (_keys.Pressed(Settings.PluginStartKey))
        {
            _readerActive = !_readerActive;
            Status(_readerActive ? "Jiten reader enabled." : "Jiten reader disabled.");
            if (!_readerActive) { _cueRenderPending = false; HidePlayerOverlays(); } else { _currentCueKey = ""; _cueRenderPending = false; }
        }
        HandleNavigationKeybinds();
        if (!_mpc.IsConnected) return;

        var now = DateTime.UtcNow;
        if ((now - _lastPoll).TotalMilliseconds >= 100) { _lastPoll = now; _mpc.PollPosition(); }
        if (_loopCurrentCue && _currentCue is not null && _mpc.PositionSeconds >= EffectiveCueEnd(_currentCue) - .04) _mpc.Seek(Math.Max(0, EffectiveCueStart(_currentCue)));

        if (!WindowUtil.IsPlayerWindowVisible(_mpc.Hwnd)) { HidePlayerOverlays(); return; }
        if ((now - _lastGeometry).TotalMilliseconds >= 100)
        {
            _lastGeometry = now;
            _overlay.SetGeometry(_mpc.Hwnd, Settings);
            if (_popup.IsVisible && _popup.CurrentToken is not null)
            {
                _popup.PositionFor(_popup.CurrentToken, Settings, _mpc.Hwnd);
                _popup.EnsureAboveOwner(_mpc.Hwnd);
            }
        }
        if (_readerActive && !_overlay.IsVisible) _overlay.Show();
        var inZone = MouseInInteractionZone(now);
        _overlay.UpdateTransientUi(Settings, inZone);
        var leftPressed = _keys.MouseLeftPressed();
        if (!_readerActive) return;
        var cue = SubtitleParser.CueAt(_cues, SubtitleClockSeconds);
        var key = cue is null ? "" : $"{cue.Start:F3}-{cue.End:F3}|{cue.Text}";
        if (key != _currentCueKey)
        {
            _currentCueKey = key;
            _currentCue = cue;
            _cueRenderPending = cue is not null;
            _overlay.ClearSubtitle();
            _ = RenderCueAsync(cue);
        }

        HandlePopupKeybinds();
        HandleHover(now, leftPressed);
    }

    private void HandleNavigationKeybinds()
    {
        if (_keys.Pressed(Settings.KeybindPrevSub)) SeekPreviousCue();
        if (_keys.Pressed(Settings.KeybindNextSub)) SeekNextCue();
        if (_keys.Pressed(Settings.KeybindLoopSub))
        {
            _loopCurrentCue = !_loopCurrentCue;
            Status(_loopCurrentCue ? "Subtitle loop enabled." : "Subtitle loop disabled.");
        }
        if (_keys.Pressed(Settings.KeybindSubtitleEarlier)) AdjustSubtitleOffset(-Math.Max(1, Settings.SubtitleOffsetStepMs));
        if (_keys.Pressed(Settings.KeybindSubtitleLater)) AdjustSubtitleOffset(Math.Max(1, Settings.SubtitleOffsetStepMs));
    }

    private double SubtitleClockSeconds => _mpc.PositionSeconds - _subtitleOffsetMs / 1000.0;
    private double EffectiveCueStart(SubtitleCue cue) => cue.Start + _subtitleOffsetMs / 1000.0;
    private double EffectiveCueEnd(SubtitleCue cue) => cue.End + _subtitleOffsetMs / 1000.0;
    private SubtitleCue WithSubtitleOffset(SubtitleCue cue) => new(EffectiveCueStart(cue), EffectiveCueEnd(cue), cue.Text);

    private void AdjustSubtitleOffset(int deltaMs)
    {
        if (!_mpc.IsConnected || _cues.Count == 0) return;
        _subtitleOffsetMs = Math.Clamp(_subtitleOffsetMs + deltaMs, -3_600_000, 3_600_000);
        _currentCueKey = "";
        var direction = _subtitleOffsetMs switch { < 0 => "earlier", > 0 => "later", _ => "aligned" };
        Status($"Subtitle offset: {_subtitleOffsetMs:+0;-0;0} ms ({direction}).");
    }

    private void HandlePopupKeybinds()
    {
        if (!_popup.IsVisible || _popup.CurrentToken is null) return;
        foreach (var pair in Settings.PopupKeybinds)
        {
            if (!_keys.Pressed(pair.Value)) continue;
            var rating = pair.Key switch { "ReviewAgain" => 1, "ReviewHard" => 2, "ReviewGood" => 3, "ReviewEasy" => 4, _ => 0 };
            _ = HandlePopupCommandAsync(new PopupCommand(pair.Key, _popup.CurrentToken, rating));
            break;
        }
    }

    private bool MouseInInteractionZone(DateTime now)
    {
        if (!WindowUtil.TryGetCursor(out var p)) return false;
        var moved = !_haveLastCursor || p.X != _lastCursor.X || p.Y != _lastCursor.Y;
        if (moved)
        {
            _lastCursor = p;
            _haveLastCursor = true;
            _lastMouseMovement = now;
        }

        var rect = WindowUtil.GetBestVideoRect(_mpc.Hwnd);
        if (!rect.IsValid || p.X < rect.Left || p.X > rect.Right || p.Y < rect.Top || p.Y > rect.Bottom)
        {
            _interactionUiUntil = DateTime.MinValue;
            return false;
        }

        var zone = Math.Clamp(Settings.MouseZonePercent, 30, 100) / 100.0;
        var inTriggerZone = p.Y >= rect.Bottom - rect.Height * zone;

        // Once one of our controls is visible, hovering it pins the control strip open even
        // when the cursor is stationary. This makes the timeout behave like a paused timer
        // while the user is actually aiming at Previous / Next.
        var overVisibleControl = now < _interactionUiUntil && _overlay.FindMouseActionAt(p) is not null;
        if (overVisibleControl)
        {
            _interactionUiUntil = now.AddMilliseconds(1800);
            return true;
        }

        // Entering the configured lower interaction zone arms the controls. Once armed,
        // continued mouse movement anywhere over the player extends their lifetime so the
        // cursor can actually travel from the trigger zone to Previous / Next.
        if (moved && (inTriggerZone || now < _interactionUiUntil))
            _interactionUiUntil = now.AddMilliseconds(1800);

        return now < _interactionUiUntil;
    }

    private void ExecuteMouseAction(string action)
    {
        switch (action)
        {
            case "Previous": SeekPreviousCue(); break;
            case "Next": SeekNextCue(); break;
        }
    }

    public void SeekPreviousCue()
    {
        if (!_mpc.IsConnected || _cues.Count == 0) return;
        var pos = SubtitleClockSeconds;
        var currentStart = _currentCue?.Start ?? pos;
        var target = _cues.LastOrDefault(c => c.Start < currentStart - .08) ?? _cues.First();
        _mpc.Seek(Math.Max(0, EffectiveCueStart(target) + .01)); Status("Previous subtitle.");
    }

    public void SeekNextCue()
    {
        if (!_mpc.IsConnected || _cues.Count == 0) return;
        var currentStart = _currentCue?.Start ?? SubtitleClockSeconds;
        var target = _cues.FirstOrDefault(c => c.Start > currentStart + .08) ?? _cues.Last();
        _mpc.Seek(Math.Max(0, EffectiveCueStart(target) + .01)); Status("Next subtitle.");
    }

    private async Task RenderCueAsync(SubtitleCue? cue)
    {
        var generation = ++_renderGeneration;
        if (_blurWord is not null) _overlay.SetBlurReveal(_blurWord, false);
        _blurWord = null; _hovered = null; HidePopup(); SetHoverPause(false, DateTime.UtcNow);
        if (cue is null)
        {
            _cueRenderPending = false;
            _overlay.ClearSubtitle();
            return;
        }
        ParsedSubtitle? parsed = null;
        if (!string.IsNullOrWhiteSpace(Settings.ApiKey)) parsed = await _jiten.ParseAsync(Settings.ApiBaseUrl, Settings.ApiKey, cue.Text, Settings.ApiTimeoutSeconds, Settings.CacheSize);
        if (generation != _renderGeneration || cue != _currentCue) return;
        var segments = JitenApiClient.BuildSegments(cue.Text, parsed);
        _overlay.RenderSegments(segments, Settings); _overlay.SetGeometry(_mpc.Hwnd, Settings);
        _cueRenderPending = false;
        _log.Write($"Rendered cue {cue.Start:F3}-{cue.End:F3}; interactive word controls={_overlay.WordControls.Count}; API key configured={!string.IsNullOrWhiteSpace(Settings.ApiKey)}");
        if (_readerActive && WindowUtil.IsPlayerWindowVisible(_mpc.Hwnd) && !_overlay.IsVisible) _overlay.Show();
    }

    public Task RerenderCurrentCueAsync() => _currentCue is null ? Task.CompletedTask : RenderCueAsync(_currentCue);

    private void HandleHover(DateTime now, bool leftPressed)
    {
        var hovered = _overlay.FindHoveredWord();
        var popupHovered = _popup.IsCursorInside();

        HandleBlurReveal(hovered, now);
        SetHoverPause(hovered is not null || popupHovered, now);

        if (hovered is not null)
        {
            _lastHoverSeen = now;
            if (!ReferenceEquals(_hovered, hovered))
            {
                _hovered = hovered; _hoverSince = now;
                _log.Write($"Hover entered word [{hovered.Surface}]");
            }

            if (leftPressed && !popupHovered && Settings.MiningEnabled && Settings.DoubleClickAction == DoubleClickAction.Mine && hovered.Word is not null)
            {
                var clickKey = $"{hovered.Word.WordId}:{hovered.Word.ReadingIndex}";
                if (_lastWordClickKey == clickKey && (now - _lastWordClickAt).TotalMilliseconds <= 500)
                {
                    _lastWordClickAt = DateTime.MinValue; _lastWordClickKey = "";
                    _ = MineTokenAsync(hovered);
                    return;
                }
                _lastWordClickKey = clickKey; _lastWordClickAt = now;
            }

            if (Settings.PopupTrigger == PopupTriggerMode.Click)
            {
                if (leftPressed) ShowPopup(hovered);
                return;
            }
            var delay = _popup.IsVisible && !ReferenceEquals(_popup.CurrentToken, hovered) ? Settings.PopupSwitchDelayMs : Settings.PopupHoverDelayMs;
            if ((now - _hoverSince).TotalMilliseconds >= Math.Max(0, delay)) ShowPopup(hovered);
            return;
        }

        _hovered = null;
        if (popupHovered) { _lastHoverSeen = now; return; }
        if (!Settings.PopupAutoHide) return;
        if (_lastHoverSeen != DateTime.MinValue && (now - _lastHoverSeen).TotalMilliseconds < Math.Max(0, Settings.PopupAutoHideDelayMs)) return;
        HidePopup();
    }

    private void HandleBlurReveal(OutlinedTokenControl? hovered, DateTime now)
    {
        if (!Settings.BlurEnabled || !Settings.BlurRevealOnHover)
        {
            if (_blurWord is not null) _overlay.SetBlurReveal(_blurWord, false);
            _blurWord = null; return;
        }
        if (!ReferenceEquals(_blurWord, hovered))
        {
            if (_blurWord is not null) _overlay.SetBlurReveal(_blurWord, false);
            _blurWord = hovered; _blurHoverSince = now;
        }
        if (_blurWord is not null && (now - _blurHoverSince).TotalMilliseconds >= Math.Max(0, Settings.BlurRevealDelayMs)) _overlay.SetBlurReveal(_blurWord, true);
    }

    private void ShowPopup(OutlinedTokenControl token)
    {
        if (token.Word is null || !WindowUtil.IsPlayerWindowVisible(_mpc.Hwnd)) return;
        if (!ReferenceEquals(_popup.CurrentToken, token))
        {
            _popup.Populate(token, Settings);
            if (Settings.PopupShowDeckMembership || (Settings.MiningEnabled && Settings.MiningStudyDeckId is not null)) _ = LoadDeckMembershipAsync(token);
        }
        if (!_popup.IsVisible)
        {
            _popup.Show(_overlay);
            _log.Write($"Dictionary popup shown for [{token.Surface}] as child of subtitle overlay; opacity={Settings.PopupBgOpacity}; position={Settings.PopupPosition}.");
        }
        _popup.PositionFor(token, Settings, _mpc.Hwnd);
        _popup.EnsureAboveOwner(_mpc.Hwnd);
    }

    private async Task LoadDeckMembershipAsync(OutlinedTokenControl token)
    {
        if (token.Word is null || string.IsNullOrWhiteSpace(Settings.ApiKey)) return;
        var namesTask = Settings.PopupShowDeckMembership
            ? _jiten.LookupDeckMembershipAsync(Settings.ApiBaseUrl, Settings.ApiKey, token.Word.WordId, token.Word.ReadingIndex, Settings.ApiTimeoutSeconds)
            : Task.FromResult(new List<string>());
        var idsTask = Settings.MiningEnabled && Settings.MiningStudyDeckId is not null
            ? _jiten.LookupDeckIdsAsync(Settings.ApiBaseUrl, Settings.ApiKey, token.Word.WordId, token.Word.ReadingIndex, Settings.ApiTimeoutSeconds)
            : Task.FromResult(new List<int>());
        await Task.WhenAll(namesTask, idsTask);
        if (ReferenceEquals(_popup.CurrentToken, token))
        {
            _popup.SetDeckMembership(namesTask.Result);
            _popup.SetMineState(Settings.MiningStudyDeckId is int target && idsTask.Result.Contains(target));
        }
    }

    private async Task HandlePopupCommandAsync(PopupCommand command)
    {
        var word = command.Token.Word;
        if (word is null || string.IsNullOrWhiteSpace(Settings.ApiKey)) return;

        if (command.Name == "Mine")
        {
            if (!Settings.MiningEnabled) return;
            if (Settings.MiningToStudyDeck && Settings.MiningStudyDeckId is int directDeck)
                await MineTokenAsync(command.Token, directDeck);
            else
            {
                var decks = _studyDecks.Count > 0 ? _studyDecks : (await LoadStudyDecksAsync()).ToList();
                _popup.ShowDeckPicker(decks);
                Dispatcher.UIThread.Post(() => { if (_popup.IsVisible) _popup.PositionFor(command.Token, Settings, _mpc.Hwnd); }, DispatcherPriority.Background);
            }
            return;
        }
        if (command.Name.StartsWith("MineDeck:", StringComparison.Ordinal) && int.TryParse(command.Name[9..], out var pickedDeck))
        {
            _popup.HideDeckPicker();
            await MineTokenAsync(command.Token, pickedDeck);
            return;
        }

        var ok = false;
        var wasReview = false;
        switch (command.Name)
        {
            case "OpenHeadword":
                if (!Settings.PopupDisableHeadwordLink)
                {
                    var query = Uri.EscapeDataString(word.Spelling ?? command.Token.Surface);
                    Process.Start(new ProcessStartInfo("https://jiten.moe/parse?text=" + query) { UseShellExecute = true });
                }
                return;
            case "NeverForget": ok = await _jiten.SetVocabularyStateAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, "neverForget-add", Settings.ApiTimeoutSeconds); break;
            case "Blacklist": ok = await _jiten.SetVocabularyStateAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, "blacklist-add", Settings.ApiTimeoutSeconds); break;
            case "Suspend": ok = await _jiten.SetVocabularyStateAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, "suspend-add", Settings.ApiTimeoutSeconds); break;
            case "Forget": ok = await _jiten.SetVocabularyStateAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, "forget-add", Settings.ApiTimeoutSeconds); break;
            case "RotateForward": ok = await RotateStateAsync(word, true); break;
            case "RotateBackward": ok = await RotateStateAsync(word, false); break;
            case "ReviewAgain" or "ReviewHard" or "ReviewGood" or "ReviewEasy":
                wasReview = true;
                if (Settings.ReviewsEnabled) ok = await _jiten.ReviewAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, command.Rating, Settings.ApiTimeoutSeconds);
                break;
        }
        Status(ok ? $"{command.Name} applied to {word.Spelling ?? command.Token.Surface}." : $"{command.Name} failed; see log.");
        if (ok)
        {
            if (wasReview && Settings.MiningEnabled && Settings.MiningAutoOnReview && Settings.MiningStudyDeckId is int reviewDeck)
                await MineTokenAsync(command.Token, reviewDeck, true);
            _currentCueKey = "";
            if (Settings.PopupHideAfterAction) HidePopup();
            else await RerenderCurrentCueAsync();
        }
    }

    private async Task MineTokenAsync(OutlinedTokenControl token, int? deckOverride = null, bool fromReview = false)
    {
        if (_miningBusy || !Settings.MiningEnabled || token.Word is null || string.IsNullOrWhiteSpace(Settings.ApiKey)) return;
        var word = token.Word;
        var deckId = deckOverride ?? (Settings.MiningToStudyDeck || fromReview ? Settings.MiningStudyDeckId : null);
        if (deckId is null)
        {
            var decks = _studyDecks.Count > 0 ? _studyDecks : (await LoadStudyDecksAsync()).ToList();
            if (_popup.IsVisible && ReferenceEquals(_popup.CurrentToken, token))
            {
                _popup.ShowDeckPicker(decks);
                Dispatcher.UIThread.Post(() => { if (_popup.IsVisible) _popup.PositionFor(token, Settings, _mpc.Hwnd); }, DispatcherPriority.Background);
            }
            else Status("Choose a target word list in Mining settings first.");
            return;
        }

        _miningBusy = true;
        try
        {
            if (Settings.MiningSkipIfPresent)
            {
                var memberships = await _jiten.LookupDeckIdsAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, Settings.ApiTimeoutSeconds);
                if (memberships.Contains(deckId.Value))
                {
                    _popup.SetMineState(true); Status($"{word.Spelling ?? token.Surface} is already in the target list; skipped."); return;
                }
            }

            var contextCues = GetMiningContextCues();
            var sentence = Settings.MiningCaptureSentence ? (_currentCue?.Text ?? token.Surface) : null;
            var source = Settings.MiningCaptureSentence && !string.IsNullOrWhiteSpace(_mpc.MediaPath) ? Path.GetFileNameWithoutExtension(_mpc.MediaPath) : null;
            MiningMediaBundle? media = null;
            var skipMedia = !Settings.MediaCaptureEnabled;

            if (!skipMedia)
            {
                var plus = await RefreshJitenPlusStatusAsync();
                if (!plus.IsPlus)
                {
                    skipMedia = true;
                    _log.Write("Mining media skipped because Jiten+ is unavailable; text mining will continue.");
                }
                else if (string.IsNullOrWhiteSpace(Settings.FfmpegPath) || !File.Exists(Settings.FfmpegPath) || string.IsNullOrWhiteSpace(_mpc.MediaPath) || !File.Exists(_mpc.MediaPath))
                {
                    skipMedia = true;
                    Status("Mining word without media: ffmpeg/current media file is unavailable.");
                }
                else
                {
                    var existing = await _jiten.GetCardMediaAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, Settings.ApiTimeoutSeconds);
                    var conflicts = (Settings.MediaCaptureImage && existing.HasImage) || (Settings.MediaCaptureAudio && existing.HasAudio);
                    if (conflicts && Settings.MediaOverwritePrompt != MediaOverwritePrompt.Never &&
                        !(Settings.MediaOverwritePrompt == MediaOverwritePrompt.OncePerSession && _mediaOverwriteApprovedThisSession))
                    {
                        var dialog = new MediaOverwriteDialog(existing);
                        var decision = await dialog.ShowDialog<MediaOverwriteDecision>(DialogOwner());
                        if (decision == MediaOverwriteDecision.Cancel) return;
                        if (decision == MediaOverwriteDecision.SkipMedia) skipMedia = true;
                        if (decision == MediaOverwriteDecision.Replace && Settings.MediaOverwritePrompt == MediaOverwritePrompt.OncePerSession)
                            _mediaOverwriteApprovedThisSession = true;
                    }

                    if (!skipMedia)
                    {
                        var captureCue = _currentCue is null ? new SubtitleCue(_mpc.PositionSeconds, _mpc.PositionSeconds + 1, token.Surface) : WithSubtitleOffset(_currentCue);
                        media = await _miningMedia.CaptureAsync(Settings.FfmpegPath, _mpc.MediaPath, captureCue,
                            _mpc.PositionSeconds, _currentCue?.Text ?? token.Surface, _overlay.LastSegments, Settings);

                        if (Settings.MediaReviewPopup && _currentCue is not null)
                        {
                            var effectiveCurrentCue = WithSubtitleOffset(_currentCue);
                            var review = new MiningReviewWindow(word.Spelling ?? token.Surface, media, effectiveCurrentCue, contextCues);
                            var result = await review.ShowDialog<MiningReviewResult>(DialogOwner());
                            if (result is null || !result.Accepted) return;
                            if (Settings.MiningCaptureSentence) sentence = result.Sentence;
                            media = await _miningMedia.CaptureAsync(Settings.FfmpegPath, _mpc.MediaPath, effectiveCurrentCue, _mpc.PositionSeconds, _currentCue.Text, _overlay.LastSegments, Settings, result.ImageTime, result.AudioStart, result.AudioEnd);
                        }
                    }
                }
            }

            var added = await _jiten.AddToStudyDeckAsync(Settings.ApiBaseUrl, Settings.ApiKey, deckId.Value, word.WordId, word.ReadingIndex, sentence, source, Settings.ApiTimeoutSeconds);
            if (!added) { Status("Mining failed while adding the word; see log."); return; }

            var mediaErrors = new List<string>();
            if (media?.Image is not null)
            {
                var r = await _jiten.UploadCardMediaAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, media.Image, Settings.ApiTimeoutSeconds);
                if (!r.Success) mediaErrors.Add(r.Error);
            }
            if (media?.Audio is not null)
            {
                var r = await _jiten.UploadCardMediaAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, media.Audio, Settings.ApiTimeoutSeconds);
                if (!r.Success) mediaErrors.Add(r.Error);
            }
            if (media is not null) await RefreshJitenPlusStatusAsync();

            _jiten.ClearCache();
            _popup.SetMineState(true);
            if (Settings.PopupShowDeckMembership) await LoadDeckMembershipAsync(token);
            Status(mediaErrors.Count == 0
                ? $"Mined {word.Spelling ?? token.Surface}."
                : $"Mined {word.Spelling ?? token.Surface}, but media upload failed: {string.Join("; ", mediaErrors)}");
            if (Settings.PopupHideAfterAction) HidePopup();
        }
        catch (Exception ex)
        {
            _log.Write("Mining failed: " + ex);
            Status("Mining failed: " + ex.Message);
        }
        finally { _miningBusy = false; }
    }

    private List<SubtitleCue> GetMiningContextCues()
    {
        if (_currentCue is null) return [];
        var index = _cues.IndexOf(_currentCue);
        if (index < 0) return [WithSubtitleOffset(_currentCue)];
        var n = Math.Clamp(Settings.MediaSentenceContextLines, 0, 5);
        var start = Math.Max(0, index - n);
        var end = Math.Min(_cues.Count - 1, index + n);
        return _cues.Skip(start).Take(end - start + 1).Select(WithSubtitleOffset).ToList();
    }

    private Avalonia.Controls.Window DialogOwner() => _popup.IsVisible ? _popup : (Avalonia.Controls.Window?)_main ?? _overlay;

    private Task<bool> RotateStateAsync(JitenWord word, bool forward)
    {
        // Match JitenMPV's rotation policy: RotateCycle keeps the rotation entirely among
        // the selected states; when it is off, the same ring also passes through a cleared
        // ("forget-add") slot. In either mode rotation wraps in both directions.
        var actions = new List<string>();
        if (!Settings.RotateCycle) actions.Add("forget-add");
        if (Settings.RotateCycleNeverForget) actions.Add("neverForget-add");
        if (Settings.RotateCycleBlacklist) actions.Add("blacklist-add");
        if (Settings.RotateCycleSuspended) actions.Add("suspend-add");
        if (actions.Count == (Settings.RotateCycle ? 0 : 1))
            actions.AddRange(["neverForget-add", "blacklist-add", "suspend-add"]);

        var current = JitenApiClient.CollapseKnownState(word);
        var currentAction = current switch { 5 => "neverForget-add", 3 => "blacklist-add", 7 => "suspend-add", _ => "forget-add" };
        var idx = actions.IndexOf(currentAction);
        if (idx < 0) idx = forward ? -1 : 0;
        var next = forward ? idx + 1 : idx - 1;
        next = (next % actions.Count + actions.Count) % actions.Count;
        return _jiten.SetVocabularyStateAsync(Settings.ApiBaseUrl, Settings.ApiKey, word.WordId, word.ReadingIndex, actions[next], Settings.ApiTimeoutSeconds);
    }

    private void HidePopup() { if (_popup.IsVisible) _popup.Hide(); }

    private void SetHoverPause(bool active, DateTime now)
    {
        if (!Settings.AutopauseEnabled) active = false;
        if (active)
        {
            if (_pauseHoverSince == DateTime.MinValue) _pauseHoverSince = now;
            if ((now - _pauseHoverSince).TotalMilliseconds < Math.Max(0, Settings.AutopauseDelayMs)) return;
            if (_hoverHold) return;
            _hoverHold = true;
            if (_mpc.PlayState == 0) { _pausedByHover = true; _mpc.Pause(); _log.Write("Hover autopause requested."); }
            else _pausedByHover = false;
        }
        else
        {
            _pauseHoverSince = DateTime.MinValue;
            if (!_hoverHold) return;
            _hoverHold = false;
            if (_pausedByHover) { _mpc.Play(); _log.Write("Hover autoresume requested."); }
            _pausedByHover = false;
        }
    }

    private void HidePlayerOverlays()
    {
        HidePopup();
        if (_overlay.IsVisible) _overlay.Hide();
        SetHoverPause(false, DateTime.UtcNow);
    }

    public IReadOnlyList<string> GetJapaneseFonts()
    {
        var result = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            foreach (var family in FontManager.Current.SystemFonts)
            {
                var name = family.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var typeface = new Typeface(new FontFamily(name), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
                if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyph) || glyph is null) continue;
                if (glyph.CharacterToGlyphMap.ContainsGlyph(0x65E5) && glyph.CharacterToGlyphMap.ContainsGlyph(0x3042) && glyph.CharacterToGlyphMap.ContainsGlyph(0x30A2)) result.Add(name);
            }
        }
        catch (Exception ex) { _log.Write("Japanese font enumeration failed: " + ex.Message); }
        if (!string.IsNullOrWhiteSpace(Settings.FontFamily)) result.Add(Settings.FontFamily);
        return result.ToList();
    }

    private void Status(string text)
    {
        StatusChanged?.Invoke(text); _main?.SetStatus(text);
        if (Settings.StatusOverlayEnabled && _mpc.IsConnected && WindowUtil.IsPlayerWindowVisible(_mpc.Hwnd)) _overlay.ShowStatus(text);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _mediaLoadCts?.Cancel(); _preparseCts?.Cancel();
        _mpc.Connected -= OnConnected;
        _mpc.Disconnected -= OnDisconnected;
        _mpc.LaunchedProcessExited -= OnMpcProcessExited;
        HidePlayerOverlays(); _mouseClickInterceptor?.Dispose(); _overlay.Close(); _popup.Close(); _mpc.Dispose();
    }
}
