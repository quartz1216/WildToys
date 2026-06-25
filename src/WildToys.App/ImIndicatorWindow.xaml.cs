using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;
using Win32 = WildToys.Modules.PowerSwitcher.Win32Interop;

namespace WildToys;

/// <summary>
/// A small, caret-anchored badge that briefly shows the current input language
/// (e.g. "日本語") when it changes. It never takes focus (WS_EX_NOACTIVATE +
/// AppWindow.Show(false)) so it does not disturb typing, and auto-hides after a
/// configurable delay. Borderless/topmost chrome follows the SwitcherWindow pattern.
/// </summary>
public sealed partial class ImIndicatorWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _visible;

    public ImIndicatorWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;

        // Never steal activation: it must not interrupt the foreground app's typing.
        uint exStyle = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);

        ApplyWindowChrome();

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideIndicator();

        _appWindow.Hide();
    }

    /// <summary>Show the badge with <paramref name="text"/> anchored just below the
    /// caret point (<paramref name="screenX"/>, <paramref name="screenY"/> in physical
    /// pixels), and auto-hide after <paramref name="durationMs"/>.</summary>
    public void ShowAt(int screenX, int screenY, string text, int durationMs)
    {
        LabelText.Text = text;

        // Provisional placement; resized to fit once layout settles.
        _appWindow.MoveAndResize(new RectInt32(screenX, screenY, 200, 56));
        _appWindow.Show(false);
        ApplyWindowChrome();
        _visible = true;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            RootGrid.UpdateLayout();
            PositionAt(screenX, screenY);
        });

        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, durationMs));
        _hideTimer.Start();
    }

    public void HideIndicator()
    {
        if (!_visible) return;
        _visible = false;
        _hideTimer.Stop();
        _appWindow.Hide();
    }

    private void PositionAt(int screenX, int screenY)
    {
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        double w = ContentRoot.ActualWidth;
        double h = ContentRoot.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int pw = (int)Math.Ceiling(w * scale);
        int ph = (int)Math.Ceiling(h * scale);

        // Keep the badge fully on the display that contains the caret.
        var area = DisplayArea.GetFromPoint(new PointInt32(screenX, screenY), DisplayAreaFallback.Nearest);
        var b = area.OuterBounds;

        int x = screenX;
        int y = screenY;
        if (x + pw > b.X + b.Width) x = b.X + b.Width - pw;
        if (x < b.X) x = b.X;
        if (y + ph > b.Y + b.Height) y = screenY - ph - 8; // flip above the caret
        if (y < b.Y) y = b.Y;

        _appWindow.MoveAndResize(new RectInt32(x, y, pw, ph));
    }

    // ---- Borderless/topmost chrome (same technique as SwitcherWindow) ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private WndProcDelegate? _wndProc;
    private IntPtr _oldWndProc;

    private void ApplyWindowChrome()
    {
        InstallBorderlessHook();

        int dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(_hwnd, 20, ref dark, sizeof(int));
        int cornerRound = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, 33, ref cornerRound, sizeof(int));
    }

    private void InstallBorderlessHook()
    {
        if (_oldWndProc != IntPtr.Zero) return;

        _wndProc = WndProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, -4 /* GWLP_WNDPROC */, _wndProc);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_NCCALCSIZE = 0x0083;
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            return IntPtr.Zero;
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }
}
