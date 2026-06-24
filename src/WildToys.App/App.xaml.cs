using System;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

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

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Persists the current settings to disk.</summary>
    public void SaveSettings() => SettingsService.Save(Settings);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings = SettingsService.Load();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WildToys",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
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
        _trayIcon?.Dispose();
        _trayIcon = null;
        Exit();
    }
}
