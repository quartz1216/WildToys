using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WildToys.Pages;

public sealed partial class LumaEdgesPage : Page
{
    private bool _loading;

    public LumaEdgesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;

        var s = App.Current.Settings;
        EnabledToggle.IsOn = s.IsLumaEdgesEnabled;

        ThicknessSlider.Value = s.LumaEdgesThickness;
        ThicknessLabel.Text = $"Edge thickness: {s.LumaEdgesThickness} px";

        HoverToggle.IsOn = s.LumaEdgesHoverEnabled;
        HoverDelaySlider.Value = s.LumaEdgesHoverDelayMs;
        HoverDelayLabel.Text = $"Activation delay: {s.LumaEdgesHoverDelayMs} ms";

        LoadZones(s.LumaEdgesHoverZones, TxtHoverTop, TxtHoverBottom, TxtHoverLeft, TxtHoverRight,
            TxtHoverTopLeft, TxtHoverTopRight, TxtHoverBottomLeft, TxtHoverBottomRight);
        LoadZones(s.LumaEdgesRightZones, TxtRightTop, TxtRightBottom, TxtRightLeft, TxtRightRight,
            TxtRightTopLeft, TxtRightTopRight, TxtRightBottomLeft, TxtRightBottomRight);
        LoadZones(s.LumaEdgesLeftZones, TxtLeftTop, TxtLeftBottom, TxtLeftLeft, TxtLeftRight,
            TxtLeftTopLeft, TxtLeftTopRight, TxtLeftBottomLeft, TxtLeftBottomRight);
        LoadZones(s.LumaEdgesMiddleZones, TxtMiddleTop, TxtMiddleBottom, TxtMiddleLeft, TxtMiddleRight,
            TxtMiddleTopLeft, TxtMiddleTopRight, TxtMiddleBottomLeft, TxtMiddleBottomRight);

        _loading = false;
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = EnabledToggle.IsOn;
        App.Current.Settings.IsLumaEdgesEnabled = enabled;
        App.Current.SaveSettings();

        if (enabled)
            App.Current.LumaEdges.Start();
        else
            App.Current.LumaEdges.Stop();
    }

    private void ThicknessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var px = (int)e.NewValue;
        if (ThicknessLabel is not null)
            ThicknessLabel.Text = $"Edge thickness: {px} px";

        if (_loading) return;

        App.Current.Settings.LumaEdgesThickness = px;
        Persist();
    }

    private void HoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        App.Current.Settings.LumaEdgesHoverEnabled = HoverToggle.IsOn;
        Persist();
    }

    private void HoverDelaySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var ms = (int)e.NewValue;
        if (HoverDelayLabel is not null)
            HoverDelayLabel.Text = $"Activation delay: {ms} ms";

        if (_loading) return;

        App.Current.Settings.LumaEdgesHoverDelayMs = ms;
        Persist();
    }

    private void Zone_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SaveZones();
    }

    private void SaveZones()
    {
        var s = App.Current.Settings;
        s.LumaEdgesHoverZones = CollectZones(TxtHoverTop, TxtHoverBottom, TxtHoverLeft, TxtHoverRight,
            TxtHoverTopLeft, TxtHoverTopRight, TxtHoverBottomLeft, TxtHoverBottomRight);
        s.LumaEdgesRightZones = CollectZones(TxtRightTop, TxtRightBottom, TxtRightLeft, TxtRightRight,
            TxtRightTopLeft, TxtRightTopRight, TxtRightBottomLeft, TxtRightBottomRight);
        s.LumaEdgesLeftZones = CollectZones(TxtLeftTop, TxtLeftBottom, TxtLeftLeft, TxtLeftRight,
            TxtLeftTopLeft, TxtLeftTopRight, TxtLeftBottomLeft, TxtLeftBottomRight);
        s.LumaEdgesMiddleZones = CollectZones(TxtMiddleTop, TxtMiddleBottom, TxtMiddleLeft, TxtMiddleRight,
            TxtMiddleTopLeft, TxtMiddleTopRight, TxtMiddleBottomLeft, TxtMiddleBottomRight);
        Persist();
    }

    // Save to disk and refresh the running module's cached settings so changes apply live.
    private static void Persist()
    {
        App.Current.SaveSettings();
        App.Current.LumaEdges.ReloadSettings();
    }

    // Order matches the textbox parameter order below.
    private static readonly string[] ZoneKeys =
        { "Top", "Bottom", "Left", "Right", "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

    private static void LoadZones(Dictionary<string, string> map, params TextBox[] boxes)
    {
        for (int i = 0; i < ZoneKeys.Length; i++)
            boxes[i].Text = map.TryGetValue(ZoneKeys[i], out var v) ? v : string.Empty;
    }

    private static Dictionary<string, string> CollectZones(params TextBox[] boxes)
    {
        var map = new Dictionary<string, string>();
        for (int i = 0; i < ZoneKeys.Length; i++)
        {
            var text = boxes[i].Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                map[ZoneKeys[i]] = text;
        }
        return map;
    }
}
