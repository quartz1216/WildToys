using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace WildToys.Modules.ImIndicator;

/// <summary>
/// Shows a brief badge with the current input language near the caret whenever the
/// foreground window's keyboard layout (HKL) changes — e.g. after Win+Space switches
/// between a US and a Japanese keyboard.
///
/// Detection is by polling GetKeyboardLayout for the foreground thread on a timer
/// (there is no robust global notification for input-language changes). The caret
/// location comes from GetGUIThreadInfo's rcCaret, falling back to the cursor for apps
/// that don't expose a Win32 caret. Must be started on the UI thread.
/// </summary>
public sealed class ImIndicatorModule : IDisposable
{
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _pollTimer;
    private ImIndicatorWindow? _window;
    private bool _running;
    private AppSettings _settings = new();

    private IntPtr _lastHkl = IntPtr.Zero;
    private bool _baselineSet;

    public void Start()
    {
        if (_running) return;
        _running = true;

        _settings = SettingsService.Load();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _window = new ImIndicatorWindow();

        _pollTimer = _dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(250);
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += (_, _) => Poll();
        _pollTimer.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        _pollTimer?.Stop();
        _pollTimer = null;

        _window?.Close();
        _window = null;

        _baselineSet = false;
        _lastHkl = IntPtr.Zero;
    }

    public void ReloadSettings()
    {
        if (_running) _settings = SettingsService.Load();
    }

    private void Poll()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return;

        uint threadId = GetWindowThreadProcessId(fg, out _);
        if (threadId == 0) return;

        IntPtr hkl = GetKeyboardLayout(threadId);

        // The first reading just establishes a baseline so we don't flash on startup.
        if (!_baselineSet)
        {
            _lastHkl = hkl;
            _baselineSet = true;
            return;
        }

        if (hkl == _lastHkl) return;
        _lastHkl = hkl;

        var (x, y) = GetCaretScreenPos(threadId);
        _window?.ShowAt(x, y, GetLanguageName(hkl), _settings.ImIndicatorDurationMs);
    }

    private static string GetLanguageName(IntPtr hkl)
    {
        int langId = (int)((long)hkl & 0xFFFF);
        try
        {
            // Drop the region suffix so it reads "日本語" / "English", not
            // "日本語 (日本)" / "English (United States)".
            var name = CultureInfo.GetCultureInfo(langId).NativeName;
            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            return paren > 0 ? name.Substring(0, paren) : name;
        }
        catch
        {
            return $"0x{langId:X4}";
        }
    }

    private static (int X, int Y) GetCaretScreenPos(uint threadId)
    {
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(threadId, ref gti) &&
            gti.hwndCaret != IntPtr.Zero &&
            gti.rcCaret.bottom > gti.rcCaret.top)
        {
            var p = new POINT { x = gti.rcCaret.left, y = gti.rcCaret.bottom };
            if (ClientToScreen(gti.hwndCaret, ref p))
                return (p.x, p.y + 4);
        }

        // Fallback: just below the cursor (apps like Chromium expose no Win32 caret).
        if (GetCursorPos(out var c))
            return (c.x, c.y + 20);

        return (0, 0);
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }
}
