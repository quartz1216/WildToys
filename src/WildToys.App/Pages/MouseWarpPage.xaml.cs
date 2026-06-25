using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WildToys.Pages;

public sealed partial class MouseWarpPage : Page
{
    private bool _loading;

    public MouseWarpPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;

        var s = App.Current.Settings;
        EnabledToggle.IsOn = s.IsMouseWarpEnabled;
        AnimationToggle.IsOn = s.MouseWarpAnimationEnabled;
        DurationSlider.Value = s.MouseWarpAnimationDurationMs;
        DurationLabel.Text = $"Animation duration: {s.MouseWarpAnimationDurationMs} ms";
        UpdateDurationEnabled();

        _loading = false;
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = EnabledToggle.IsOn;
        App.Current.Settings.IsMouseWarpEnabled = enabled;
        App.Current.SaveSettings();

        if (enabled)
            App.Current.MouseWarp.Start();
        else
            App.Current.MouseWarp.Stop();
    }

    private void AnimationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        App.Current.Settings.MouseWarpAnimationEnabled = AnimationToggle.IsOn;
        UpdateDurationEnabled();
        Persist();
    }

    private void DurationSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var ms = (int)e.NewValue;
        if (DurationLabel is not null)
            DurationLabel.Text = $"Animation duration: {ms} ms";

        if (_loading) return;

        App.Current.Settings.MouseWarpAnimationDurationMs = ms;
        Persist();
    }

    private void UpdateDurationEnabled()
    {
        if (DurationSlider is not null)
            DurationSlider.IsEnabled = AnimationToggle.IsOn;
    }

    // Save to disk and refresh the running module's cached settings so changes apply live.
    private static void Persist()
    {
        App.Current.SaveSettings();
        App.Current.MouseWarp.ReloadSettings();
    }
}
