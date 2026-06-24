using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WildToys.Modules.PowerSwitcher;

/// <summary>A top-level application window, identified by handle and title.</summary>
public class WindowItem
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Enumerates top-level application windows in MRU order and tracks focus
/// changes. Icon rendering is handled separately by the UI layer (WinUI),
/// so this stays free of any UI-framework dependency.
/// </summary>
public static class WindowManager
{
    public static bool ShowAllWindows { get; set; } = true;
    public static bool DebugExeTitle { get; set; } = false;

    private static readonly HashSet<string> _blacklist = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IntPtr> _mruList = new();
    private static Win32Interop.WinEventDelegate? _winEventDelegate;
    private static IntPtr _hWinEventHook = IntPtr.Zero;

    public static void InitializeMruTracking()
    {
        LoadBlacklist();

        lock (_mruList)
        {
            _mruList.Clear();
            IntPtr hWnd = Win32Interop.GetTopWindow(IntPtr.Zero);
            while (hWnd != IntPtr.Zero)
            {
                _mruList.Add(hWnd);
                hWnd = Win32Interop.GetWindow(hWnd, Win32Interop.GW_HWNDNEXT);
            }
        }

        _winEventDelegate = new Win32Interop.WinEventDelegate(WinEventProc);
        _hWinEventHook = Win32Interop.SetWinEventHook(
            Win32Interop.EVENT_SYSTEM_FOREGROUND, Win32Interop.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0,
            Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS);
    }

    private static void LoadBlacklist()
    {
        try
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string blacklistPath = Path.Combine(exeDir, "blacklist.txt");

            if (File.Exists(blacklistPath))
            {
                foreach (var line in File.ReadAllLines(blacklistPath))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        _blacklist.Add(trimmed);
                }
            }
            else
            {
                // Create a default blacklist.
                File.WriteAllText(blacklistPath, "TextInputHost.exe\nPopupHost\n");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PowerSwitcher] Failed to load blacklist: {ex}");
        }
    }

    public static void ShutdownMruTracking()
    {
        if (_hWinEventHook != IntPtr.Zero)
        {
            Win32Interop.UnhookWinEvent(_hWinEventHook);
            _hWinEventHook = IntPtr.Zero;
        }
    }

    private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == Win32Interop.EVENT_SYSTEM_FOREGROUND)
        {
            lock (_mruList)
            {
                _mruList.Remove(hwnd);
                _mruList.Insert(0, hwnd);
            }
        }
    }

    public static List<WindowItem> GetOpenWindows()
    {
        var windows = new List<WindowItem>();

        IntPtr hWnd = Win32Interop.GetTopWindow(IntPtr.Zero);
        while (hWnd != IntPtr.Zero)
        {
            if (IsAppWindow(hWnd))
            {
                string title = DebugExeTitle ? GetProcessName(hWnd) : GetWindowTitle(hWnd);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    windows.Add(new WindowItem { Hwnd = hWnd, Title = title });
                }
            }

            hWnd = Win32Interop.GetWindow(hWnd, Win32Interop.GW_HWNDNEXT);
        }

        lock (_mruList)
        {
            windows = windows.OrderBy(w =>
            {
                int index = _mruList.IndexOf(w.Hwnd);
                return index == -1 ? int.MaxValue : index;
            }).ToList();
        }

        return windows;
    }

    private static bool IsAppWindow(IntPtr hWnd)
    {
        if (!Win32Interop.IsWindowVisible(hWnd))
            return false;

        uint exStyle = Win32Interop.GetWindowLong(hWnd, Win32Interop.GWL_EXSTYLE);

        // Ignore tool windows.
        if ((exStyle & Win32Interop.WS_EX_TOOLWINDOW) != 0)
            return false;

        // The inner UWP CoreWindow is never the Alt+Tab representative (the
        // ApplicationFrameWindow is); closed Store apps leave it as a ghost.
        var cls = new StringBuilder(256);
        Win32Interop.GetClassName(hWnd, cls, cls.Capacity);
        if (cls.ToString() == "Windows.UI.Core.CoreWindow")
            return false;

        // Check if cloaked by DWM (background/suspended apps, or other desktops).
        Win32Interop.DwmGetWindowAttribute(hWnd, Win32Interop.DWMWA_CLOAKED, out int cloaked, sizeof(int));
        if (cloaked != 0)
        {
            if (!(ShowAllWindows && cloaked == Win32Interop.DWM_CLOAKED_SHELL))
                return false;

            // Only keep cloaked windows that live on a real virtual desktop.
            // Closed UWP apps leave cloaked ghosts with no desktop assignment.
            var deskId = WorkspaceManager.GetDesktopIdForWindow(hWnd);
            if (deskId == null || deskId == Guid.Empty)
                return false;
        }

        // Must have a title.
        if (Win32Interop.GetWindowTextLength(hWnd) == 0)
            return false;

        // Check blacklist (process name or window title).
        string procName = GetProcessName(hWnd);
        if (!string.IsNullOrEmpty(procName) && _blacklist.Contains(procName))
            return false;

        string windowTitle = GetWindowTitle(hWnd);
        if (!string.IsNullOrEmpty(windowTitle) && _blacklist.Contains(windowTitle))
            return false;

        return true;
    }

    private static string GetProcessName(IntPtr hWnd)
    {
        try
        {
            Win32Interop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != 0)
            {
                IntPtr hProc = Win32Interop.OpenProcess(Win32Interop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        if (Win32Interop.GetModuleFileNameEx(hProc, IntPtr.Zero, sb, sb.Capacity) > 0)
                            return Path.GetFileName(sb.ToString());
                    }
                    finally
                    {
                        Win32Interop.CloseHandle(hProc);
                    }
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int length = Win32Interop.GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        Win32Interop.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
