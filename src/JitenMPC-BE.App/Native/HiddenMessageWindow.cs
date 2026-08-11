using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JitenMpcBe.Native;

public sealed class MpcMessageEventArgs(uint command, string data) : EventArgs
{
    public uint Command { get; } = command;
    public string Data { get; } = data;
}

public sealed class HiddenMessageWindow : IDisposable
{
    private readonly string _className = "JitenMPCBE.ApiHost." + Guid.NewGuid().ToString("N");
    private readonly IntPtr _instance;
    private readonly NativeMethods.WndProc _wndProc;
    private bool _registered;

    public IntPtr Hwnd { get; private set; }
    public event EventHandler<MpcMessageEventArgs>? MessageReceived;

    public HiddenMessageWindow()
    {
        _instance = NativeMethods.GetModuleHandle(null);
        _wndProc = WndProc;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = _instance,
            lpszClassName = _className
        };
        if (NativeMethods.RegisterClassEx(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register MPC-BE IPC window class.");
        _registered = true;

        Hwnd = NativeMethods.CreateWindowEx(
            unchecked((uint)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE)),
            _className, "JitenMPC-BE MPC API Host", NativeMethods.WS_POPUP,
            -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create MPC-BE IPC window.");
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_COPYDATA && lParam != IntPtr.Zero)
        {
            var cds = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(lParam);
            var command = unchecked((uint)cds.dwData.ToInt64());
            var data = cds.lpData == IntPtr.Zero ? "" : Marshal.PtrToStringUni(cds.lpData) ?? "";
            MessageReceived?.Invoke(this, new MpcMessageEventArgs(command, data));
            return new IntPtr(1);
        }
        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public bool SendCommand(IntPtr target, uint command, string data = "")
    {
        if (target == IntPtr.Zero) return false;
        var text = Marshal.StringToHGlobalUni(data ?? "");
        try
        {
            var cds = new NativeMethods.COPYDATASTRUCT
            {
                dwData = new IntPtr(unchecked((long)command)),
                cbData = ((data?.Length ?? 0) + 1) * 2,
                lpData = text
            };
            var block = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.COPYDATASTRUCT>());
            try
            {
                Marshal.StructureToPtr(cds, block, false);
                NativeMethods.SendMessage(target, NativeMethods.WM_COPYDATA, Hwnd, block);
                return true;
            }
            finally { Marshal.FreeHGlobal(block); }
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    public void Dispose()
    {
        if (Hwnd != IntPtr.Zero) { NativeMethods.DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
        if (_registered) { NativeMethods.UnregisterClass(_className, _instance); _registered = false; }
        GC.KeepAlive(_wndProc);
    }
}
