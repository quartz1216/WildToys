using System;
using System.Runtime.InteropServices;

namespace WildToys;

/// <summary>
/// A full-virtual-screen backdrop shown behind the Power Switcher to dim and/or blur
/// the desktop. It is a bare Win32 popup (no activation, topmost) supporting two modes:
///  - dim  : a WS_EX_LAYERED window painted black at a uniform alpha. Reliable on all
///           builds (the SetWindowCompositionAttribute "transparent gradient" accent
///           renders as an opaque bluish fill on current Windows 11, so it isn't used).
///  - blur : SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND) with an
///           adjustable black tint alpha.
/// </summary>
internal sealed class SwitcherBackdrop
{
    private IntPtr _hwnd;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static WndProcDelegate? _wndProc; // static so the GC never collects it
    private static bool _classRegistered;
    private static bool _dimMode;             // single backdrop instance, so static is fine
    private const string ClassName = "WildToysSwitcherBackdrop";

    public IntPtr Handle => _hwnd;

    /// <summary>Show the backdrop over every monitor.</summary>
    /// <param name="blur">true for acrylic blur, false for a flat dim.</param>
    /// <param name="alpha">tint/dim alpha 0-255.</param>
    public void Show(bool blur, int alpha)
    {
        EnsureCreated();
        if (_hwnd == IntPtr.Zero) return;

        alpha = Math.Clamp(alpha, 0, 255);
        _dimMode = !blur;

        long ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        if (blur)
        {
            // Acrylic blur: not layered; DWM composites the blur + tint.
            ex &= ~WS_EX_LAYERED;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)ex);
            ApplyAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, alpha);
        }
        else
        {
            // Flat dim: a black layered window at uniform alpha.
            ApplyAccent(ACCENT_DISABLED, 0);
            ex |= WS_EX_LAYERED;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)ex);
            SetLayeredWindowAttributes(_hwnd, 0, (byte)alpha, LWA_ALPHA);
        }

        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    /// <summary>Re-apply just the tint/dim alpha (for fade-in), keeping the current mode.</summary>
    public void SetAlpha(bool blur, int alpha)
    {
        if (_hwnd == IntPtr.Zero) return;
        alpha = Math.Clamp(alpha, 0, 255);
        if (blur)
            ApplyAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, alpha);
        else
            SetLayeredWindowAttributes(_hwnd, 0, (byte)alpha, LWA_ALPHA);
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero)
            ShowWindow(_hwnd, SW_HIDE);
    }

    /// <summary>Drop the backdrop directly behind <paramref name="above"/> so the
    /// switcher window stays on top of it.</summary>
    public void PlaceBehind(IntPtr above)
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void EnsureCreated()
    {
        if (_hwnd != IntPtr.Zero) return;
        IntPtr hInst = GetModuleHandle(null);

        if (!_classRegistered)
        {
            _wndProc = WndProc;
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInst,
                lpszClassName = ClassName,
            };
            RegisterClassExW(ref wc);
            _classRegistered = true;
        }

        _hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
            ClassName, string.Empty, WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
    }

    // Paint black in dim mode so the layered alpha yields translucent black; paint
    // nothing in blur mode so the acrylic shows through.
    private static IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_ERASEBKGND)
        {
            if (_dimMode)
            {
                GetClientRect(h, out RECT rc);
                FillRect(w, ref rc, GetStockObject(BLACK_BRUSH));
            }
            return (IntPtr)1;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    private void ApplyAccent(int state, int alpha)
    {
        var accent = new ACCENT_POLICY
        {
            AccentState = state,
            GradientColor = (uint)((alpha & 0xFF) << 24), // ABGR: alpha + black
        };

        int size = Marshal.SizeOf<ACCENT_POLICY>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINCOMPATTRDATA { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(_hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // ---- constants ----
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const long WS_EX_LAYERED = 0x00080000;
    private const int GWL_EXSTYLE = -20;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private const uint LWA_ALPHA = 0x2;
    private const int BLACK_BRUSH = 4;
    private const uint WM_ERASEBKGND = 0x0014;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    // ---- structs ----
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINCOMPATTRDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    // ---- P/Invoke ----
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int i);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
