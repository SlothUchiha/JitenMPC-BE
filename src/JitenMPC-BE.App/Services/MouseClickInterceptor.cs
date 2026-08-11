using System.Runtime.InteropServices;
using Avalonia.Threading;
using JitenMpcBe.Native;

namespace JitenMpcBe.Services;

/// <summary>
/// Suppresses only clicks that land on JitenMPC-BE's overlay controls while leaving
/// the full-screen subtitle overlay click-through to MPC-BE everywhere else.
/// </summary>
public sealed class MouseClickInterceptor : IDisposable
{
    private readonly Func<WinPoint, string?> _hitTest;
    private readonly Action<string> _onAction;
    private readonly Func<IntPtr> _playerWindow;
    private readonly FileLogger _log;
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hook;
    private bool _swallowLeftUp;
    private string? _deferredAction;
    private IntPtr _temporarilyDisabledPlayer;

    public MouseClickInterceptor(Func<WinPoint, string?> hitTest, Action<string> onAction, Func<IntPtr> playerWindow, FileLogger log)
    {
        _hitTest = hitTest;
        _onAction = onAction;
        _playerWindow = playerWindow;
        _log = log;
        _proc = HookProc;

        if (!OperatingSystem.IsWindows()) return;
        var module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, module, 0);
        if (_hook == IntPtr.Zero)
            _log.Write("Mouse click interceptor could not install; overlay buttons may click through to MPC-BE.");
        else
            _log.Write("Mouse click interceptor installed.");
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = unchecked((uint)wParam.ToInt64());
            if (message == NativeMethods.WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                string? action = null;
                try { action = _hitTest(data.pt); }
                catch (Exception ex) { _log.Write("Overlay control hit-test failed: " + ex.Message); }

                if (!string.IsNullOrWhiteSpace(action))
                {
                    _swallowLeftUp = true;

                    // Opening a new top-level settings window while the physical mouse click is
                    // still held down can let the remainder of that same gesture reactivate
                    // MPC-BE. Finish swallowing the complete down/up gesture first, then show
                    // Settings. Player-local actions can still execute immediately.
                    if (string.Equals(action, "Settings", StringComparison.Ordinal))
                    {
                        _deferredAction = action;
                        _temporarilyDisabledPlayer = _playerWindow();
                        if (_temporarilyDisabledPlayer != IntPtr.Zero && NativeMethods.IsWindow(_temporarilyDisabledPlayer))
                        {
                            NativeMethods.EnableWindow(_temporarilyDisabledPlayer, false);
                            _log.Write($"Overlay Settings click captured; disabled MPC-BE host HWND={_temporarilyDisabledPlayer} until swallowed button-up.");

                            // Absolute failsafe: never leave MPC-BE disabled if Windows fails to deliver the matching up event.
                            var disabled = _temporarilyDisabledPlayer;
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(2500);
                                if (_temporarilyDisabledPlayer == disabled && disabled != IntPtr.Zero && NativeMethods.IsWindow(disabled))
                                {
                                    NativeMethods.EnableWindow(disabled, true);
                                    _temporarilyDisabledPlayer = IntPtr.Zero;
                                    _log.Write("Settings click capture failsafe re-enabled MPC-BE host.");
                                }
                            });
                        }
                        else
                        {
                            _log.Write("Overlay Settings click captured; no valid MPC-BE host was available to disable.");
                        }
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() => _onAction(action));
                    }
                    return new IntPtr(1);
                }
            }
            else if (message == NativeMethods.WM_LBUTTONUP && _swallowLeftUp)
            {
                _swallowLeftUp = false;
                var deferred = _deferredAction;
                _deferredAction = null;
                var disabled = _temporarilyDisabledPlayer;
                _temporarilyDisabledPlayer = IntPtr.Zero;
                if (!string.IsNullOrWhiteSpace(deferred))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            _onAction(deferred);
                        }
                        finally
                        {
                            if (disabled != IntPtr.Zero && NativeMethods.IsWindow(disabled))
                            {
                                NativeMethods.EnableWindow(disabled, true);
                                _log.Write($"Settings click button-up swallowed; re-enabled MPC-BE host HWND={disabled} after activating Settings.");
                            }
                        }
                    });
                }
                else if (disabled != IntPtr.Zero && NativeMethods.IsWindow(disabled))
                {
                    NativeMethods.EnableWindow(disabled, true);
                }
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_temporarilyDisabledPlayer != IntPtr.Zero && NativeMethods.IsWindow(_temporarilyDisabledPlayer))
        {
            NativeMethods.EnableWindow(_temporarilyDisabledPlayer, true);
            _temporarilyDisabledPlayer = IntPtr.Zero;
        }
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
