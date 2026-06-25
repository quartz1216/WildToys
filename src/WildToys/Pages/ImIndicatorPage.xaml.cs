using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WildToys.Pages;

public sealed partial class ImIndicatorPage : Page
{
    private bool _loading;

    public ImIndicatorPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;

        var s = App.Current.Settings;
        EnabledToggle.IsOn = s.IsImIndicatorEnabled;
        DurationSlider.Value = s.ImIndicatorDurationMs;
        DurationLabel.Text = $"Display time: {s.ImIndicatorDurationMs} ms";

        _loading = false;
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = EnabledToggle.IsOn;
        App.Current.Settings.IsImIndicatorEnabled = enabled;
        App.Current.SaveSettings();

        if (enabled)
            App.Current.ImIndicator.Start();
        else
            App.Current.ImIndicator.Stop();
    }

    private void DurationSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var ms = (int)e.NewValue;
        if (DurationLabel is not null)
            DurationLabel.Text = $"Display time: {ms} ms";

        if (_loading) return;

        App.Current.Settings.ImIndicatorDurationMs = ms;
        App.Current.SaveSettings();
        App.Current.ImIndicator.ReloadSettings();
    }
}
