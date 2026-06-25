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
        BlurSlider.Value = s.PowerSwitcherBlurAmount;
        BlurAmountLabel.Text = $"Blur tint: {s.PowerSwitcherBlurAmount}%";
        UpdateDimEnabled();
        UpdateBlurEnabled();

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
        UpdateBlurEnabled();
        App.Current.SaveSettings();
    }

    private void BlurSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var pct = (int)e.NewValue;
        if (BlurAmountLabel is not null)
            BlurAmountLabel.Text = $"Blur tint: {pct}%";

        if (_loading) return;

        App.Current.Settings.PowerSwitcherBlurAmount = pct;
        App.Current.SaveSettings();
    }

    private void UpdateDimEnabled()
    {
        if (DimSlider is not null)
            DimSlider.IsEnabled = DimToggle.IsOn;
    }

    private void UpdateBlurEnabled()
    {
        if (BlurSlider is not null)
            BlurSlider.IsEnabled = BlurToggle.IsOn;
    }
}
