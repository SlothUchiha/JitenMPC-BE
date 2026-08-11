using System.Runtime.InteropServices;

namespace JitenMpcBe.Native;

[StructLayout(LayoutKind.Sequential)]
public struct WinRect
{
    public int Left, Top, Right, Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Width > 0 && Height > 0;
}

[StructLayout(LayoutKind.Sequential)]
public struct WinPoint { public int X, Y; }

internal static class NativeMethods
{
    public const int WM_COPYDATA = 0x004A;
    public const int GWL_EXSTYLE = -20;
    public const int GWLP_HWNDPARENT = -8;
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_LAYERED = 0x00080000L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;
    public const long WS_EX_TOPMOST = 0x00000008L;
    public const int WH_MOUSE_LL = 14;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WS_POPUP = 0x80000000;
    public const uint GA_ROOT = 2;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public WinPoint pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(IntPtr hWnd, out WinRect rect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(IntPtr hWnd, ref WinPoint point);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool EnableWindow(IntPtr hWnd, bool enable);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out WinPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr value);

    internal static long GetWindowLongPtrValue(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index).ToInt64() : GetWindowLong32(hwnd, index).ToInt64();

    internal static void SetWindowLongPtrValue(IntPtr hwnd, int index, long value)
    {
        var p = new IntPtr(value);
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, index, p);
        else SetWindowLong32(hwnd, index, p);
    }
}

public static class WindowUtil
{
    public static IntPtr GetRootWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public static IntPtr GetPlayerHostWindow(IntPtr hwnd)
    {
        var root = GetRootWindow(hwnd);
        if (root == IntPtr.Zero || !NativeMethods.IsWindow(root)) return IntPtr.Zero;
        NativeMethods.GetWindowThreadProcessId(root, out var pid);
        if (pid == 0) return root;

        var best = IntPtr.Zero;
        long bestArea = -1;
        NativeMethods.EnumWindowsProc callback = (candidate, _) =>
        {
            if (!NativeMethods.IsWindowVisible(candidate) || NativeMethods.IsIconic(candidate)) return true;
            NativeMethods.GetWindowThreadProcessId(candidate, out var candidatePid);
            if (candidatePid != pid || !NativeMethods.GetWindowRect(candidate, out var rect) || !rect.IsValid) return true;
            if (rect.Width < 200 || rect.Height < 120) return true;
            var area = (long)rect.Width * rect.Height;
            if (area > bestArea)
            {
                best = candidate;
                bestArea = area;
            }
            return true;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);

        if (best != IntPtr.Zero) return best;
        return NativeMethods.IsWindowVisible(root) && !NativeMethods.IsIconic(root) ? root : IntPtr.Zero;
    }

    public static bool IsPlayerWindowVisible(IntPtr hwnd)
    {
        var host = GetPlayerHostWindow(hwnd);
        return host != IntPtr.Zero && NativeMethods.IsWindow(host) &&
               NativeMethods.IsWindowVisible(host) && !NativeMethods.IsIconic(host);
    }

    public static WinRect GetClientScreenRect(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client)) return default;
        var p = new WinPoint();
        if (!NativeMethods.ClientToScreen(hwnd, ref p)) return default;
        return new WinRect { Left = p.X, Top = p.Y, Right = p.X + client.Right, Bottom = p.Y + client.Bottom };
    }

    public static WinRect GetBestVideoRect(IntPtr main)
    {
        main = GetPlayerHostWindow(main);
        var fallback = GetClientScreenRect(main);
        if (!fallback.IsValid) return fallback;

        var best = default(WinRect);
        long bestArea = 0;
        NativeMethods.EnumWindowsProc callback = (child, _) =>
        {
            if (!NativeMethods.IsWindowVisible(child) || !NativeMethods.GetWindowRect(child, out var r)) return true;
            if (r.Width < 200 || r.Height < 120) return true;
            var area = (long)r.Width * r.Height;
            if (area > bestArea && r.Left >= fallback.Left - 20 && r.Top >= fallback.Top - 20 &&
                r.Right <= fallback.Right + 20 && r.Bottom <= fallback.Bottom + 20)
            {
                best = r;
                bestArea = area;
            }
            return true;
        };
        NativeMethods.EnumChildWindows(main, callback, IntPtr.Zero);
        var fallbackArea = (long)fallback.Width * fallback.Height;
        return bestArea > fallbackArea / 3 ? best : fallback;
    }

    public static double GetScaleForWindow(IntPtr hwnd)
    {
        hwnd = GetPlayerHostWindow(hwnd);
        if (hwnd == IntPtr.Zero) return 1;
        try
        {
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            if (dpi is >= 48 and <= 768) return dpi / 96.0;
        }
        catch (EntryPointNotFoundException) { }
        return 1;
    }

    public static bool TryGetCursor(out WinPoint point) => NativeMethods.GetCursorPos(out point);
    public static bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    public static bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public static bool IsPlayerForeground(IntPtr player)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || player == IntPtr.Zero) return false;
        var host = GetPlayerHostWindow(player);
        if (host == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
        NativeMethods.GetWindowThreadProcessId(host, out var playerPid);
        return foregroundPid != 0 && foregroundPid == playerPid;
    }

    public static void MakeOverlayClickThrough(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    public static void MakeNonActivatingToolWindow(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    public static void MakeOverlayInputWindow(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE);
        style &= ~NativeMethods.WS_EX_TRANSPARENT;
        style |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    public static void SetOwner(IntPtr hwnd, IntPtr owner)
    {
        if (hwnd != IntPtr.Zero && owner != IntPtr.Zero)
            NativeMethods.SetWindowLongPtrValue(hwnd, NativeMethods.GWLP_HWNDPARENT, owner.ToInt64());
    }

    public static void BringToFrontNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public static void SyncAbovePlayer(IntPtr hwnd, IntPtr player)
    {
        if (hwnd == IntPtr.Zero) return;
        player = GetPlayerHostWindow(player);
        if (player == IntPtr.Zero) return;

        var playerStyle = NativeMethods.GetWindowLongPtrValue(player, NativeMethods.GWL_EXSTYLE);
        var ownStyle = NativeMethods.GetWindowLongPtrValue(hwnd, NativeMethods.GWL_EXSTYLE);
        var playerTopmost = (playerStyle & NativeMethods.WS_EX_TOPMOST) != 0;
        var ownTopmost = (ownStyle & NativeMethods.WS_EX_TOPMOST) != 0;

        if (playerTopmost)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        else
        {
            if (ownTopmost)
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }
}
