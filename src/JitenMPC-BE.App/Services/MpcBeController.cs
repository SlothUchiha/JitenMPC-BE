using System.Diagnostics;
using System.Globalization;
using JitenMpcBe.Native;

namespace JitenMpcBe.Services;

public sealed class MpcBeController : IDisposable
{
    public const uint CMD_CONNECT = 0x50000000, CMD_STATE = 0x50000001, CMD_PLAYMODE = 0x50000002,
        CMD_NOWPLAYING = 0x50000003, CMD_PLAYLIST = 0x50000006, CMD_CURRENTPOSITION = 0x50000007,
        CMD_NOTIFYSEEK = 0x50000008, CMD_VERSION = 0x5000000A, CMD_DISCONNECT = 0x5000000B,
        CMD_GETNOWPLAYING = 0xA0003002, CMD_GETPLAYLIST = 0xA0003003,
        CMD_GETCURRENTPOSITION = 0xA0003004, CMD_GETVERSION = 0xA0003006,
        CMD_SETPOSITION = 0xA0002000, CMD_PLAY = 0xA0000004, CMD_PAUSE = 0xA0000005;

    private readonly HiddenMessageWindow _host;
    private readonly FileLogger _log;
    private string _mpcExecutable = "";
    private string _pendingMedia = "";
    private Process? _launchedProcess;
    private bool _disposed;

    public IntPtr Hwnd { get; private set; }
    public string Version { get; private set; } = "";
    public string MediaPath { get; private set; } = "";
    public double PositionSeconds { get; private set; }
    public int PlayState { get; private set; } = -1;
    public IntPtr HostHwnd => _host.Hwnd;
    public bool IsConnected => Hwnd != IntPtr.Zero && WindowUtil.IsWindow(Hwnd);

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action? LaunchedProcessExited;
    public event Action<string>? VersionChanged;
    public event Action<string>? MediaPathChanged;
    public event Action<double>? PositionChanged;
    public event Action<int>? PlayStateChanged;

    public MpcBeController(FileLogger log)
    {
        _log = log;
        _host = new HiddenMessageWindow();
        _host.MessageReceived += OnMessage;
        _log.Write("API host HWND=" + _host.Hwnd.ToInt64());
    }

    public void Launch(string executable)
    {
        if (IsConnected) throw new InvalidOperationException("JitenMPC-BE is already connected to MPC-BE.");
        if (!File.Exists(executable)) throw new FileNotFoundException("MPC-BE executable was not found.", executable);
        _mpcExecutable = executable;
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false };
        psi.ArgumentList.Add("/slave");
        psi.ArgumentList.Add(_host.Hwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
        _log.Write($"Launching {executable} /slave {_host.Hwnd.ToInt64()}");
        ReleaseLaunchedProcess();
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Windows could not start MPC-BE.");
        _launchedProcess = process;
        process.EnableRaisingEvents = true;
        process.Exited += OnLaunchedProcessExited;
    }

    private void OnLaunchedProcessExited(object? sender, EventArgs e)
    {
        _log.Write("Launched MPC-BE process exited.");
        LaunchedProcessExited?.Invoke();
    }

    private void ReleaseLaunchedProcess()
    {
        if (_launchedProcess is null) return;
        try { _launchedProcess.Exited -= OnLaunchedProcessExited; } catch { }
        try { _launchedProcess.Dispose(); } catch { }
        _launchedProcess = null;
    }

    public bool Send(uint command, string data = "") => IsConnected && _host.SendCommand(Hwnd, command, data);
    public void PollPosition() => Send(CMD_GETCURRENTPOSITION);
    public void RequestNowPlaying() => Send(CMD_GETNOWPLAYING);
    public void RequestPlaylist() => Send(CMD_GETPLAYLIST);
    public void Pause() => Send(CMD_PAUSE);
    public void Play() => Send(CMD_PLAY);
    public void Seek(double seconds) => Send(CMD_SETPOSITION, Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture));

    private void OnMessage(object? sender, MpcMessageEventArgs e)
    {
        try
        {
            switch (e.Command)
            {
                case CMD_CONNECT:
                    if (long.TryParse(e.Data.Trim('\0', ' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                    {
                        Hwnd = new IntPtr(h);
                        _log.Write("MPC connected HWND=" + h);
                        Connected?.Invoke();
                        Send(CMD_GETVERSION); Send(CMD_GETNOWPLAYING);
                    }
                    break;
                case CMD_VERSION:
                    Version = e.Data.Trim('\0'); _log.Write("MPC version " + Version); VersionChanged?.Invoke(Version); break;
                case CMD_PLAYMODE:
                    if (int.TryParse(e.Data.Trim('\0', ' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out var state))
                    {
                        PlayState = state; _log.Write("MPC play state=" + state); PlayStateChanged?.Invoke(state);
                    }
                    break;
                case CMD_STATE:
                    _log.Write("MPC state=" + e.Data.Trim('\0'));
                    break;
                case CMD_CURRENTPOSITION:
                case CMD_NOTIFYSEEK:
                    if (double.TryParse(e.Data.Trim('\0', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var pos))
                    {
                        // MPC-BE can occasionally deliver a slightly older position sample immediately after a
                        // newer one while playback is advancing. Accept explicit seek notifications, but suppress
                        // small backwards poll jitter so the subtitle timeline cannot momentarily return to the
                        // cue that just ended. Larger backwards jumps are treated as real user seeks.
                        var explicitSeek = e.Command == CMD_NOTIFYSEEK;
                        var backwards = PositionSeconds - pos;
                        if (!explicitSeek && PlayState == 0 && backwards > 0 && backwards <= 0.75)
                        {
                            _log.Write($"Ignored stale backwards position sample {pos:0.###} after {PositionSeconds:0.###}.");
                            break;
                        }
                        PositionSeconds = pos; PositionChanged?.Invoke(pos);
                    }
                    break;
                case CMD_NOWPLAYING:
                    _log.Write("NOWPLAYING raw payload: [" + e.Data + "]");
                    HandleNowPlaying(e.Data);
                    break;
                case CMD_PLAYLIST:
                    _log.Write("PLAYLIST raw payload: [" + e.Data + "]");
                    HandlePlaylist(e.Data);
                    break;
                case CMD_DISCONNECT:
                    ResetConnection(); Disconnected?.Invoke(); break;
            }
        }
        catch (Exception ex) { _log.Write("MPC message handler error: " + ex); }
    }

    private void HandleNowPlaying(string data)
    {
        var fields = SplitFields(data);
        var raw = fields.Length >= 4 ? fields[3] : fields.FirstOrDefault() ?? "";
        _pendingMedia = raw;
        if (!TryAcceptMedia(raw, "NOWPLAYING")) RequestPlaylist();
    }

    private void HandlePlaylist(string data)
    {
        var fields = SplitFields(data);
        if (fields.Length < 2) return;
        if (!int.TryParse(fields[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var active)) return;
        if (active < 0 || active >= fields.Length - 1) return;
        TryAcceptMedia(fields[active], $"PLAYLIST[{active}]");
    }

    private bool TryAcceptMedia(string raw, string source)
    {
        var normalized = NormalizePath(raw);
        _log.Write($"Media path from {source}: raw=[{raw}] normalized=[{normalized}]");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalized)) candidates.Add(normalized);
        if (!string.IsNullOrWhiteSpace(normalized) && !Path.IsPathRooted(normalized))
        {
            candidates.Add(Path.Combine(Environment.CurrentDirectory, normalized));
            var mpcDir = string.IsNullOrWhiteSpace(_mpcExecutable) ? "" : Path.GetDirectoryName(_mpcExecutable) ?? "";
            if (mpcDir.Length > 0) candidates.Add(Path.Combine(mpcDir, normalized));
        }
        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var resolved = Path.GetFullPath(candidate);
                _pendingMedia = "";
                if (!string.Equals(MediaPath, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    MediaPath = resolved;
                    _log.Write($"Accepted media path from {source}: {resolved}");
                    MediaPathChanged?.Invoke(resolved);
                }
                return true;
            }
            catch (Exception ex) { _log.Write($"Path probe failed for [{candidate}]: {ex.Message}"); }
        }
        return false;
    }

    private static string NormalizePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var p = raw.Trim().Trim('\0');
        if (p.Length >= 2 && ((p[0] == '"' && p[^1] == '"') || (p[0] == '\'' && p[^1] == '\''))) p = p[1..^1];
        p = Environment.ExpandEnvironmentVariables(p);
        if (p.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(p, UriKind.Absolute, out var uri) && uri.IsFile) p = uri.LocalPath;
        if (System.Text.RegularExpressions.Regex.IsMatch(p, "%[0-9A-Fa-f]{2}"))
        {
            try { p = Uri.UnescapeDataString(p); } catch { }
        }
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) p = p[4..];
        return p.Trim();
    }

    public static string[] SplitFields(string value)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length && value[i + 1] == '|') { sb.Append('|'); i++; continue; }
            if (ch == '|') { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private void ResetConnection()
    {
        Hwnd = IntPtr.Zero; Version = ""; MediaPath = ""; PositionSeconds = 0; PlayState = -1; _pendingMedia = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.MessageReceived -= OnMessage;
        ReleaseLaunchedProcess();
        _host.Dispose();
    }
}
