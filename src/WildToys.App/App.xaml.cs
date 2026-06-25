using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WildToys.Modules.ImIndicator;
using WildToys.Modules.LumaEdges;
using WildToys.Modules.MouseGesture;
using WildToys.Modules.MouseWarp;
using WildToys.Modules.PowerSwitcher;

namespace WildToys;

/// <summary>
/// Tray-resident application. No window is shown on launch; the settings window
/// is opened on demand from the tray icon.
/// </summary>
public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

    /// <summary>Strongly-typed accessor for the running application instance.</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>The current, in-memory settings (loaded at startup).</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>The Power Switcher module (Alt+Tab replacement).</summary>
    public PowerSwitcherModule PowerSwitcher { get; } = new();

    /// <summary>The LumaEdges module (screen-edge click hotkeys).</summary>
    public LumaEdgesModule LumaEdges { get; } = new();

    /// <summary>The MouseWarp module (cursor to the activated window's center).</summary>
    public MouseWarpModule MouseWarp { get; } = new();

    /// <summary>The IM Indicator module (input-language badge near the caret).</summary>
    public ImIndicatorModule ImIndicator { get; } = new();

    /// <summary>The MouseGesture module (right/middle-button drag gestures).</summary>
    public MouseGestureModule MouseGesture { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Persists the current settings to disk.</summary>
    public void SaveSettings() => SettingsService.Save(Settings);

    /// <summary>Absolute path to a file deployed under the app's Assets folder.</summary>
    public static string AssetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool RestartAsAdmin()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch
        {
            return false; // user dismissed the UAC prompt
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings = SettingsService.Load();

        // Relaunch elevated if requested (needed to interact with elevated
        // windows like Task Manager). Only possible because the app is unpackaged.
        if (Settings.StartAsAdmin && !IsElevated() && RestartAsAdmin())
        {
            Exit();
            return;
        }

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WildToys",
            IconSource = new BitmapImage(new Uri(AssetPath("AppIcon.ico"))),
            NoLeftClickDelay = true,
            ContextMenuMode = ContextMenuMode.SecondWindow,
            LeftClickCommand = new RelayCommand(ShowSettings),
        };

        var menu = new MenuFlyout();

        var settingsItem = new MenuFlyoutItem { Text = "Settings..." };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextFlyout = menu;
        _trayIcon.ForceCreate();

        if (Settings.IsPowerSwitcherEnabled)
            PowerSwitcher.Start();

        if (Settings.IsLumaEdgesEnabled)
            LumaEdges.Start();

        if (Settings.IsMouseWarpEnabled)
            MouseWarp.Start();

        if (Settings.IsImIndicatorEnabled)
            ImIndicator.Start();

        if (Settings.IsMouseGestureEnabled)
            MouseGesture.Start();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        PowerSwitcher.Dispose();
        LumaEdges.Dispose();
        MouseWarp.Dispose();
        ImIndicator.Dispose();
        MouseGesture.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        Exit();
    }
}
