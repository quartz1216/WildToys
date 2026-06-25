using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WildToys.Modules.LumaEdges;

/// <summary>
/// Screen-edge hotkey zones. Installs a low-level mouse hook (WH_MOUSE_LL) and
/// supports two trigger styles:
///  - Click triggers: a left/right/middle button-down inside an edge/corner zone
///    blocks the click and fires the mapped hotkey.
///  - Hover (hot-corner) triggers: moving the cursor into a zone fires the mapped
///    hotkey with no click. Edge-triggered (fires once per entry) with a configurable
///    dwell delay before firing.
///
/// Unlike the original WPF implementation there are no transparent overlay windows:
/// the WPF "HotEdgeForm" overlays were near-invisible (opacity 0.01) debug-only aids
/// and played no part in detection, which is done entirely from the cursor position.
/// Must be started on a thread with a running message loop (the UI thread).
/// </summary>
public sealed class LumaEdgesModule : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);
    private LowLevelMouseProc? _mouseProcDelegate;
    private nint _mouseHookId = nint.Zero;
    private bool _isRunning;

    // Cached settings + screen bounds so the high-frequency WM_MOUSEMOVE path never
    // hits the disk or re-enumerates monitors. Refreshed on Start, on ReloadSettings
    // (called by the settings page after a save), and on display changes.
    private AppSettings _settings = new();
    private RECT[] _screens = Array.Empty<RECT>();

    private static readonly object CooldownLock = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(300);
    private static DateTimeOffset _lastTriggeredAt = DateTimeOffset.MinValue;

    private bool _lButtonBlocked;
    private bool _rButtonBlocked;
    private bool _mButtonBlocked;

    // Hover (hot-corner) state. _currentHoverZone is the zone the cursor is currently
    // in; we only act on transitions into a new zone, so each entry fires at most once.
    private HotZone _currentHoverZone = HotZone.None;
    private System.Threading.Timer? _hoverTimer;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _settings = SettingsService.Load();
        RefreshScreens();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _mouseProcDelegate = MouseHookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule != null)
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProcDelegate, GetModuleHandle(curModule.ModuleName), 0);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (_mouseHookId != nint.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = nint.Zero;
        }

        _hoverTimer?.Dispose();
        _hoverTimer = null;
        _currentHoverZone = HotZone.None;

        _mouseProcDelegate = null;
        _lButtonBlocked = false;
        _rButtonBlocked = false;
        _mButtonBlocked = false;
    }

    /// <summary>Re-reads settings into the module's cache. Call after saving from the UI
    /// so thickness/zone/hover changes take effect without re-hooking.</summary>
    public void ReloadSettings()
    {
        if (!_isRunning) return;
        _settings = SettingsService.Load();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RefreshScreens();

    private void RefreshScreens()
    {
        var rects = new List<RECT>();
        EnumDisplayMonitors(nint.Zero, nint.Zero,
            (nint _, nint _, ref RECT r, nint _) => { rects.Add(r); return true; },
            nint.Zero);
        _screens = rects.ToArray();
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
        if (nCode >= 0)
        {
            var msg = (int)wParam;

            // Ignore mouse input the app re-synthesized itself (e.g. MouseGesture
            // replaying a plain click). Without this an edge re-emit would re-trigger
            // us and swallow the app's own click.
            if (msg is WM_LBUTTONDOWN or WM_LBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP or WM_MBUTTONDOWN or WM_MBUTTONUP
                && Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).dwExtraInfo == InjectedInput.SelfTag)
            {
                return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
            }

            // Swallow the button-UP that pairs with a blocked DOWN so the target app
            // never sees a half-click.
            if (msg == WM_LBUTTONUP && _lButtonBlocked)
            {
                _lButtonBlocked = false;
                return 1;
            }
            if (msg == WM_RBUTTONUP && _rButtonBlocked)
            {
                _rButtonBlocked = false;
                return 1;
            }
            if (msg == WM_MBUTTONUP && _mButtonBlocked)
            {
                _mButtonBlocked = false;
                return 1;
            }

            if (msg == WM_MOUSEMOVE)
            {
                HandleHoverMove();
            }
            else if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                var detectedZone = DetectZone(GetCursor(), _settings.LumaEdgesThickness);
                if (detectedZone != HotZone.None)
                {
                    string? hotkey = null;
                    if (msg == WM_LBUTTONDOWN)
                        hotkey = _settings.LumaEdgesLeftZones.GetValueOrDefault(detectedZone.ToString());
                    else if (msg == WM_RBUTTONDOWN)
                        hotkey = _settings.LumaEdgesRightZones.GetValueOrDefault(detectedZone.ToString());
                    else if (msg == WM_MBUTTONDOWN)
                        hotkey = _settings.LumaEdgesMiddleZones.GetValueOrDefault(detectedZone.ToString());

                    if (!string.IsNullOrWhiteSpace(hotkey))
                    {
                        // Always swallow a click that lands in a mapped zone (and remember
                        // the button so its paired UP is swallowed too) so it never leaks to
                        // the app behind the edge. The cooldown only debounces the hotkey
                        // itself — without this, a click during the cooldown window would
                        // pass straight through (the click-through bug).
                        if (msg == WM_LBUTTONDOWN) _lButtonBlocked = true;
                        else if (msg == WM_RBUTTONDOWN) _rButtonBlocked = true;
                        else if (msg == WM_MBUTTONDOWN) _mButtonBlocked = true;

                        if (TryStartCooldown())
                            SendHotkeyAsync(hotkey);

                        return 1; // Block click
                    }
                }
            }
        }

        }
        catch (Exception ex)
        {
            CrashLog.Write("LumaEdges.MouseHookCallback", ex);
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private void HandleHoverMove()
    {
        if (!_settings.LumaEdgesHoverEnabled)
        {
            _currentHoverZone = HotZone.None;
            return;
        }

        var zone = DetectZone(GetCursor(), _settings.LumaEdgesThickness);
        if (zone == _currentHoverZone)
            return; // still in the same zone (or still outside) — nothing to do

        _currentHoverZone = zone;

        // Cancel any dwell that was pending for the zone we just left.
        _hoverTimer?.Dispose();
        _hoverTimer = null;

        if (zone == HotZone.None)
            return;

        var hotkey = _settings.LumaEdgesHoverZones.GetValueOrDefault(zone.ToString());
        if (string.IsNullOrWhiteSpace(hotkey))
            return;

        var targetZone = zone;
        var delay = Math.Max(0, _settings.LumaEdgesHoverDelayMs);
        _hoverTimer = new System.Threading.Timer(
            _ => OnHoverDwellElapsed(targetZone, hotkey), null, delay, System.Threading.Timeout.Infinite);
    }

    // Runs on a thread-pool thread after the dwell delay. Fire only if the cursor is
    // still in the same zone, so a quick pass-through doesn't trigger.
    private void OnHoverDwellElapsed(HotZone targetZone, string hotkey)
    {
        if (!_isRunning) return;
        if (DetectZone(GetCursor(), _settings.LumaEdgesThickness) != targetZone)
            return;
        if (!TryStartCooldown())
            return;
        HotkeySender.SendDetailed(hotkey);
    }

    private HotZone DetectZone(POINT position, int thickness)
    {
        foreach (var b in _screens)
        {
            var zone = HotZoneDetector.Detect(position.x, position.y, b.left, b.top, b.right, b.bottom, thickness);
            if (zone != HotZone.None)
                return zone;
        }
        return HotZone.None;
    }

    private static POINT GetCursor()
    {
        GetCursorPos(out var p);
        return p;
    }

    private static void SendHotkeyAsync(string hotkey)
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(10);
                HotkeySender.SendDetailed(hotkey);
            }
            catch (Exception ex)
            {
                CrashLog.Write("LumaEdges.SendHotkeyAsync", ex);
            }
        });
    }

    private static bool TryStartCooldown()
    {
        lock (CooldownLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastTriggeredAt < Cooldown)
            {
                return false;
            }

            _lastTriggeredAt = now;
            return true;
        }
    }

    public void Dispose() => Stop();

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
