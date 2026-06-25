using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WildToys.Modules.MouseWarp;

/// <summary>
/// Warps the cursor to the center of a window that has just become foreground
/// (e.g. after Alt+Tab or the Power Switcher). Rewritten from the original
/// polling-the-Alt-key loop to a EVENT_SYSTEM_FOREGROUND WinEvent hook, which is
/// event-driven and far more stable.
///
/// To behave like "warp on keyboard switch" without being coupled to the Alt key,
/// it skips warping when the cursor is already inside the newly-activated window
/// (i.e. the user activated it by clicking). Events from our own process (the Power
/// Switcher overlay) are filtered out via WINEVENT_SKIPOWNPROCESS.
///
/// Optionally animates the move with an ease-out curve; the animation and its
/// duration are configurable. Must be started on a thread with a running message
/// loop (the UI thread) so out-of-context WinEvents are delivered.
/// </summary>
public sealed class MouseWarpModule : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;

    private WinEventDelegate? _winEventDelegate;
    private nint _winEventHook;
    private bool _isRunning;
    private AppSettings _settings = new();
    private CancellationTokenSource? _animationCts;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _settings = SettingsService.Load();

        _winEventDelegate = OnForegroundChanged;
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            nint.Zero,
            _winEventDelegate,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (_winEventHook != nint.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = nint.Zero;
        }

        _animationCts?.Cancel();
        _animationCts = null;
        _winEventDelegate = null;
    }

    /// <summary>Re-reads settings into the module's cache. Call after saving from the UI.</summary>
    public void ReloadSettings()
    {
        if (_isRunning) _settings = SettingsService.Load();
    }

    private void OnForegroundChanged(
        nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isRunning) return;
        if (eventType != EVENT_SYSTEM_FOREGROUND) return;
        if (hwnd == nint.Zero || idObject != OBJID_WINDOW) return;
        if (IsIconic(hwnd)) return;
        if (!GetWindowRect(hwnd, out var rect)) return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return;

        var targetX = rect.Left + width / 2;
        var targetY = rect.Top + height / 2;

        // If the cursor is already inside this window the user most likely activated it
        // with the mouse, so don't yank the cursor to the center.
        if (GetCursorPos(out var cursor) &&
            cursor.X >= rect.Left && cursor.X < rect.Right &&
            cursor.Y >= rect.Top && cursor.Y < rect.Bottom)
        {
            return;
        }

        WarpTo(targetX, targetY);
    }

    private void WarpTo(int targetX, int targetY)
    {
        _animationCts?.Cancel();
        _animationCts = null;

        if (!_settings.MouseWarpAnimationEnabled || !GetCursorPos(out var start))
        {
            SetCursorPos(targetX, targetY);
            return;
        }

        var duration = Math.Max(1, _settings.MouseWarpAnimationDurationMs);
        var cts = new CancellationTokenSource();
        _animationCts = cts;
        _ = AnimateAsync(start.X, start.Y, targetX, targetY, duration, cts.Token);
    }

    private static async Task AnimateAsync(int sx, int sy, int tx, int ty, int durationMs, CancellationToken ct)
    {
        const int frameMs = 8; // request ~120 fps; actual cadence is timer-limited
        var sw = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var elapsed = sw.ElapsedMilliseconds;
                if (elapsed >= durationMs) break;

                var t = (double)elapsed / durationMs;
                var eased = EaseOutCubic(t);
                var x = (int)Math.Round(sx + (tx - sx) * eased);
                var y = (int)Math.Round(sy + (ty - sy) * eased);
                SetCursorPos(x, y);

                await Task.Delay(frameMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer warp
        }

        if (!ct.IsCancellationRequested)
            SetCursorPos(tx, ty);
    }

    private static double EaseOutCubic(double t)
    {
        var inv = 1.0 - t;
        return 1.0 - inv * inv * inv;
    }

    public void Dispose() => Stop();

    private delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
