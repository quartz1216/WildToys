using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WildToys.Modules.PowerSwitcher;

public enum MoveDirection
{
    Left, Right, Up, Down, Home, End, PageUp, PageDown
}

/// <summary>
/// Global low-level keyboard hook driving the switcher. Must be created on a
/// thread with a message loop (the UI thread); callbacks are delivered there,
/// so events are raised directly without marshaling.
/// </summary>
public class KeyboardHook : IDisposable
{
    private readonly Win32Interop.LowLevelKeyboardProc _proc;
    private IntPtr _hookID = IntPtr.Zero;

    public event EventHandler<bool>? AltTabOpen; // bool isSticky
    public event EventHandler? AltReleased;
    public event EventHandler? EnterPressed;
    public event EventHandler? EscPressed;
    public event EventHandler? QPressed;
    public event EventHandler<MoveDirection>? DirectionKeyPressed;

    public Func<bool>? IsSwitcherActive { get; set; }

    public KeyboardHook()
    {
        _proc = HookCallback;
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule!;
        _hookID = Win32Interop.SetWindowsHookEx(Win32Interop.WH_KEYBOARD_LL, _proc,
            Win32Interop.GetModuleHandle(curModule.ModuleName!), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32Interop.KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)hookStruct.vkCode;

            if (wParam == (IntPtr)Win32Interop.WM_SYSKEYDOWN || wParam == (IntPtr)Win32Interop.WM_KEYDOWN)
            {
                bool isAltPressed = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LMENU) & 0x8000) != 0 ||
                                    (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RMENU) & 0x8000) != 0;

                bool isShiftPressed = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LSHIFT) & 0x8000) != 0 ||
                                      (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RSHIFT) & 0x8000) != 0;

                bool isCtrlPressed = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LCONTROL) & 0x8000) != 0 ||
                                     (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RCONTROL) & 0x8000) != 0;

                bool isActive = IsSwitcherActive != null && IsSwitcherActive();

                if (!isActive)
                {
                    if (vkCode == Win32Interop.VK_TAB && isAltPressed && !isShiftPressed)
                    {
                        AltTabOpen?.Invoke(this, isCtrlPressed);
                        return (IntPtr)1;
                    }
                }
                else
                {
                    MoveDirection? dir = null;

                    if (vkCode == Win32Interop.VK_TAB)
                        dir = MoveDirection.Right;
                    else if (vkCode == 0x10 || vkCode == Win32Interop.VK_LSHIFT || vkCode == Win32Interop.VK_RSHIFT) // Shift, LShift, RShift
                        dir = MoveDirection.Left;
                    else if (vkCode == 0x44 || vkCode == 0x4C || vkCode == Win32Interop.VK_RIGHT) // D, L or Right
                        dir = MoveDirection.Right;
                    else if (vkCode == 0x41 || vkCode == 0x48 || vkCode == Win32Interop.VK_LEFT) // A, H or Left
                        dir = MoveDirection.Left;
                    else if ((vkCode == 0x53 || vkCode == 0x4A) && !isShiftPressed || vkCode == Win32Interop.VK_DOWN) // S, J or Down
                        dir = MoveDirection.Down;
                    else if (vkCode == 0x57 || vkCode == 0x4B || vkCode == Win32Interop.VK_UP) // W, K or Up
                        dir = MoveDirection.Up;
                    else if (vkCode == 0x24) // Home
                        dir = MoveDirection.Home;
                    else if (vkCode == 0x23) // End
                        dir = MoveDirection.End;
                    else if (vkCode == 0x21) // Page Up
                        dir = MoveDirection.PageUp;
                    else if (vkCode == 0x22) // Page Down
                        dir = MoveDirection.PageDown;

                    if (dir != null)
                    {
                        DirectionKeyPressed?.Invoke(this, dir.Value);
                        return (IntPtr)1;
                    }

                    if (vkCode == 0x0D || vkCode == 0x20) // Enter or Space
                    {
                        EnterPressed?.Invoke(this, EventArgs.Empty);
                        return (IntPtr)1;
                    }
                    if (vkCode == Win32Interop.VK_ESCAPE)
                    {
                        EscPressed?.Invoke(this, EventArgs.Empty);
                        return (IntPtr)1;
                    }
                    if (vkCode == 0x51) // Q
                    {
                        QPressed?.Invoke(this, EventArgs.Empty);
                        return (IntPtr)1;
                    }

                    // Block other typing keys while the switcher is active so they
                    // don't leak into background apps.
                    if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
                        return (IntPtr)1;
                }
            }
            else if (wParam == (IntPtr)Win32Interop.WM_KEYUP || wParam == (IntPtr)Win32Interop.WM_SYSKEYUP)
            {
                if (vkCode == Win32Interop.VK_LMENU || vkCode == Win32Interop.VK_RMENU)
                {
                    AltReleased?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        return Win32Interop.CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookID != IntPtr.Zero)
        {
            Win32Interop.UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }
}
