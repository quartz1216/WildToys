using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WildToys.Pages;

public sealed partial class PowerSwitcherPage : Page
{
    private bool _loading;

    public PowerSwitcherPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        EnabledToggle.IsOn = App.Current.Settings.IsPowerSwitcherEnabled;
        _loading = false;
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = EnabledToggle.IsOn;
        App.Current.Settings.IsPowerSwitcherEnabled = enabled;
        App.Current.SaveSettings();

        if (enabled)
            App.Current.PowerSwitcher.Start();
        else
            App.Current.PowerSwitcher.Stop();
    }
}
