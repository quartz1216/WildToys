using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WildToys.Modules.LumaEdges;

namespace WildToys.Modules.MouseGesture;

/// <summary>
/// Right- or middle-button drag gestures. A WH_MOUSE_LL hook records the drag while
/// the gesture button is held, reduces the path to a chainable 4-direction sequence
/// (e.g. "Right,Left,Up"), and on release matches it (plus button and foreground
/// process) against the configured entries, firing the entry's hotkey via HotkeySender.
///
/// The gesture button's DOWN is blocked so it doesn't reach the app mid-gesture; if the
/// user just clicked without dragging, the click is re-synthesized (tagged with a magic
/// dwExtraInfo the hook ignores) so context menus / middle-clicks still work. Entries
/// with a process apply only to that app; entries with an empty process are global and a
/// process-specific match wins over a global one. Must be started on the UI thread.
/// </summary>
public sealed class MouseGestureModule : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;

    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    private const int SegmentThreshold = 30; // px before a stroke direction is committed

    private const string Right = "Right";
    private const string Middle = "Middle";

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);
    private LowLevelMouseProc? _proc;
    private nint _hookId;
    private bool _isRunning;
    private AppSettings _settings = new();

    private bool _recording;
    private string _activeButton = "";
    private POINT _lastPoint;
    private readonly List<string> _dirs = new();
    private nint _gestureWindow;

    /// <summary>While true, the hook passes every event through untouched. The settings
    /// "draw a gesture" pad sets this so the global hook doesn't swallow the right/middle
    /// button events it needs to capture the drawing.</summary>
    public bool SuspendForCapture { get; set; }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _settings = SettingsService.Load();

        _proc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule != null)
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (_hookId != nint.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = nint.Zero;
        }

        _proc = null;
        _recording = false;
        _dirs.Clear();
    }

    public void ReloadSettings()
    {
        if (_isRunning) _settings = SettingsService.Load();
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && !SuspendForCapture)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            // Ignore the clicks we re-synthesize ourselves.
            if (info.dwExtraInfo != InjectedInput.SelfTag)
            {
                switch ((int)wParam)
                {
                    case WM_RBUTTONDOWN: return OnButtonDown(Right, info);
                    case WM_MBUTTONDOWN: return OnButtonDown(Middle, info);
                    case WM_MOUSEMOVE: OnMove(info); break;
                    case WM_RBUTTONUP: if (_recording && _activeButton == Right) return OnButtonUp(Right, info); break;
                    case WM_MBUTTONUP: if (_recording && _activeButton == Middle) return OnButtonUp(Middle, info); break;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private nint OnButtonDown(string button, MSLLHOOKSTRUCT info)
    {
        // Only intercept the button if at least one entry uses it; otherwise leave the
        // click alone so normal right/middle clicks are unaffected.
        if (_recording || !HasEntriesForButton(button))
            return CallNextHookEx(_hookId, 0, 0, 0);

        _recording = true;
        _activeButton = button;
        _dirs.Clear();
        _lastPoint = info.pt;
        _gestureWindow = GetForegroundWindow();
        return 1; // block the down until we know whether it's a gesture or a click
    }

    private void OnMove(MSLLHOOKSTRUCT info)
    {
        if (!_recording) return;

        int dx = info.pt.x - _lastPoint.x;
        int dy = info.pt.y - _lastPoint.y;
        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) < SegmentThreshold)
            return;

        string dir = System.Math.Abs(dx) > System.Math.Abs(dy)
            ? (dx > 0 ? Right : "Left")
            : (dy > 0 ? "Down" : "Up");

        if (_dirs.Count == 0 || _dirs[^1] != dir)
            _dirs.Add(dir);

        _lastPoint = info.pt;
    }

    private nint OnButtonUp(string button, MSLLHOOKSTRUCT info)
    {
        _recording = false;

        if (_dirs.Count == 0)
        {
            // No drag: it was a plain click — replay it so the app gets it.
            ReemitClick(button);
            return 1; // block the original up; the re-synthesized pair carries the magic tag
        }

        var sequence = NormalizeSequence(string.Join(",", _dirs));
        _dirs.Clear();

        var hotkey = MatchAction(button, sequence, _gestureWindow);
        if (!string.IsNullOrWhiteSpace(hotkey))
        {
            var captured = hotkey;
            Task.Run(() => HotkeySender.SendDetailed(captured));
        }

        return 1; // swallow the up: the gesture is consumed whether or not it matched
    }

    private bool HasEntriesForButton(string button) =>
        _settings.MouseGestures.Any(e => ButtonEquals(e.Button, button) && !string.IsNullOrWhiteSpace(e.Gesture));

    private string? MatchAction(string button, string sequence, nint window)
    {
        string process = GetProcessName(window);
        GestureEntry? global = null;
        GestureEntry? specific = null;

        foreach (var e in _settings.MouseGestures)
        {
            if (!ButtonEquals(e.Button, button)) continue;
            if (!string.Equals(NormalizeSequence(e.Gesture), sequence, StringComparison.OrdinalIgnoreCase)) continue;

            if (string.IsNullOrWhiteSpace(e.Process))
                global ??= e;
            else if (string.Equals(e.Process.Trim(), process, StringComparison.OrdinalIgnoreCase))
                specific ??= e;
        }

        return (specific ?? global)?.Action;
    }

    private static string NormalizeSequence(string sequence)
    {
        var parts = sequence.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var raw in parts)
        {
            var dir = raw.ToLowerInvariant() switch
            {
                "up" or "u" => "Up",
                "down" or "d" => "Down",
                "left" or "l" => "Left",
                "right" or "r" => "Right",
                _ => ""
            };
            if (dir.Length == 0) continue;
            if (result.Count == 0 || result[^1] != dir) result.Add(dir);
        }
        return string.Join(",", result);
    }

    private static bool ButtonEquals(string a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);

    private static string GetProcessName(nint window)
    {
        if (window == nint.Zero) return "";
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0) return "";
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private static void ReemitClick(string button)
    {
        var (down, up) = button == Right
            ? (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP)
            : (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
        mouse_event(down, 0, 0, 0, InjectedInput.SelfTag);
        mouse_event(up, 0, 0, 0, InjectedInput.SelfTag);
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
