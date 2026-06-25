using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WildToys.Pages;

namespace WildToys;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = App.AssetPath("AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        AppTitleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(iconPath)) };

        ContentFrame.Navigate(typeof(GeneralPage));
        NavView.SelectedItem = NavGeneral;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        Type? page = tag switch
        {
            "GeneralPage" => typeof(GeneralPage),
            "MouseWarpPage" => typeof(MouseWarpPage),
            "PowerSwitcherPage" => typeof(PowerSwitcherPage),
            "LumaEdgesPage" => typeof(LumaEdgesPage),
            "MouseGesturePage" => typeof(MouseGesturePage),
            "ImIndicatorPage" => typeof(ImIndicatorPage),
            _ => null,
        };

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }
}
