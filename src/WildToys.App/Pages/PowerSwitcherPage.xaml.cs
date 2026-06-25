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

        var s = App.Current.Settings;
        EnabledToggle.IsOn = s.IsPowerSwitcherEnabled;

        DimToggle.IsOn = s.PowerSwitcherDimEnabled;
        DimSlider.Value = s.PowerSwitcherDimAmount;
        DimAmountLabel.Text = $"Darkness: {s.PowerSwitcherDimAmount}%";
        BlurToggle.IsOn = s.PowerSwitcherBlurEnabled;
        DelaySlider.Value = s.PowerSwitcherShowDelayMs;
        DelayLabel.Text = $"Show delay: {s.PowerSwitcherShowDelayMs} ms";
        FadeToggle.IsOn = s.PowerSwitcherFadeIn;
        UpdateDimEnabled();

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

    private void DimToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        App.Current.Settings.PowerSwitcherDimEnabled = DimToggle.IsOn;
        UpdateDimEnabled();
        App.Current.SaveSettings();
    }

    private void DimSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var pct = (int)e.NewValue;
        if (DimAmountLabel is not null)
            DimAmountLabel.Text = $"Darkness: {pct}%";

        if (_loading) return;

        App.Current.Settings.PowerSwitcherDimAmount = pct;
        App.Current.SaveSettings();
    }

    private void BlurToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        App.Current.Settings.PowerSwitcherBlurEnabled = BlurToggle.IsOn;
        App.Current.SaveSettings();
    }

    private void DelaySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var ms = (int)e.NewValue;
        if (DelayLabel is not null)
            DelayLabel.Text = $"Show delay: {ms} ms";

        if (_loading) return;

        App.Current.Settings.PowerSwitcherShowDelayMs = ms;
        App.Current.SaveSettings();
    }

    private void FadeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        App.Current.Settings.PowerSwitcherFadeIn = FadeToggle.IsOn;
        App.Current.SaveSettings();
    }

    private void UpdateDimEnabled()
    {
        if (DimSlider is not null)
            DimSlider.IsEnabled = DimToggle.IsOn;
    }
}
