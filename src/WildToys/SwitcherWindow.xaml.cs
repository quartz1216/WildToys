using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WildToys.Modules.PowerSwitcher;
using WinRT.Interop;
using Win32 = WildToys.Modules.PowerSwitcher.Win32Interop;

namespace WildToys;

public sealed partial class SwitcherWindow : Window
{
    private sealed class WorkspaceRow
    {
        public WorkspaceItem Workspace = new();
        public List<WindowItem> Windows = new();
    }

    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromArgb(0xFF, 0x31, 0x31, 0x31));

    private readonly WindowIconService _iconService = new();
    private readonly SwitcherBackdrop _backdrop = new();
    private readonly List<WorkspaceRow> _grid = new();
    private readonly List<List<Border>> _cells = new();

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;

    private int _selectedRow;
    private int _selectedCol;
    private bool _sticky;
    private bool _active;   // session is live (grid built) — may precede the window being shown
    private bool _visible;  // the switcher window is actually on screen
    private IntPtr _thumbnailId = IntPtr.Zero;

    private DispatcherQueueTimer? _showTimer;
    private DispatcherQueueTimer? _fadeTimer;
    private DateTime _fadeStart;
    private bool _backdropShown;
    private bool _fadeBlur;
    private int _fadeTargetAlpha;
    private const int FadeDurationMs = 140;

    public bool IsSwitcherActive => _grid.Count > 0;

    public SwitcherWindow()
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

        // Never steal activation: input arrives via the global keyboard hook.
        uint exStyle = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_NOACTIVATE);

        ApplyWindowChrome();

        _appWindow.Hide();
    }

    public void ShowSwitcher(bool sticky)
    {
        try
        {
            ShowSwitcherCore(sticky);
        }
        catch (Exception ex)
        {
            PsLog.Write($"ShowSwitcher EXCEPTION: {ex}");
        }
    }

    private void ShowSwitcherCore(bool sticky)
    {
        if (_active) return;
        _active = true;
        _sticky = sticky;

        BuildGrid();
        if (_grid.Count == 0)
        {
            _active = false;
            return;
        }

        RebuildVisuals();

        _selectedRow = 0;
        _selectedCol = 0;

        // The grid is now live (IsSwitcherActive), so Tab-stepping and a quick
        // Alt-release commit work immediately. The window itself appears only after
        // a short delay, so a fast Alt+Tab tap switches without flashing the UI.
        int delay = Math.Max(0, SettingsService.Load().PowerSwitcherShowDelayMs);
        if (delay <= 0)
        {
            RevealNow();
            return;
        }

        _showTimer = DispatcherQueue.CreateTimer();
        _showTimer.IsRepeating = false;
        _showTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _showTimer.Tick += (_, _) =>
        {
            _showTimer?.Stop();
            _showTimer = null;
            RevealNow();
        };
        _showTimer.Start();
    }

    private void RevealNow()
    {
        if (!_active || _visible) return;

        bool fade = SettingsService.Load().PowerSwitcherFadeIn;

        ShowBackdrop(fade);

        // Show centered with a provisional size, then shrink to fit the content
        // (the window is never full-screen, so it doesn't cover the desktop).
        var work = DisplayArea.Primary.WorkArea;
        _appWindow.MoveAndResize(new RectInt32(work.X + (work.Width - 760) / 2, work.Y + (work.Height - 640) / 2, 760, 640));
        _appWindow.Show(false);
        ApplyWindowChrome();
        _backdrop.PlaceBehind(_hwnd);
        _visible = true;

        RootGrid.Opacity = fade ? 0 : 1;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            RootGrid.UpdateLayout();
            SizeToContent();
            // Let the resize settle before measuring the thumbnail position,
            // otherwise the first thumbnail is offset.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                RootGrid.UpdateLayout();
                UpdateSelectionUI();
                if (fade) StartFade();
            });
        });
    }

    private void StartFade()
    {
        _fadeTimer?.Stop();
        _fadeStart = DateTime.UtcNow;
        _fadeTimer = DispatcherQueue.CreateTimer();
        _fadeTimer.IsRepeating = true;
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(15);
        _fadeTimer.Tick += (_, _) =>
        {
            double t = (DateTime.UtcNow - _fadeStart).TotalMilliseconds / FadeDurationMs;
            if (t >= 1.0)
            {
                RootGrid.Opacity = 1;
                if (_backdropShown) _backdrop.SetAlpha(_fadeBlur, _fadeTargetAlpha);
                _fadeTimer?.Stop();
                _fadeTimer = null;
                return;
            }

            RootGrid.Opacity = t;
            if (_backdropShown) _backdrop.SetAlpha(_fadeBlur, (int)(_fadeTargetAlpha * t));
        };
        _fadeTimer.Start();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
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

        // Subclass the window and zero out the non-client area on WM_NCCALCSIZE,
        // which removes the system frame/border the presenter keeps drawing.
        _wndProc = WndProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, -4 /* GWLP_WNDPROC */, _wndProc);
        // SWP_NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_NCCALCSIZE = 0x0083;
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            return IntPtr.Zero; // client area spans the whole window -> no frame
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void SizeToContent()
    {
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        double w = ContentRoot.ActualWidth;
        double h = ContentRoot.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int pw = (int)Math.Ceiling(w * scale);
        int ph = (int)Math.Ceiling(h * scale);
        var work = DisplayArea.Primary.WorkArea;
        int x = work.X + (work.Width - pw) / 2;
        int y = work.Y + (work.Height - ph) / 2;
        _appWindow.MoveAndResize(new RectInt32(x, y, pw, ph));
    }

    private void BuildGrid()
    {
        _grid.Clear();

        var workspaces = WorkspaceManager.GetWorkspacesInMruOrder();
        var allWindows = WindowManager.GetOpenWindows();
        allWindows.RemoveAll(w => w.Hwnd == _hwnd);

        IntPtr foreground = Win32.GetForegroundWindow();

        foreach (var ws in workspaces)
            _grid.Add(new WorkspaceRow { Workspace = ws });

        // Fallback when virtual-desktop info is unavailable: a single row that
        // holds every window, so the switcher still works.
        if (_grid.Count == 0)
            _grid.Add(new WorkspaceRow { Workspace = new WorkspaceItem { Id = Guid.Empty, Name = "Desktop", IsCurrent = true } });

        Guid fallback = workspaces.FirstOrDefault(w => w.IsCurrent)?.Id ?? _grid[0].Workspace.Id;

        foreach (var win in allWindows)
        {
            Guid deskId = WorkspaceManager.GetDesktopIdForWindow(win.Hwnd) ?? fallback;
            var row = _grid.FirstOrDefault(r => r.Workspace.Id == deskId);
            if (row != null)
                row.Windows.Add(win);
            else if (_grid.Count > 0)
                _grid[0].Windows.Add(win);
        }

        // Move the foreground window to the end of its row in the current desktop.
        var current = workspaces.FirstOrDefault(w => w.IsCurrent);
        if (current != null)
        {
            var currentRow = _grid.FirstOrDefault(r => r.Workspace.Id == current.Id);
            var fgWin = currentRow?.Windows.FirstOrDefault(w => w.Hwnd == foreground);
            if (currentRow != null && fgWin != null)
            {
                currentRow.Windows.Remove(fgWin);
                currentRow.Windows.Add(fgWin);
            }
        }

        _grid.RemoveAll(r => r.Windows.Count == 0);
    }

    private void RebuildVisuals()
    {
        RowsPanel.Children.Clear();
        _cells.Clear();

        for (int r = 0; r < _grid.Count; r++)
        {
            var rowData = _grid[r];
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            rowPanel.Children.Add(new TextBlock
            {
                Text = rowData.Workspace.Name,
                Width = 90,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x87, 0xFF, 0xFF, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var iconsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var borders = new List<Border>();

            for (int c = 0; c < rowData.Windows.Count; c++)
            {
                var win = rowData.Windows[c];
                var border = new Border
                {
                    Width = 56,
                    Height = 56,
                    Padding = new Thickness(8),
                    Margin = new Thickness(3, 0, 3, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = TransparentBrush,
                    Tag = (r, c),
                    Child = new Image
                    {
                        Source = _iconService.GetIcon(win.Hwnd),
                        Stretch = Stretch.Uniform,
                    },
                };
                border.PointerReleased += Cell_PointerReleased;
                iconsPanel.Children.Add(border);
                borders.Add(border);
            }

            var hscroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxWidth = 820,
                Content = iconsPanel,
            };

            rowPanel.Children.Add(hscroll);
            RowsPanel.Children.Add(rowPanel);
            _cells.Add(borders);
        }
    }

    public void MoveSelection(MoveDirection dir)
    {
        if (_grid.Count == 0) return;

        switch (dir)
        {
            case MoveDirection.Right:
                _selectedCol++;
                if (_selectedCol >= _grid[_selectedRow].Windows.Count) _selectedCol = 0;
                break;
            case MoveDirection.Left:
                _selectedCol--;
                if (_selectedCol < 0) _selectedCol = Math.Max(0, _grid[_selectedRow].Windows.Count - 1);
                break;
            case MoveDirection.Down:
                _selectedRow++;
                if (_selectedRow >= _grid.Count) _selectedRow = 0;
                _selectedCol = Math.Min(_selectedCol, Math.Max(0, _grid[_selectedRow].Windows.Count - 1));
                break;
            case MoveDirection.Up:
                _selectedRow--;
                if (_selectedRow < 0) _selectedRow = _grid.Count - 1;
                _selectedCol = Math.Min(_selectedCol, Math.Max(0, _grid[_selectedRow].Windows.Count - 1));
                break;
            case MoveDirection.Home:
                _selectedCol = 0;
                break;
            case MoveDirection.End:
                _selectedCol = Math.Max(0, _grid[_selectedRow].Windows.Count - 1);
                break;
            case MoveDirection.PageUp:
                _selectedRow = 0;
                _selectedCol = Math.Min(_selectedCol, Math.Max(0, _grid[_selectedRow].Windows.Count - 1));
                break;
            case MoveDirection.PageDown:
                _selectedRow = _grid.Count - 1;
                _selectedCol = Math.Min(_selectedCol, Math.Max(0, _grid[_selectedRow].Windows.Count - 1));
                break;
        }

        UpdateSelectionUI();
    }

    private void UpdateSelectionUI()
    {
        if (_grid.Count == 0) return;

        foreach (var row in _cells)
            foreach (var border in row)
                border.Background = TransparentBrush;

        if (_selectedRow < _cells.Count && _selectedCol < _cells[_selectedRow].Count)
        {
            var sel = _cells[_selectedRow][_selectedCol];
            sel.Background = SelectedBrush;
            sel.StartBringIntoView();
        }

        var selectedWindow = _grid[_selectedRow].Windows[_selectedCol];
        ActiveWindowText.Text = selectedWindow.Title;

        if (_visible)
            RegisterThumbnail(selectedWindow.Hwnd);
    }

    private void RegisterThumbnail(IntPtr target)
    {
        UnregisterThumbnail();

        int hr = Win32.DwmRegisterThumbnail(_hwnd, target, out _thumbnailId);
        if (hr == 0 && _thumbnailId != IntPtr.Zero)
            UpdateThumbnailSize();
    }

    private void UpdateThumbnailSize()
    {
        if (_thumbnailId == IntPtr.Zero) return;

        Win32.DwmQueryThumbnailSourceSize(_thumbnailId, out Win32.SIZE source);
        if (source.cx == 0 || source.cy == 0) return;

        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;

        var transform = ThumbnailAnchor.TransformToVisual(RootGrid);
        var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

        double anchorWidth = ThumbnailAnchor.ActualWidth;
        double anchorHeight = ThumbnailAnchor.ActualHeight;
        if (anchorWidth <= 0 || anchorHeight <= 0) return;

        double s = Math.Min(anchorWidth / source.cx, anchorHeight / source.cy);
        if (s > 1.0) s = 1.0;

        double finalWidth = source.cx * s;
        double finalHeight = source.cy * s;
        double offsetX = (anchorWidth - finalWidth) / 2.0;
        double offsetY = (anchorHeight - finalHeight) / 2.0;

        var dest = new Win32.RECT
        {
            left = (int)((origin.X + offsetX) * scale),
            top = (int)((origin.Y + offsetY) * scale),
            right = (int)((origin.X + offsetX + finalWidth) * scale),
            bottom = (int)((origin.Y + offsetY + finalHeight) * scale),
        };

        var props = new Win32.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = Win32.DWM_TNP_VISIBLE | Win32.DWM_TNP_RECTDESTINATION | Win32.DWM_TNP_OPACITY,
            fVisible = 1,
            opacity = 255,
            rcDestination = dest,
        };

        Win32.DwmUpdateThumbnailProperties(_thumbnailId, ref props);
    }

    private void UnregisterThumbnail()
    {
        if (_thumbnailId != IntPtr.Zero)
        {
            Win32.DwmUnregisterThumbnail(_thumbnailId);
            _thumbnailId = IntPtr.Zero;
        }
    }

    public void CommitSelection(bool ignoreSticky = false)
    {
        if (ignoreSticky || !_sticky)
            PerformSwitch();
    }

    private void PerformSwitch()
    {
        if (_grid.Count > 0 && _selectedRow >= 0 && _selectedCol >= 0)
        {
            var selectedWindow = _grid[_selectedRow].Windows[_selectedCol];
            var targetDeskId = _grid[_selectedRow].Workspace.Id;
            var currentDeskId = WorkspaceManager.GetWorkspacesInMruOrder().FirstOrDefault(w => w.IsCurrent)?.Id;

            HideSwitcher();

            if (currentDeskId != null && targetDeskId != currentDeskId)
                WorkspaceManager.SwitchToWorkspace(targetDeskId);

            Win32.ForceForegroundWindow(selectedWindow.Hwnd);
        }
        else
        {
            HideSwitcher();
        }
    }

    public void CloseSelection()
    {
        if (_grid.Count == 0) return;

        var selectedWindow = _grid[_selectedRow].Windows[_selectedCol];
        Win32.SendMessage(selectedWindow.Hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

        var row = _grid[_selectedRow];
        row.Windows.RemoveAt(_selectedCol);

        if (row.Windows.Count == 0)
        {
            _grid.RemoveAt(_selectedRow);
            if (_selectedRow >= _grid.Count)
                _selectedRow = Math.Max(0, _grid.Count - 1);
            _selectedCol = 0;
        }
        else if (_selectedCol >= row.Windows.Count)
        {
            _selectedCol = Math.Max(0, row.Windows.Count - 1);
        }

        if (_grid.Count == 0)
        {
            HideSwitcher();
        }
        else
        {
            RebuildVisuals();
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                RootGrid.UpdateLayout();
                SizeToContent();
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    RootGrid.UpdateLayout();
                    UpdateSelectionUI();
                });
            });
        }
    }

    public void HideSwitcher()
    {
        if (!_active) return;
        _active = false;

        _showTimer?.Stop();
        _showTimer = null;
        _fadeTimer?.Stop();
        _fadeTimer = null;

        UnregisterThumbnail();
        RowsPanel.Children.Clear();
        _cells.Clear();
        _grid.Clear();
        _sticky = false;

        if (_visible)
        {
            _appWindow.Hide();
            _backdrop.Hide();
            _visible = false;
        }

        RootGrid.Opacity = 1;
        _backdropShown = false;
    }

    private void ShowBackdrop(bool fade)
    {
        var s = SettingsService.Load();
        bool dim = s.PowerSwitcherDimEnabled;
        bool blur = s.PowerSwitcherBlurEnabled;

        if (!dim && !blur)
        {
            _backdrop.Hide();
            _backdropShown = false;
            return;
        }

        // The darkness slider drives the tint in both modes, so dim and blur stack:
        // blur on -> acrylic blur tinted by the dim amount; blur off -> a flat dim.
        int tint = dim ? Math.Clamp(s.PowerSwitcherDimAmount, 0, 100) * 255 / 100 : 0;
        _backdropShown = true;
        _fadeBlur = blur;
        _fadeTargetAlpha = tint;
        _backdrop.Show(blur, fade ? 0 : tint);
    }

    private void Cell_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, int> pos)
            return;

        var kind = e.GetCurrentPoint(border).Properties.PointerUpdateKind;

        _selectedRow = pos.Item1;
        _selectedCol = pos.Item2;
        UpdateSelectionUI();

        if (kind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased)
            CloseSelection();
        else if (kind == Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
            CommitSelection(true);
    }
}
