using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WildToys.Pages;

public sealed partial class GeneralPage : Page
{
    private bool _loading;

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var settings = App.Current.Settings;
        StartWithWindowsToggle.IsOn = settings.StartWithWindows;
        StartAsAdminToggle.IsOn = settings.StartAsAdmin;
        _loading = false;
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var settings = App.Current.Settings;
        settings.StartWithWindows = StartWithWindowsToggle.IsOn;
        StartupService.SetRunAtStartup(settings.StartWithWindows);
        App.Current.SaveSettings();
    }

    private void StartAsAdminToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var settings = App.Current.Settings;
        settings.StartAsAdmin = StartAsAdminToggle.IsOn;
        App.Current.SaveSettings();
    }
}
